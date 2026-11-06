using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
internal static class LyricPronunciation {
    internal static async Task<string[]> Generate(string[] lines,CancellationToken token){
        string root=Path.Combine(Catalog.Root,"tools"),script=Path.Combine(root,"pronunciation","convert.js");
        using(var process=new Process{StartInfo=new ProcessStartInfo(Path.Combine(root,"node.exe"),"--max-old-space-size=256 \""+script+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}}){
            process.Start();using(token.Register(delegate{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}})){
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                byte[] input=Encoding.UTF8.GetBytes(Catalog.Json().Serialize(lines));await process.StandardInput.BaseStream.WriteAsync(input,0,input.Length,token).ConfigureAwait(false);process.StandardInput.Close();
                string text=await output.ConfigureAwait(false);await error.ConfigureAwait(false);await Task.Run(()=>process.WaitForExit()).ConfigureAwait(false);token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0||text.Length>2000000)throw new IOException("Pronunciation dictionary unavailable.");
                var result=Catalog.Json().Deserialize<string[]>(text);if(result==null||result.Length!=lines.Length)throw new IOException("Invalid pronunciation result.");return result;
            }
        }
    }
}
internal partial class Player {
    bool pronunciationBusy;
    async void GeneratePronunciation(){
        if(pronunciationBusy||richLyrics.Count==0)return;pronunciationBusy=true;int version=lyricVersion;string key=lyricKey,previous=lyricSource.Text;lyricSource.Text="Generating Japanese / Korean pronunciation locally…";
        using(var request=new CancellationTokenSource(TimeSpan.FromSeconds(45)))try{
            var result=await LyricPronunciation.Generate(richLyrics.Select(l=>l.Text).ToArray(),request.Token);if(closing||version!=lyricVersion||key!=lyricKey)return;
            lyricRows.BeginUpdate();try{for(int i=0;i<result.Length;i++){var line=richLyrics[i];if(string.IsNullOrWhiteSpace(line.Pronunciation))line.Pronunciation=result[i];lyricRows.Items[i]=line.Text+(string.IsNullOrWhiteSpace(line.Pronunciation)?"":"\n"+line.Pronunciation)+(string.IsNullOrWhiteSpace(line.Translation)?"":"\n"+line.Translation);}}finally{lyricRows.EndUpdate();lyricRows.Reflow();}
            gpuLyricRevision++;lyricSource.Text=previous+" · Local pronunciation";
        }catch(Exception){if(!closing&&version==lyricVersion){lyricSource.Text=previous;DialogUI.Show(this,"Pronunciation could not finish. Original lyrics and timing are unchanged.","Pronunciation");}}finally{pronunciationBusy=false;}
    }
}
