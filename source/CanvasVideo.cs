using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class CanvasCache {
    internal static readonly string Root=Path.Combine(Catalog.Root,"data","canvas-cache");
    static readonly HttpClient http=new HttpClient{Timeout=TimeSpan.FromSeconds(10),MaxResponseContentBufferSize=1000000};
    internal static string Name(Track track){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes((track.File??track.BrowseId??track.Id??("q:"+track.QobuzId))+"|"+track.Title+"|"+track.Artist))).Replace("-","");}
    internal static string Existing(Track track){string stem=Name(track);if(!Directory.Exists(Root))return null;return Directory.GetFiles(Root,stem+".*").FirstOrDefault(p=>!p.Contains(".part.")&&(p.EndsWith(".mp4")||p.EndsWith(".webm")));}
    internal static string Url(string file){return file==null?null:"https://yuma-assets.local/"+Uri.EscapeDataString(Path.GetFileName(file));}
    internal static void Trim(){if(!Directory.Exists(Root))return;var files=new DirectoryInfo(Root).GetFiles().Where(f=>f.Extension==".mp4"||f.Extension==".webm").OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();long bytes=0;foreach(var f in files){bytes+=f.Length;if(bytes>512L*1024*1024)try{f.Delete();}catch(IOException){}}}
    static string NormalizeArtist(string text){return System.Text.RegularExpressions.Regex.Replace((text??" ").Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"[^\p{L}\p{N}]","");}
    internal static async Task<string> Find(Track track,CancellationToken token){
        var found=Existing(track);if(found!=null)return found;if(string.IsNullOrWhiteSpace(track.Artist)||!FfmpegAudio.Available)return null;
        foreach(string host in new[]{"https://artwork-archivetune.koiiverse.cloud/","https://artwork.boidu.dev/"}){
            object data=null;try{string json=await http.GetStringAsync(host+"?s="+Uri.EscapeDataString(track.Title??"")+"&a="+Uri.EscapeDataString(track.Artist)+"&storefront=us").ConfigureAwait(false);token.ThrowIfCancellationRequested();data=Catalog.Json().DeserializeObject(json);}catch(OperationCanceledException){throw;}catch(HttpRequestException){continue;}catch(ArgumentException){continue;}
            string returnedArtist=Convert.ToString(Catalog.Field(data,"artist"));if(string.IsNullOrWhiteSpace(returnedArtist)||NormalizeArtist(returnedArtist)!=NormalizeArtist(track.Artist))continue;string address=Convert.ToString(Catalog.Field(data,"animated"));if(address.Length==0)address=Convert.ToString(Catalog.Field(data,"videoUrl"));Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.IsLoopback)continue;
            Directory.CreateDirectory(Root);string file=Path.Combine(Root,Name(track)+".mp4"),tmp=file+".part.mp4";
            using(var child=new Process{StartInfo=new ProcessStartInfo(FfmpegAudio.Executable,"-hide_banner -nostdin -loglevel error -protocol_whitelist http,https,tcp,tls,crypto -rw_timeout 10000000 -i \""+uri.AbsoluteUri.Replace("\"","%22")+"\" -t 30 -map 0:v:0 -an -c copy -movflags +faststart -y \""+tmp+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}}){
                try{child.Start();var error=child.StandardError.ReadToEndAsync();var watch=Stopwatch.StartNew();while(!child.HasExited){token.ThrowIfCancellationRequested();if(watch.ElapsedMilliseconds>45000||File.Exists(tmp)&&new FileInfo(tmp).Length>128L*1024*1024)throw new IOException("Canvas exceeds its download budget.");await Task.Delay(100,token).ConfigureAwait(false);}await error.ConfigureAwait(false);if(child.ExitCode==0&&File.Exists(tmp)&&new FileInfo(tmp).Length>0){File.Move(tmp,file);Trim();return file;}}
                finally{try{if(!child.HasExited)child.Kill();}catch(InvalidOperationException){}if(File.Exists(tmp))try{File.Delete(tmp);}catch(IOException){}}
            }
        }return null;
    }
}
internal partial class Player {
    string canvasUrl;CancellationTokenSource canvasRequest;
    async void RefreshCanvas(Track track){canvasUrl=null;if(canvasRequest!=null)canvasRequest.Cancel();if(!featuresState.CanvasVideo)return;string key=Key(track);var request=new CancellationTokenSource(TimeSpan.FromSeconds(65));canvasRequest=request;
        try{string path=await CanvasCache.Find(track,request.Token);if(!closing&&!request.IsCancellationRequested&&key==Key(Current))canvasUrl=CanvasCache.Url(path);}catch(Exception ex){System.Diagnostics.Debug.WriteLine("Canvas: "+ex.GetType().Name);}finally{if(canvasRequest==request)canvasRequest=null;request.Dispose();}
    }
    void ImportCanvas(){using(var dialog=new OpenFileDialog{Title="Choose a video backdrop for this song",Filter="Video loops|*.mp4;*.webm"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;try{if(new FileInfo(dialog.FileName).Length>128L*1024*1024)throw new IOException("Choose a loop smaller than 128 MB.");Directory.CreateDirectory(CanvasCache.Root);string file=Path.Combine(CanvasCache.Root,CanvasCache.Name(Current)+Path.GetExtension(dialog.FileName).ToLowerInvariant());File.Copy(dialog.FileName,file,true);featuresState.CanvasVideo=true;SaveFeatures();canvasUrl=CanvasCache.Url(file);CanvasCache.Trim();}catch(IOException ex){DialogUI.Show(this,ex.Message,"Video backdrop");}}}
}
