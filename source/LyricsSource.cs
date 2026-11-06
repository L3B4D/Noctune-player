using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Provider routes follow the bundled Yuma GPL-3.0 lyrics modules.
internal static partial class LyricsSource {
    internal sealed class Result {public string Text{get;set;}public string Provider{get;set;}}
    internal sealed class Line {internal long Time;internal string Text;internal string Pronunciation,Translation;internal List<Word> Words;}
    static readonly HttpClient client=new HttpClient{Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=2000000};
    static string Escape(string s){return Uri.EscapeDataString(s??"");}
    internal static string Clean(string s){return Regex.Replace(s??"",@"\s*[\(\[][^\)\]]*(?:official|lyrics?|music video|audio only)[^\)\]]*[\)\]]","",RegexOptions.IgnoreCase).Trim();}
    static async Task<object> Get(string url,CancellationToken token){using(var req=new HttpRequestMessage(HttpMethod.Get,url)){req.Headers.UserAgent.ParseAdd("Noctune-Windows/1.2");using(var response=await client.SendAsync(req,token).ConfigureAwait(false)){response.EnsureSuccessStatusCode();string body=await response.Content.ReadAsStringAsync().ConfigureAwait(false);return body.TrimStart().StartsWith("<")?(object)body:Catalog.Json().DeserializeObject(body);}}}
    static Result Pick(IEnumerable<object> hits,int duration,string provider){
        return hits.Select(h=>new{Hit=h,Delta=duration>0?Math.Abs(Convert.ToDouble(Catalog.Field(h,"duration")??0)-duration):0}).Where(h=>duration<=0||h.Delta<=8).OrderBy(h=>string.IsNullOrWhiteSpace(Convert.ToString(Catalog.Field(h.Hit,"syncedLyrics")))?1:0).ThenBy(h=>h.Delta).Select(h=>new Result{Provider=provider,Text=string.IsNullOrWhiteSpace(Convert.ToString(Catalog.Field(h.Hit,"syncedLyrics")))?Convert.ToString(Catalog.Field(h.Hit,"plainLyrics")):Convert.ToString(Catalog.Field(h.Hit,"syncedLyrics"))}).FirstOrDefault(r=>!string.IsNullOrWhiteSpace(r.Text));
    }
    static async Task<Result> Lrc(Track t,int duration,CancellationToken token){
        Result plain=null;var titles=new[]{t.Title,Clean(t.Title)}.Concat(Clean(t.Title).Split(new[]{" - "},StringSplitOptions.RemoveEmptyEntries)).Distinct().Take(4).ToArray();
        foreach(var title in titles){var data=await Get("https://lrclib.net/api/search?track_name="+Escape(title)+"&artist_name="+Escape(t.Artist),token).ConfigureAwait(false) as IList;var result=Pick(data==null?new object[0]:data.Cast<object>(),duration,"LRCLIB");if(result!=null){if(Parse(result.Text).Any(l=>l.Time>=0))return result;plain=result;}}
        string alias=await Catalog.NativeLyricArtist(t,token).ConfigureAwait(false);if(!string.IsNullOrWhiteSpace(alias)&&!string.Equals(alias,t.Artist,StringComparison.OrdinalIgnoreCase))foreach(var title in titles){var data=await Get("https://lrclib.net/api/search?track_name="+Escape(title)+"&artist_name="+Escape(alias),token).ConfigureAwait(false) as IList;var result=Pick(data==null?new object[0]:data.Cast<object>(),duration,"LRCLIB");if(result!=null){if(Parse(result.Text).Any(l=>l.Time>=0))return result;plain=result;}}
        return plain;
    }    static async Task<Result> Simp(Track t,int duration,CancellationToken token){if(string.IsNullOrEmpty(t.Id))return null;var data=await Get("https://api-lyrics.simpmusic.org/v1/"+Escape(t.Id),token).ConfigureAwait(false);var rows=Catalog.Field(data,"data") as IList;return Pick(rows==null?new object[0]:rows.Cast<object>(),duration,"SimpMusic");}
    static async Task<Result> Plus(Track t,int duration,CancellationToken token){
        foreach(string host in new[]{"https://lyricsplus.binimum.org/","https://lyricsplus.prjktla.my.id/"}){try{
            var data=await Get(host+"v2/lyrics/get?title="+Escape(Clean(t.Title))+"&artist="+Escape(t.Artist)+(duration>0?"&duration="+duration:"")+(!string.IsNullOrEmpty(t.Album)?"&album="+Escape(t.Album):""),token).ConfigureAwait(false);
            var rows=Catalog.Field(data,"lyrics") as IList;if(rows==null)continue;var text=new StringBuilder();foreach(var row in rows){string value=Convert.ToString(Catalog.Field(row,"text"));if(string.IsNullOrWhiteSpace(value))continue;var time=Catalog.Field(row,"time");if(time!=null){long ms=Convert.ToInt64(time);text.AppendFormat(CultureInfo.InvariantCulture,"[{0:00}:{1:00}.{2:000}]",ms/60000,(ms/1000)%60,ms%1000);}var syllables=Catalog.Field(row,"syllabus") as IList;if(syllables!=null&&syllables.Count>0){foreach(var syllable in syllables){long at=Convert.ToInt64(Catalog.Field(syllable,"time")??0);text.AppendFormat(CultureInfo.InvariantCulture,"<{0:00}:{1:00}.{2:000}>",at/60000,(at/1000)%60,at%1000);text.Append(Convert.ToString(Catalog.Field(syllable,"text")));}text.AppendLine();}else text.AppendLine(value);}if(text.Length>0)return new Result{Text=text.ToString(),Provider="YouLyPlus"};
        }catch(OperationCanceledException){token.ThrowIfCancellationRequested();}catch(HttpRequestException){}catch(ArgumentException){}}
        return null;
    }
    static async Task<Result> Safe(Task<Result> task){try{return await task.ConfigureAwait(false);}catch(Exception){return null;}}
    static string CachePath(Track t){using(var sha=SHA256.Create()){var name=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes((t.Id??t.QobuzId.ToString())+"|"+t.Title+"|"+t.Artist))).Replace("-","");return Path.Combine(Catalog.Root,"data","lyrics",name+".json");}}
    internal static async Task<Result> Load(Track t,int duration,CancellationToken token,bool refresh){
        string path=CachePath(t);if(!refresh)try{if(File.Exists(path)){var cached=Catalog.Json().Deserialize<Result>(File.ReadAllText(path));if(cached!=null&&!string.IsNullOrWhiteSpace(cached.Text)&&Parse(cached.Text).Any(l=>l.Time>=0))return cached;}}catch(IOException){}catch(ArgumentException){}
        using(var linked=CancellationTokenSource.CreateLinkedTokenSource(token)){linked.CancelAfter(22000);var tasks=new List<Task<Result>>{Safe(Lrc(t,duration,linked.Token)),Safe(Simp(t,duration,linked.Token)),Safe(Plus(t,duration,linked.Token)),Safe(Catalog.YouTubeLyrics(t,linked.Token)),Safe(Better(t,duration,linked.Token)),Safe(Unison(t,duration,linked.Token)),Safe(Kugou(t,duration,linked.Token))};Result best=null;
            while(tasks.Count>0){var done=await Task.WhenAny(tasks).ConfigureAwait(false);tasks.Remove(done);var result=await done.ConfigureAwait(false);token.ThrowIfCancellationRequested();if(result==null)continue;if(best==null)best=result;if(Parse(result.Text).Any(l=>l.Time>=0)){best=result;linked.Cancel();break;}}
            if(best==null||!Parse(best.Text).Any(l=>l.Time>=0)){using(var captions=CancellationTokenSource.CreateLinkedTokenSource(token)){captions.CancelAfter(8000);var subtitle=await Safe(Catalog.YouTubeSubtitles(t,captions.Token)).ConfigureAwait(false);token.ThrowIfCancellationRequested();if(subtitle!=null)best=subtitle;}}
            if(best!=null)try{Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,Catalog.Json().Serialize(best));}catch(IOException){}catch(UnauthorizedAccessException){}
            return best;
        }
    }
    internal static List<Line> Parse(string text){
        if((text??"").Length>2000000)throw new ArgumentException("Lyrics response too large");if((text??"").TrimStart().StartsWith("<"))return ParseTtml(text);var result=new List<Line>();long offset=0;var off=Regex.Match(text??"",@"\[offset:([+-]?\d+)\]",RegexOptions.IgnoreCase);if(off.Success)long.TryParse(off.Groups[1].Value,out offset);
        foreach(string raw in (text??"").Split('\n').Take(4000)){var tags=Regex.Matches(raw,@"\[(\d+):(\d+(?:\.\d+)?)\]");string value=Regex.Replace(raw,@"\[[^\]]*\]|<\d+:\d+(?:\.\d+)?>","").Trim();if(value.Length==0)continue;if(tags.Count==0)result.Add(new Line{Time=-1,Text=value});else foreach(Match m in tags){double sec=double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*60+double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture);result.Add(new Line{Time=Math.Max(0,(long)(sec*1000)+offset),Text=value,Words=ParseWords(raw,offset)});}}
        return result.Any(l=>l.Time>=0)?result.Where(l=>l.Time>=0).OrderBy(l=>l.Time).ToList():result;
    }
}

