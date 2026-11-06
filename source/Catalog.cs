using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public class Track {
    public string Artwork { get; set; }
    public DateTime AddedAtUtc {get;set;} public int DurationSeconds {get;set;} public string Section {get;set;} public int Year {get;set;}
    public string BrowseParams {get;set;} public string Kind {get;set;} public string BrowseId {get;set;} public string ArtistId {get;set;} public string AlbumId {get;set;} public string Album {get;set;}
    public long QobuzId { get; set; }
    public string Title { get; set; }
    public string Artist { get; set; }
    public string Id { get; set; }
    public string File { get; set; }
}

internal static partial class Catalog {
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
    internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 16000000, RecursionLimit = 240 }; }
    internal static object Field(object obj, string key) {
        var dict = obj as IDictionary<string,object>;
        return dict != null && dict.ContainsKey(key) ? dict[key] : null;
    }
    internal static string BestThumbnail(IList thumbs){
        string best=null;long area=-1;
        if(thumbs!=null)foreach(var thumb in thumbs){
            long w,h;long.TryParse(Convert.ToString(Field(thumb,"width")),out w);long.TryParse(Convert.ToString(Field(thumb,"height")),out h);
            string url=Convert.ToString(Field(thumb,"url"));if(url.Length>0&&w*h>=area){area=w*h;best=url;}
        }
        return best;
    }
    static object First(object obj) { var a = obj as IList; return a != null && a.Count > 0 ? a[0] : null; }
    static string Column(object row, int i) {
        var cols = Field(row,"flexColumns") as IList;
        if (cols == null || cols.Count <= i) return "";
        var runs = Field(Field(Field(cols[i],"musicResponsiveListItemFlexColumnRenderer"),"text"),"runs");
        return Convert.ToString(Field(First(runs),"text"));
    }
    static void Collect(object node, List<Track> tracks, HashSet<string> seen) {
        var dict = node as IDictionary<string,object>;
        if (dict != null) {
            var row = Field(node,"musicResponsiveListItemRenderer");
            if (row != null) {
                string id = Convert.ToString(Field(Field(row,"playlistItemData"),"videoId"));
                string name = Column(row,0);
                var thumbs=Field(Field(Field(Field(row,"thumbnail"),"musicThumbnailRenderer"),"thumbnail"),"thumbnails") as IList;
                string art=BestThumbnail(thumbs);
                if (Valid(id) && name.Length > 0 && seen.Add(id))
                    {var track=new Track { Title=name, Artist=Column(row,1), Id=id, Artwork=art };Metadata(track,row);tracks.Add(track);}
                return;
            }
            foreach (var value in dict.Values) Collect(value,tracks,seen);
        } else if (node is IList) foreach (var value in (IList)node) Collect(value,tracks,seen);
    }
    static bool Valid(string id) { return id != null && Regex.IsMatch(id,@"^[A-Za-z0-9_-]{11}$"); }
    internal static async Task<List<Track>> Search(string query, CancellationToken token) {
        var tracks = new List<Track>();
        var seen = new HashSet<string>();
        var body = new { context = new { client = new { clientName="WEB_REMIX", clientVersion="1.20260213.01.00", hl="en" } }, query=query, @params="EgWKAQIIAWoKEAkQBRAKEAMQBA%3D%3D" };
        try {
            using (var content = new StringContent(Json().Serialize(body),Encoding.UTF8,"application/json"))
            using (var response = await Http.PostAsync("https://music.youtube.com/youtubei/v1/search?prettyPrint=false", content,token).ConfigureAwait(false)) {
                response.EnsureSuccessStatusCode();
                Collect(Json().DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false)),tracks,seen);
            }
        } catch (Exception e) {
            token.ThrowIfCancellationRequested();
            if (!(e is HttpRequestException || e is TaskCanceledException || e is ArgumentException || e is InvalidOperationException)) throw;
        }
        if (tracks.Count > 0) return tracks;
        string lines = await Tool(token,"--flat-playlist","--dump-json","ytsearch15:"+query).ConfigureAwait(false);
        foreach (string line in lines.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries)) {
            var item=Json().DeserializeObject(line); string id=Convert.ToString(Field(item,"id"));
            if (Valid(id) && seen.Add(id)) tracks.Add(new Track { Id=id, Title=Convert.ToString(Field(item,"title")), DurationSeconds=Convert.ToInt32(Field(item,"duration")??0), Artist=Convert.ToString(Field(item,"uploader")), Artwork=BestThumbnail(Field(item,"thumbnails") as IList)??Convert.ToString(Field(item,"thumbnail")) });
        }
        return tracks;
    }
    static string Quote(string s) {
        return "\"" + Regex.Replace(s, "(\\\\*)(\"|$)", m => m.Groups[1].Value + m.Groups[1].Value + (m.Groups[2].Length>0 ? "\\\"" : "")) + "\"";
    }
    static Task<string> Tool(CancellationToken token, params string[] args) {return ToolProgress(token,null,args);}
    static Task<string> ToolProgress(CancellationToken token,Action<string> progress,params string[] args) {
        return Task.Run(async delegate {
            token.ThrowIfCancellationRequested();
            var all=new List<string> { "--ignore-config","--no-playlist","--socket-timeout","15","--retries","1","--extractor-retries","1","--js-runtimes","node:"+Path.Combine(Root,"tools","node.exe") };

            all.AddRange(args);
            using (var p=new Process()) {
                p.StartInfo=new ProcessStartInfo(Path.Combine(Root,"tools","resolver","yt-dlp.exe"),string.Join(" ",all.ConvertAll(Quote))) {
                    UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true,
                    StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
                p.StartInfo.Arguments="--encoding utf-8 "+p.StartInfo.Arguments;
                p.Start();
                using(token.Register(delegate { try { p.Kill(); } catch(InvalidOperationException) {} })) {
                    var output=progress==null?p.StandardOutput.ReadToEndAsync():ReadProgress(p,progress); var errors=p.StandardError.ReadToEndAsync();
                    await Task.WhenAll(output,errors).ConfigureAwait(false);
                    p.WaitForExit(); token.ThrowIfCancellationRequested();
                    if (p.ExitCode!=0) {
                        string msg=errors.Result.Trim(); int start=msg.LastIndexOf("ERROR:",StringComparison.Ordinal);
                        if(start>=0) msg=msg.Substring(start+6).Trim();
                        throw new IOException(msg.Length>450 ? msg.Substring(0,450) : msg);
                    }
                    return output.Result.Trim();
                }
            }
        },token);
    }
    sealed class CachedAudio {
        internal string Url;
        internal DateTime Until;
    }
    static readonly Dictionary<string,CachedAudio> audioLinks=new Dictionary<string,CachedAudio>();
    internal static void Forget(string id){
        if(id!=null)lock(audioLinks)audioLinks.Remove(id);
    }
    static string Cached(string id){
        lock(audioLinks){
            CachedAudio entry;
            if(audioLinks.TryGetValue(id,out entry)&&entry.Until>DateTime.UtcNow)return entry.Url;
            audioLinks.Remove(id);return null;
        }
    }
    static void RememberUrl(string id,Uri url){
        var until=DateTime.UtcNow.AddMinutes(10);
        var match=Regex.Match(url.Query,@"[?&]expire=(\d+)");
        long expiry;
        if(match.Success&&long.TryParse(match.Groups[1].Value,out expiry)&&expiry>0&&expiry<253402300799L){
            var limit=DateTimeOffset.FromUnixTimeSeconds(expiry).UtcDateTime.AddMinutes(-1);
            if(limit<until)until=limit;
        }
        if(until<=DateTime.UtcNow)return;
        lock(audioLinks){
            if(audioLinks.Count>=64){
                string oldest=null;var first=DateTime.MaxValue;
                foreach(var item in audioLinks)if(item.Value.Until<first){oldest=item.Key;first=item.Value.Until;}
                if(oldest!=null)audioLinks.Remove(oldest);
            }
            audioLinks[id]=new CachedAudio{Url=url.AbsoluteUri,Until=until};
        }
    }
    static bool MediaHost(Uri url){
        return url.Scheme==Uri.UriSchemeHttps&&url.Host.EndsWith(".googlevideo.com",StringComparison.OrdinalIgnoreCase);
    }
    static async Task<Uri> DirectAudio(string id,CancellationToken token){
        using(var budget=CancellationTokenSource.CreateLinkedTokenSource(token)){
            budget.CancelAfter(TimeSpan.FromSeconds(3));
            try{
                var body=new { context=new { client=new { clientName="IOS",clientVersion="21.02.3",hl="en" } },
                    videoId=id,contentCheckOk=true,racyCheckOk=true };
                using(var request=new HttpRequestMessage(HttpMethod.Post,"https://www.youtube.com/youtubei/v1/player?prettyPrint=false")){
                    request.Headers.Add("X-YouTube-Client-Name","5");request.Headers.Add("X-YouTube-Client-Version","21.02.3");
                    request.Content=new StringContent(Json().Serialize(body),Encoding.UTF8,"application/json");
                    using(var response=await Http.SendAsync(request,budget.Token).ConfigureAwait(false)){
                        if(!response.IsSuccessStatusCode)return null;
                        var data=Json().DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        var info=Field(data,"videoDetails");
                        if(!Equals(Field(Field(data,"playabilityStatus"),"status"),"OK")||!Equals(Field(info,"videoId"),id))return null;
                        long duration;
                        if(!long.TryParse(Convert.ToString(Field(info,"lengthSeconds")),out duration)||duration<=0)return null;
                        var formats=Field(Field(data,"streamingData"),"adaptiveFormats") as IList;
                        if(formats==null)return null;
                        Uri best=null;long bestRate=0;int format=0;
                        foreach(var f in formats){
                            Uri url;long milliseconds,rate;
                            if(!Convert.ToString(Field(f,"mimeType")).StartsWith("audio/mp4",StringComparison.OrdinalIgnoreCase)||
                                !long.TryParse(Convert.ToString(Field(f,"approxDurationMs")),out milliseconds)||milliseconds<duration*1000-3000||
                                !long.TryParse(Convert.ToString(Field(f,"bitrate")),out rate)||rate<=bestRate||
                                !Uri.TryCreate(Convert.ToString(Field(f,"url")),UriKind.Absolute,out url)||!MediaHost(url))continue;
                            int itag;if(!int.TryParse(Convert.ToString(Field(f,"itag")),out itag)||itag<=0)continue;best=url;bestRate=rate;format=itag;
                        }
                        if(best==null)return null;
                        await RangeAudio.Register(best,id,format,budget.Token).ConfigureAwait(false);return best;
                    }
                }
            }catch(OperationCanceledException){token.ThrowIfCancellationRequested();return null;}
            catch(HttpRequestException){return null;}catch(IOException){return null;}
            catch(ArgumentException){return null;}
            catch(InvalidOperationException){return null;}
        }
    }
    internal static async Task<Uri> FullFormat(string id,int format,CancellationToken token){
        string result=await Tool(token,"--extractor-args","youtube:skip=hls,dash,translated_subs","--check-formats","-f",
            format.ToString(System.Globalization.CultureInfo.InvariantCulture),"--get-url","https://www.youtube.com/watch?v="+id).ConfigureAwait(false);
        Uri uri;
        if(!Uri.TryCreate(result,UriKind.Absolute,out uri)||!MediaHost(uri))throw new IOException("A full audio stream was unavailable.");
        return uri;
    }
    internal static async Task Prime(Track track,CancellationToken token){
        if(!Valid(track.Id)||Cached(track.Id)!=null)return;
        var uri=await DirectAudio(track.Id,token).ConfigureAwait(false);
        if(uri!=null)RememberUrl(track.Id,uri);
    }
    internal static async Task<string> Resolve(Track t,CancellationToken token,bool retryIPv4=false,bool alreadyTriedDirect=false) {
        if (!string.IsNullOrEmpty(t.File)) {
            if (!System.IO.File.Exists(t.File)) throw new FileNotFoundException("This file has moved or was removed.",t.File);
            return new Uri(t.File).AbsoluteUri;
        }
        if (!Valid(t.Id)) throw new IOException("Invalid track ID.");
        if(retryIPv4)Forget(t.Id);
        else{
            string cached=Cached(t.Id);
            if(cached!=null)return cached;
            Uri direct=alreadyTriedDirect?null:await DirectAudio(t.Id,token).ConfigureAwait(false);
            if(direct!=null){RememberUrl(t.Id,direct);return direct.AbsoluteUri;}
        }
        var args=new List<string> { "--check-formats","-f","bestaudio[ext=m4a][protocol=https]/bestaudio[protocol=https]/best[protocol=https]","--get-url","https://www.youtube.com/watch?v="+t.Id };
        if(retryIPv4)args.Insert(0,"--force-ipv4");
        string url=await Tool(token,args.ToArray()).ConfigureAwait(false);
        Uri u;
        if (!Uri.TryCreate(url,UriKind.Absolute,out u) || u.Scheme!="https") throw new IOException("The provider returned an invalid stream.");
        RememberUrl(t.Id,u);return u.AbsoluteUri;
    }
}
