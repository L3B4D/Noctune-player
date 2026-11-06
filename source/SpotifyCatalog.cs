// Spotify web-session integration adapted from YumaPlayer / ArchiveTune, GPL-3.0.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal static class SpotifyCatalog {
    static readonly HttpClient http=new HttpClient(new HttpClientHandler{UseCookies=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=8000000};
    static readonly SemaphoreSlim authGate=new SemaphoreSlim(1,1);
    static string access;static DateTime expires;static int generation;
    static readonly string sessionPath=Path.Combine(Catalog.Root,"data","spotify-session.bin");
    internal static bool Connected {get{return File.Exists(sessionPath);}}
    internal static readonly Dictionary<string,string> Hashes=new Dictionary<string,string>{
        {"home","23e37f2e58d82d567f27080101d36609009d8c3676457b1086cb0acc55b72a5d"},
        {"searchDesktop","4801118d4a100f756e833d33984436a3899cff359c532f8fd3aaf174b60b3b49"},
        {"libraryV3","973e511ca44261fda7eebac8b653155e7caee3675abb4fb110cc1b8c78b091c3"},
        {"fetchPlaylist","346811f856fb0b7e4f6c59f8ebea78dd081c6e2fb01b77c954b26259d5fc6763"},
        {"getAlbum","b9bfabef66ed756e5e13f68a942deb60bd4125ec1f1be8cc42769dc0259b4b10"}};
    internal static void SaveSession(string cookie){
        if(string.IsNullOrWhiteSpace(cookie)||cookie.Length>8192||cookie.Any(c=>char.IsControl(c)||c==';'))throw new IOException("Invalid Spotify session.");
        Directory.CreateDirectory(Path.GetDirectoryName(sessionPath));
        File.WriteAllBytes(sessionPath,ProtectedData.Protect(Encoding.UTF8.GetBytes(cookie),null,DataProtectionScope.CurrentUser));
        generation++;access=null;expires=DateTime.MinValue;
    }
    internal static void Disconnect(){generation++;access=null;expires=DateTime.MinValue;if(File.Exists(sessionPath))File.Delete(sessionPath);}
    internal static string Totp(string secret,long seconds){
        var bytes=new List<byte>();int bits=0,buffer=0;
        foreach(char c in secret.TrimEnd('=')){int n="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);if(n<0)throw new IOException("Invalid Spotify sign-in parameters.");buffer=(buffer<<5)|n;bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}
        long counter=seconds/30;var message=new byte[8];for(int i=7;i>=0;i--){message[i]=(byte)counter;counter>>=8;}
        using(var hmac=new HMACSHA1(bytes.ToArray())){var hash=hmac.ComputeHash(message);int o=hash[19]&15;int code=((hash[o]&127)<<24)|(hash[o+1]<<16)|(hash[o+2]<<8)|hash[o+3];return (code%1000000).ToString("D6");}
    }
    static async Task<object> Request(string url,object body,string bearer,string cookie,CancellationToken token){
        using(var req=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,url)){
            req.Headers.TryAddWithoutValidation("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
            if(new Uri(url).Host=="api-partner.spotify.com"){req.Headers.TryAddWithoutValidation("app-platform","WebPlayer");req.Headers.TryAddWithoutValidation("Origin","https://open.spotify.com");req.Headers.Referrer=new Uri("https://open.spotify.com/");req.Headers.TryAddWithoutValidation("Accept-Language",System.Globalization.CultureInfo.CurrentUICulture.Name);}
            if(bearer!=null)req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",bearer);
            if(cookie!=null)req.Headers.Add("Cookie","sp_dc="+cookie);
            if(body!=null)req.Content=new StringContent(Catalog.Json().Serialize(body),Encoding.UTF8,"application/json");
            using(var response=await http.SendAsync(req,token).ConfigureAwait(false)){
                if(!response.IsSuccessStatusCode)throw new IOException("Spotify request failed ("+(int)response.StatusCode+"). "+((int)response.StatusCode==401?"Sign in again.":(int)response.StatusCode==429?"Please wait before retrying.":"The web service may be unavailable."));
                return Catalog.Json().DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
    }
    static async Task<string> Token(CancellationToken token){
        await authGate.WaitAsync(token).ConfigureAwait(false);try{
            if(access!=null&&expires>DateTime.UtcNow)return access;
            if(!Connected)throw new IOException("Sign in to Spotify first.");int version=generation;
            string cookie;try{cookie=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(sessionPath),null,DataProtectionScope.CurrentUser));}catch(CryptographicException){throw new IOException("This Spotify session belongs to another Windows account. Sign in again.");}
            var rows=await Request("https://gist.githubusercontent.com/sonic-liberation/22ed9c6ba463899e933427f7de1f0eef/raw/nuances.json",null,null,null,token).ConfigureAwait(false) as IList;
            var nuance=rows==null?null:rows.Cast<object>().Where(x=>Regex.IsMatch(S(x,"s"),"^[A-Z2-7]+=*$")).OrderByDescending(x=>Convert.ToInt32(Catalog.Field(x,"v")??0)).FirstOrDefault();
            if(nuance==null)throw new IOException("Spotify sign-in parameters are unavailable.");
            var clock=await Request("https://open.spotify.com/api/server-time",null,null,null,token).ConfigureAwait(false);
            string otp=Totp(S(nuance,"s"),Convert.ToInt64(Catalog.Field(clock,"serverTime")));
            var data=await Request("https://open.spotify.com/api/token?reason=transport&productType=web-player&totp="+otp+"&totpServer="+otp+"&totpVer="+S(nuance,"v"),null,null,cookie,token).ConfigureAwait(false);
            string result=S(data,"accessToken");if(result.Length==0||Convert.ToBoolean(Catalog.Field(data,"isAnonymous")??true))throw new IOException("Spotify session expired. Sign in again.");
            if(version!=generation)throw new OperationCanceledException();access=result;
            long until;expires=long.TryParse(S(data,"accessTokenExpirationTimestampMs"),out until)&&until>0?new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(until).AddMinutes(-1):DateTime.UtcNow.AddMinutes(5);return access;
        }finally{authGate.Release();}
    }
    internal static async Task<object> Query(string operation,object variables,CancellationToken token){
        var result=await Request("https://api-partner.spotify.com/pathfinder/v2/query",new{operationName=operation,variables=variables,extensions=new{persistedQuery=new{version=1,sha256Hash=Hashes[operation]}}},await Token(token).ConfigureAwait(false),null,token).ConfigureAwait(false);
        var errors=Catalog.Field(result,"errors") as IList;if(errors!=null&&errors.Count>0)throw new IOException("Spotify rejected this catalog request. Its web interface may have changed; your music library is unchanged.");return result;
    }
    internal static string S(object node,string key){return Convert.ToString(Catalog.Field(node,key));}
    internal static IEnumerable<object> Walk(object node){
        var dict=node as IDictionary<string,object>;if(dict!=null){yield return node;foreach(var v in dict.Values)foreach(var c in Walk(v))yield return c;}
        else if(node is IList)foreach(var v in (IList)node)foreach(var c in Walk(v))yield return c;
    }
    static string Art(object node){return Walk(node).Where(x=>S(x,"url").StartsWith("https://")).OrderByDescending(x=>Convert.ToInt32(Catalog.Field(x,"width")??0)).Select(x=>S(x,"url")).FirstOrDefault();}
    internal static List<Track> Parse(object node,bool songsOnly){
        var result=new List<Track>();var seen=new HashSet<string>();
        foreach(var item in Walk(node)){
            string type=S(item,"__typename"),uri=S(item,"uri"),name=S(item,"name");
            if(!Regex.IsMatch(uri,"^spotify:(track|album|playlist):[A-Za-z0-9]{22}$")||name.Length==0)continue;
            bool song=uri.StartsWith("spotify:track:");if(songsOnly&&!song||!seen.Add(uri))continue;
            var artists=Walk(Catalog.Field(item,"artists")).Select(x=>S(Catalog.Field(x,"profile"),"name")).Where(x=>x.Length>0).Distinct();
            int ms=0;int.TryParse(S(Catalog.Field(item,"duration"),"totalMilliseconds"),out ms);if(ms==0)int.TryParse(S(Catalog.Field(item,"trackDuration"),"totalMilliseconds"),out ms);
            string art=Art(Catalog.Field(item,"coverArt"))??Art(Catalog.Field(item,"images"))??Art(Catalog.Field(item,"albumOfTrack"));
            result.Add(new Track{Id=song?uri:null,BrowseId=song?null:uri,Kind=song?null:uri.StartsWith("spotify:album:")?"Album":"Playlist",Title=name,Artist=string.Join(", ",artists),Artwork=art,DurationSeconds=ms/1000});
        }return result;
    }
    internal static async Task<List<Track>> Browse(Track entity,CancellationToken token){
        string uri=entity.BrowseId;bool album=uri.StartsWith("spotify:album:");var tracks=new List<Track>();
        for(int offset=0;offset<10000;offset+=50){var root=await Query(album?"getAlbum":"fetchPlaylist",new{uri=uri,locale="",offset=offset,limit=50,enableWatchFeedEntrypoint=true},token).ConfigureAwait(false);var items=Parse(root,true);tracks.AddRange(items);if(items.Count<50)break;}
        return tracks;
    }
}