internal static partial class Catalog {
    internal static async Task<string> NativeLyricArtist(Track t,CancellationToken token){
        string channel=t.ArtistId;if(string.IsNullOrEmpty(channel)){if(string.IsNullOrEmpty(t.Id))return null;var player=await MusicRequest("player",new{context=MusicContext(),videoId=t.Id},token).ConfigureAwait(false);channel=Convert.ToString(Field(Field(player,"videoDetails"),"channelId"));}if(!channel.StartsWith("UC"))return null;
        var data=await MusicRequest("browse",new{context=new{client=new{clientName="WEB_REMIX",clientVersion="1.20260213.01.00",hl="ja"}},browseId=channel},token).ConfigureAwait(false);
        var header=Field(data,"header");return Nodes(header,"musicImmersiveHeaderRenderer").Concat(Nodes(header,"musicVisualHeaderRenderer")).Select(x=>Runs(Field(x,"title"))).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
    }
    internal static async Task<LyricsSource.Result> YouTubeLyrics(Track t,CancellationToken token){
        if(string.IsNullOrEmpty(t.Id))return null;
        var next=await MusicRequest("next",new{context=MusicContext(),videoId=t.Id},token).ConfigureAwait(false);
        string id=Nodes(next,"browseEndpoint").Select(e=>Convert.ToString(Field(e,"browseId"))).FirstOrDefault(x=>x.StartsWith("MPLY"));if(string.IsNullOrEmpty(id))return null;
        var data=await MusicRequest("browse",new{context=MusicContext(),browseId=id},token).ConfigureAwait(false);
        string text=Nodes(data,"musicDescriptionShelfRenderer").Select(e=>Runs(Field(e,"description"))).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
        return string.IsNullOrWhiteSpace(text)?null:new LyricsSource.Result{Text=text,Provider="YouTube Music"};
    }
}