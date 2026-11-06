using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal sealed class FfmpegAudio:IDisposable {
    internal static readonly string Executable=Path.Combine(Catalog.Root,"tools","ffmpeg","ffmpeg.exe");
    internal static bool Available {get{return File.Exists(Executable)&&PcmOutput.Shared.Available;}}
    readonly PcmDeck deck;readonly object gate=new object();CancellationTokenSource cancel;Process process;string url;long length;float rate=1,pitch;int version;
    internal volatile bool Failed;internal bool Active;internal long Length {get{return Interlocked.Read(ref length);}}internal float Rate {get{return rate;}}
    internal FfmpegAudio(PcmDeck deck){this.deck=deck;}
    static string Q(string s){return "\""+Regex.Replace(s,"(\\\\*)(\"|$)",m=>m.Groups[1].Value+m.Groups[1].Value+(m.Groups[2].Length>0?"\\\"":""))+"\"";}
    internal void ExpectedLength(long ms){if(ms>0&&Length==0)Interlocked.Exchange(ref length,ms);}
    internal void Start(string source,long position,bool held){deck.Paused=false;url=source;Interlocked.Exchange(ref length,0);Restart(position,held);}
    internal void SetRate(float value){value=Math.Max(.5f,Math.Min(2,value));if(Math.Abs(rate-value)<.0001)return;rate=value;if(Active)Restart(deck.Time,deck.Hold);}
    internal void SetPitch(float value){value=Math.Max(-12,Math.Min(12,value));if(Math.Abs(pitch-value)<.0001)return;pitch=value;if(Active)Restart(deck.Time,deck.Hold);}
    internal void Seek(long position){if(Active)Restart(Math.Max(0,position),deck.Hold);}
    void Restart(long position,bool held){bool paused=deck.Paused;Stop();Active=true;Failed=false;deck.Reset(position,held);deck.Paused=paused;deck.Rate=rate;int generation=deck.Generation,stamp=++version;var request=new CancellationTokenSource();cancel=request;Task.Run(()=>Run(url,position,rate,pitch,generation,stamp,request));}
    async Task Run(string source,long position,float tempo,float semitones,int generation,int stamp,CancellationTokenSource request){
        Process child=null;Task input=null;
        try{
            var ranged=RangeAudio.Find(source);Uri uri;if(Uri.TryCreate(source,UriKind.Absolute,out uri)&&uri.IsFile)source=uri.LocalPath;
            string seek=" -ss "+(position/1000.0).ToString("0.000",CultureInfo.InvariantCulture);
            string filters="aresample=48000";if(Math.Abs(tempo-1)>.0001||Math.Abs(semitones)>.0001)filters+=",rubberband=tempo="+tempo.ToString("0.0000",CultureInfo.InvariantCulture)+":pitch="+Math.Pow(2,semitones/12.0).ToString("0.000000",CultureInfo.InvariantCulture);
            string args="-hide_banner -nostdin -nostats -loglevel info -threads 2"+(ranged==null&&position>0?seek:"")+(ranged==null&&uri!=null&&!uri.IsFile?" -rw_timeout 15000000":"")+" -i "+Q(ranged==null?source:"pipe:0")+(ranged!=null&&position>0?seek:"")+" -map 0:a:0 -vn -sn -dn -af "+Q(filters)+" -ac 2 -ar 48000 -f f32le -acodec pcm_f32le pipe:1";
            child=new Process{StartInfo=new ProcessStartInfo(Executable,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=ranged!=null}};
            child.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(stamp!=version||e.Data==null)return;var m=Regex.Match(e.Data,@"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");if(m.Success){double seconds=double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*3600+double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture)*60+double.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture);Interlocked.Exchange(ref length,(long)(seconds*1000));}};
            request.Token.ThrowIfCancellationRequested();child.Start();lock(gate){if(request.IsCancellationRequested){child.Kill();return;}process=child;}child.BeginErrorReadLine();
            if(ranged!=null)input=Task.Run(async delegate{try{await ranged.CopyEncoded(child.StandardInput.BaseStream,request.Token);}catch(Exception){try{child.StandardInput.Close();}catch(Exception){}}finally{try{child.StandardInput.Close();}catch(Exception){}}});
            using(request.Token.Register(delegate{try{if(!child.HasExited)child.Kill();}catch(InvalidOperationException){}})){
                var bytes=new byte[16384];var samples=new float[4096];int saved=0;
                while(true){int n=await child.StandardOutput.BaseStream.ReadAsync(bytes,saved,bytes.Length-saved,request.Token).ConfigureAwait(false);if(n==0)break;n+=saved;int complete=n-n%8;Buffer.BlockCopy(bytes,0,samples,0,complete);deck.PushManaged(samples,complete/2/4,generation);saved=n-complete;if(saved>0)Buffer.BlockCopy(bytes,complete,bytes,0,saved);request.Token.ThrowIfCancellationRequested();}
                await Task.Run(()=>child.WaitForExit()).ConfigureAwait(false);if(stamp==version){Failed=child.ExitCode!=0;deck.Ended=true;}
            }
        }catch(OperationCanceledException){}catch(Exception){if(stamp==version){Failed=true;deck.Ended=true;}}
        finally{if(child!=null){lock(gate)if(process==child)process=null;try{if(!child.HasExited)child.Kill();}catch(Exception){}Observe(input);child.Dispose();}request.Dispose();}
    }
    internal static void Observe(Task task){if(task!=null)task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
    internal void Stop(){version++;Active=false;deck.Stop();var c=cancel;cancel=null;if(c!=null)try{c.Cancel();}catch(ObjectDisposedException){}lock(gate){if(process!=null)try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}}}
    public void Dispose(){Stop();}
}
