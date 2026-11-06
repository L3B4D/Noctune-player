// Provider routes and TTML conventions adapted from YumaPlayer / ArchiveTune (GPL-3.0).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.IO;
internal static partial class LyricsSource {
    internal sealed class Word {internal long Start,End;internal string Text;}
    static string Unwrap(object value,int depth=0){if(depth>3||value==null)return null;var str=value as string;if(str!=null)return str;foreach(var key in new[]{"ttml","lyrics","data","result","response"}){var result=Unwrap(Catalog.Field(value,key),depth+1);if(!string.IsNullOrWhiteSpace(result))return result;}return null;}
    static async Task<Result> Better(Track t,int duration,CancellationToken token){
        foreach(string route in new[]{"getLyrics","kugou/getLyrics"}){try{string url="https://lyrics-api.boidu.dev/"+route+"?s="+Escape(Clean(t.Title))+"&a="+Escape(t.Artist)+(duration>0?"&d="+duration:"");string text=Unwrap(await Get(url,token).ConfigureAwait(false));if(!string.IsNullOrWhiteSpace(text)&&Parse(text).Any(l=>l.Time>=0))return new Result{Provider="BetterLyrics",Text=text};}catch(OperationCanceledException){token.ThrowIfCancellationRequested();}catch(Exception){}}return null;
    }
    static async Task<Result> Unison(Track t,int duration,CancellationToken token){
        var queries=new List<string>();if(!string.IsNullOrEmpty(t.Id))queries.Add("v="+Escape(t.Id));queries.Add("song="+Escape(Clean(t.Title))+"&artist="+Escape(t.Artist)+(duration>0?"&duration="+duration:""));foreach(var query in queries){try{var data=await Get("https://unison.boidu.dev/lyrics?"+query,token).ConfigureAwait(false);string value=Convert.ToString(Catalog.Field(Catalog.Field(data,"data"),"lyrics"));if(!string.IsNullOrWhiteSpace(value))return new Result{Provider="Unison",Text=value};}catch(OperationCanceledException){token.ThrowIfCancellationRequested();}catch(Exception){}}return null;
    }
    static async Task<Result> Kugou(Track t,int duration,CancellationToken token){
        var data=await Get("https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword="+Escape(Clean(t.Title)+" - "+t.Artist)+(duration>0?"&duration="+duration*1000:""),token).ConfigureAwait(false);var candidates=Catalog.Field(data,"candidates") as IList;if(candidates==null)return null;
        foreach(var item in candidates.Cast<object>().Take(3)){string id=Convert.ToString(Catalog.Field(item,"id")),key=Convert.ToString(Catalog.Field(item,"accesskey"));if(id.Length==0||key.Length==0)continue;var response=await Get("https://lyrics.kugou.com/download?fmt=lrc&charset=utf8&client=pc&ver=1&id="+Escape(id)+"&accesskey="+Escape(key),token).ConfigureAwait(false);string encoded=Convert.ToString(Catalog.Field(response,"content"));if(encoded.Length==0||encoded.Length>2000000)continue;string text=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));if(Parse(text).Any(l=>l.Time>=0))return new Result{Provider="Kugou",Text=text};}return null;
    }
    static long TtmlTime(string text){if(string.IsNullOrEmpty(text))return -1;double n;if(text.EndsWith("ms")&&double.TryParse(text.Substring(0,text.Length-2),NumberStyles.Float,CultureInfo.InvariantCulture,out n))return (long)n;if(text.EndsWith("s")&&double.TryParse(text.Substring(0,text.Length-1),NumberStyles.Float,CultureInfo.InvariantCulture,out n))return (long)(n*1000);double total=0;foreach(string part in text.Split(':')){if(!double.TryParse(part,NumberStyles.Float,CultureInfo.InvariantCulture,out n))return -1;total=total*60+n;}return (long)(total*1000);}
    static string Attr(XElement e,string name){return e.Attributes().Where(a=>a.Name.LocalName==name).Select(a=>a.Value).FirstOrDefault();}
    internal static List<Line> ParseTtml(string text){
        using(var reader=XmlReader.Create(new StringReader(text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2000000})){
            var doc=XDocument.Load(reader);var result=new List<Line>();foreach(var p in doc.Descendants().Where(x=>x.Name.LocalName=="p").Take(4000)){
                var line=new Line{Time=TtmlTime(Attr(p,"begin")),Text="",Words=new List<Word>()};var original=new StringBuilder();foreach(var node in p.Nodes()){var span=node as XElement;if(span==null){original.Append((node as XText)!=null?((XText)node).Value:"");continue;}string role=Attr(span,"role")??"";if(role.Contains("translation")){line.Translation=span.Value;continue;}if(role.Contains("roman")){line.Pronunciation=span.Value;continue;}original.Append(span.Value);long start=TtmlTime(Attr(span,"begin")),end=TtmlTime(Attr(span,"end"));if(start>=0)line.Words.Add(new Word{Start=start,End=end>start?end:start+500,Text=span.Value});}line.Text=Regex.Replace(original.ToString(),@"\s+"," ").Trim();if(line.Time<0&&line.Words.Count>0)line.Time=line.Words[0].Start;if(line.Text.Length>0)result.Add(line);
            }return result.OrderBy(l=>l.Time).ToList();
        }
    }
    internal static List<Word> ParseWords(string raw,long offset){var words=new List<Word>();var matches=Regex.Matches(raw,@"<(\d+):(\d+(?:\.\d+)?)>([^<]*)");foreach(Match m in matches){long start=(long)((double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*60+double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture))*1000)+offset;words.Add(new Word{Start=Math.Max(0,start),End=start+600,Text=m.Groups[3].Value});}for(int i=0;i+1<words.Count;i++)words[i].End=words[i+1].Start;return words;}
    internal static int ActiveLine(IList<long> starts,long playback,int offset){int found=-1;long time=playback+offset;for(int i=0;i<starts.Count;i++)if(starts[i]>=0&&starts[i]<=time)found=i;return found;}
}
