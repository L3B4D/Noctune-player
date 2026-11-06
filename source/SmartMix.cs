using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal sealed class MixAnalysis {
    public double Bpm{get;set;}public double Confidence{get;set;}public long IntroMs{get;set;}public long OutroMs{get;set;}public long LengthMs{get;set;}
}
internal static class SmartMix {
    // 20 ms RMS/onset envelope; bounded to 20 minutes. No playback-thread DSP.
    internal static MixAnalysis Analyze(short[] pcm){
        const int hop=160;int count=pcm.Length/hop;var result=new MixAnalysis{LengthMs=pcm.Length/8,OutroMs=pcm.Length/8};if(count<100)return result;
        var energy=new double[count];double peak=0;
        for(int i=0;i<count;i++){double sum=0;for(int j=0;j<hop;j++){double sample=pcm[i*hop+j]/32768.0;sum+=sample*sample;}energy[i]=Math.Sqrt(sum/hop);peak=Math.Max(peak,energy[i]);}
        if(peak<.0001)return result;
        double floor=Math.Max(.0003,peak*.015);int first=Array.FindIndex(energy,e=>e>floor),last=Array.FindLastIndex(energy,e=>e>floor);
        result.IntroMs=Math.Min(10000,Math.Max(0,first*20-100));result.OutroMs=Math.Max(result.LengthMs-30000,Math.Min(result.LengthMs,(last+1)*20+150));
        // A sustained quiet tail can be used as an exit cue; never cut an early quiet verse.
        int start=Math.Max((int)(count*.80),count-1500);double body=energy.Skip(count/4).Take(Math.Max(1,count/2)).Average();
        for(int i=start;i<count-200;i++){if(energy.Skip(i).Take(200).Average()<body*.18&&energy.Skip(i+200).DefaultIfEmpty(0).Average()<body*.22){result.OutroMs=Math.Min(result.OutroMs,i*20);break;}}
        int frames=Math.Min(count,6000);var flux=new double[frames];for(int i=1;i<frames;i++)flux[i]=Math.Max(0,energy[i]-energy[i-1]);
        double best=0,second=0;int lagBest=0;
        for(int lag=15;lag<=60;lag++){double dot=0,a=0,b=0;for(int i=lag;i<frames;i++){dot+=flux[i]*flux[i-lag];a+=flux[i]*flux[i];b+=flux[i-lag]*flux[i-lag];}double score=a*b>0?dot/Math.Sqrt(a*b):0;if(score>best){second=best;best=score;lagBest=lag;}else second=Math.Max(second,score);}
        if(best>.24&&lagBest>0){result.Bpm=3000.0/lagBest;result.Confidence=best;}
        return result;
    }
    internal static int Duration(MixAnalysis a,MixAnalysis b,int fallback){
        if(a==null||b==null||a.Confidence<.35||b.Confidence<.35)return fallback;
        double ratio=b.Bpm/a.Bpm;while(ratio>1.5)ratio/=2;while(ratio<.67)ratio*=2;
        int beats=Math.Abs(1-ratio)<=.07?8:16;return (int)Math.Max(2000,Math.Min(12000,beats*60000/a.Bpm));
    }
    internal static float Tempo(MixAnalysis a,MixAnalysis b){
        if(a==null||b==null||a.Bpm<=0||b.Bpm<=0||a.Confidence<.5||b.Confidence<.5)return 1;
        double ratio=a.Bpm/b.Bpm;while(ratio>1.5)ratio/=2;while(ratio<.67)ratio*=2;return Math.Abs(ratio-1)<=.04?(float)ratio:1;
    }
}

