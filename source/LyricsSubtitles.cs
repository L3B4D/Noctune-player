using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static partial class Catalog {
    internal static async Task<LyricsSource.Result> YouTubeSubtitles(Track track,CancellationToken token){
        if(!Valid(track.Id))return null;
        try{
            var transcript=await MusicRequest("get_transcript",new{context=new{client=new{clientName="WEB",clientVersion="2.20260213.01.00",hl="en"}},@params=Convert.ToBase64String(Encoding.UTF8.GetBytes("\n\v"+track.Id))},token).ConfigureAwait(false);
            var timed=new StringBuilder();foreach(var cue in Nodes(transcript,"transcriptCueRenderer").Take(4000)){long ms;string line=Convert.ToString(Field(Field(cue,"cue"),"simpleText")).Trim('♪',' ');if(line.Length>0&&long.TryParse(Convert.ToString(Field(cue,"startOffsetMs")),out ms)&&ms>=0)timed.AppendFormat(CultureInfo.InvariantCulture,"[{0:00}:{1:00}.{2:000}]{3}\n",ms/60000,(ms/1000)%60,ms%1000,line);}
            if(timed.Length>0)return new LyricsSource.Result{Provider="YouTube transcript",Text=timed.ToString()};
        }catch(OperationCanceledException){throw;}catch(System.Net.Http.HttpRequestException){}
        var player=await MusicRequest("player",new{context=MusicContext(),videoId=track.Id},token).ConfigureAwait(false);
        var captions=Field(Field(Field(player,"captions"),"playerCaptionsTracklistRenderer"),"captionTracks") as IList;
        // Prefer human captions. Auto-transcribed speech is not reliable song timing.
        var caption=captions==null?null:captions.Cast<object>().Where(x=>Convert.ToString(Field(x,"kind"))!="asr").FirstOrDefault();
        if(caption==null)return null;Uri url;
        if(!Uri.TryCreate(Convert.ToString(Field(caption,"baseUrl")),UriKind.Absolute,out url)||url.Scheme!="https"||(url.Host!="www.youtube.com"&&url.Host!="music.youtube.com"))return null;
        string address=System.Text.RegularExpressions.Regex.Replace(url.AbsoluteUri,@"([?&])fmt=[^&]*","$1fmt=json3");if(!address.Contains("fmt=json3"))address+="&fmt=json3";
        using(var response=await Http.GetAsync(address,token).ConfigureAwait(false)){response.EnsureSuccessStatusCode();string text=await response.Content.ReadAsStringAsync().ConfigureAwait(false);if(text.Length>2000000)return null;return ParseSubtitles(Json().DeserializeObject(text));}
    }
    internal static LyricsSource.Result ParseSubtitles(object data){
        var events=Field(data,"events") as IList;if(events==null)return null;var output=new StringBuilder();
        foreach(var item in events.Cast<object>().Take(4000)){
            var segments=Field(item,"segs") as IList;long start;
            if(segments==null||!long.TryParse(Convert.ToString(Field(item,"tStartMs")),out start)||start<0)continue;
            string value=string.Concat(segments.Cast<object>().Select(x=>Convert.ToString(Field(x,"utf8")))).Replace("\n"," ").Trim();if(value.Length==0)continue;
            output.AppendFormat(CultureInfo.InvariantCulture,"[{0:00}:{1:00}.{2:000}]{3}\n",start/60000,(start/1000)%60,start%1000,value);
        }return output.Length==0?null:new LyricsSource.Result{Provider="YouTube captions",Text=output.ToString()};
    }
}