internal static class MixDecoder {
    const string Lib="libvlc.dll";
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr libvlc_new(int n,IntPtr args);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr libvlc_media_new_location(IntPtr v,byte[] url);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void libvlc_media_add_option(IntPtr m,byte[] option);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern IntPtr libvlc_media_player_new_from_media(IntPtr m);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern int libvlc_media_player_play(IntPtr p);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern int libvlc_media_player_get_state(IntPtr p);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void libvlc_media_player_stop(IntPtr p);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void libvlc_media_player_release(IntPtr p);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void libvlc_media_release(IntPtr p);
    [DllImport(Lib,CallingConvention=CallingConvention.Cdecl)]static extern void libvlc_release(IntPtr v);
    static readonly SemaphoreSlim slots=new SemaphoreSlim(1,1);
    static readonly Dictionary<string,MixAnalysis> cache=new Dictionary<string,MixAnalysis>();
    static byte[] U(string s){return Encoding.UTF8.GetBytes(s+"\0");}
    internal static async Task<MixAnalysis> Load(string file,CancellationToken token){
        if(string.IsNullOrEmpty(file)||!File.Exists(file))return null;var info=new FileInfo(file);string key=info.FullName+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;
        await slots.WaitAsync(token).ConfigureAwait(false);try{MixAnalysis saved;if(cache.TryGetValue(key,out saved))return saved;
            saved=await Task.Run(()=>Decode(file,token),token).ConfigureAwait(false);if(saved!=null){if(cache.Count>=64)cache.Clear();cache[key]=saved;}return saved;
        }finally{slots.Release();}
    }
    static MixAnalysis Decode(string file,CancellationToken token){
        string tmp=Path.Combine(Path.GetTempPath(),"yuma-analysis-"+Guid.NewGuid().ToString("N")+".pcm");IntPtr v=IntPtr.Zero,m=IntPtr.Zero,p=IntPtr.Zero;
        try{
            v=libvlc_new(0,IntPtr.Zero);if(v==IntPtr.Zero)return null;m=libvlc_media_new_location(v,U(new Uri(Path.GetFullPath(file)).AbsoluteUri));if(m==IntPtr.Zero)return null;
            libvlc_media_add_option(m,U(":no-video"));libvlc_media_add_option(m,U(":sout=#transcode{acodec=s16l,channels=1,samplerate=8000}:std{access=file,mux=raw,dst='"+tmp.Replace("\\","/").Replace("'","\\'")+"'}"));
            libvlc_media_add_option(m,U(":sout-keep"));p=libvlc_media_player_new_from_media(m);if(p==IntPtr.Zero||libvlc_media_player_play(p)<0)return null;
            var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<30000){token.ThrowIfCancellationRequested();int state=libvlc_media_player_get_state(p);if(state==6)break;if(state==7)return null;if(File.Exists(tmp)&&new FileInfo(tmp).Length>19200000)return null;Thread.Sleep(25);}
            if(libvlc_media_player_get_state(p)!=6)return null;libvlc_media_player_stop(p);libvlc_media_player_release(p);p=IntPtr.Zero;
            if(!File.Exists(tmp))return null;var bytes=File.ReadAllBytes(tmp);if(bytes.Length>19200000)return null;var pcm=new short[bytes.Length/2];Buffer.BlockCopy(bytes,0,pcm,0,pcm.Length*2);return SmartMix.Analyze(pcm);
        }finally{if(p!=IntPtr.Zero){libvlc_media_player_stop(p);libvlc_media_player_release(p);}if(m!=IntPtr.Zero)libvlc_media_release(m);if(v!=IntPtr.Zero)libvlc_release(v);try{File.Delete(tmp);}catch(IOException){}}
    }
}

internal partial class Player {
    MixAnalysis currentMix,nextMix;string analyzedPair;CancellationTokenSource analysisRequest;
    void AnalyzeMixPair(Track next){
        if(!featuresState.SmartAutomix)return;string key=Key(Current)+"|"+Key(next)+"|"+OfflinePath(Current)+"|"+OfflinePath(next);if(key==analyzedPair)return;analyzedPair=key;
        if(analysisRequest!=null)analysisRequest.Cancel();var req=new CancellationTokenSource(TimeSpan.FromSeconds(65));analysisRequest=req;
        string a=Current.File??OfflinePath(Current),b=next.File??OfflinePath(next);currentMix=nextMix=null;
        AnalyzeMixPairAsync(a,b,key,req);
    }
    async void AnalyzeMixPairAsync(string a,string b,string key,CancellationTokenSource req){
        try{var first=await MixDecoder.Load(a,req.Token);var second=await MixDecoder.Load(b,req.Token);if(!closing&&!req.IsCancellationRequested&&analyzedPair==key){currentMix=first;nextMix=second;}}
        catch(Exception ex){Debug.WriteLine("Mix analysis: "+ex.GetType().Name);}finally{if(analysisRequest==req)analysisRequest=null;req.Dispose();}
    }
    int PlannedFade {get{int duration=featuresState.SmartAutomix?SmartMix.Duration(currentMix,nextMix,featuresState.Crossfade*1000):featuresState.Crossfade*1000;return audio.Length>0?(int)Math.Min(duration,audio.Length/3):duration;}}
}
