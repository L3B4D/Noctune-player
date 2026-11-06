using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Qobuz protocol adapted from YumaPlayer's GPLv3 QbdlxApiClient and QbdlxSigner.
internal sealed class Qobuz {
    const string Base="https://www.qobuz.com/api.json/0.2/";
    static readonly HttpClient Client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) {
        Timeout=TimeSpan.FromSeconds(20)
    };
    readonly string appId, secret, userToken;
    readonly HttpClient http;

    internal Qobuz(string id,string key,string token,HttpClient client=null) {
        appId=id.Trim();secret=key.Trim();userToken=token.Trim();http=client??Client;
        if(appId.Length==0 || secret.Length==0 || userToken.Length==0 ||
            appId.IndexOfAny(new[]{'\r','\n'})>=0 || userToken.IndexOfAny(new[]{'\r','\n'})>=0)
            throw new ArgumentException("Enter the Qobuz app ID, its matching secret, and your user token.");
    }
    async Task<object> Get(string endpoint,string parameters,CancellationToken cancel) {
        using(var request=new HttpRequestMessage(HttpMethod.Get,Base+endpoint+"?"+parameters+"&app_id="+Uri.EscapeDataString(appId))) {
            request.Headers.Add("X-App-Id",appId);
            request.Headers.Add("X-User-Auth-Token",userToken);
            request.Headers.Accept.ParseAdd("application/json");
            using(var reply=await http.SendAsync(request,cancel).ConfigureAwait(false)) {
                if((int)reply.StatusCode==401 || (int)reply.StatusCode==403)
                    throw new IOException("Qobuz denied access. Check your app ID, secret, user token, and account access.");
                if(!reply.IsSuccessStatusCode)throw new IOException("Qobuz returned HTTP "+(int)reply.StatusCode+". Try again or switch to YouTube Music.");
                return Catalog.Json().DeserializeObject(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
    }
    internal async Task<List<Track>> Search(string query,CancellationToken cancel) {
        var data=await Get("catalog/search","type=tracks&limit=30&query="+Uri.EscapeDataString(query),cancel).ConfigureAwait(false);
        var items=Catalog.Field(Catalog.Field(data,"tracks"),"items") as IList;
        if(items==null)throw new IOException("Qobuz returned an unrecognized catalog response.");
        var tracks=new List<Track>();
        foreach(var item in items) {
            long id;
            string title=Convert.ToString(Catalog.Field(item,"title"));
            if(!long.TryParse(Convert.ToString(Catalog.Field(item,"id")),out id) || id<=0 || title.Length==0 || Equals(Catalog.Field(item,"streamable"),false))continue;
            string version=Convert.ToString(Catalog.Field(item,"version"));
            tracks.Add(new Track { Artwork=Convert.ToString(Catalog.Field(Catalog.Field(Catalog.Field(item,"album"),"image"),"large")), QobuzId=id, Title=version.Length==0?title:title+" ("+version+")",
                Artist=Convert.ToString(Catalog.Field(Catalog.Field(item,"performer"),"name")) });
        }
        return tracks;
    }
    internal Task<string> Resolve(long id,CancellationToken cancel){return ResolveUrl(id,cancel,"stream");}
    internal async Task<string> Download(long id,string path,CancellationToken token){
        string url=await ResolveUrl(id,token,"download").ConfigureAwait(false);
        using(var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)){
            response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>1073741824)throw new IOException("Download exceeds the 1 GB per-track limit.");
            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){var buffer=new byte[65536];long total=0;int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false))>0){total+=count;if(total>1073741824)throw new IOException("Download exceeds the 1 GB per-track limit.");await output.WriteAsync(buffer,0,count,token).ConfigureAwait(false);}}
        }
        using(var file=File.OpenRead(path)){var signature=new byte[4];if(file.Read(signature,0,4)!=4||Encoding.ASCII.GetString(signature)!="fLaC")throw new IOException("The server did not return a FLAC file.");}return path;
    }    async Task<string> ResolveUrl(long id,CancellationToken cancel,string intent) {
        string track=id.ToString(CultureInfo.InvariantCulture);
        string ts=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        const string format="6"; // CD-quality FLAC
        string payload="trackgetFileUrlformat_id"+format+"intent"+intent+"track_id"+track+ts+secret;
        string signature;
        using(var md5=MD5.Create())signature=BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-","").ToLowerInvariant();
        var data=await Get("track/getFileUrl","track_id="+track+"&format_id="+format+"&intent="+intent+"&request_ts="+ts+"&request_sig="+signature,cancel).ConfigureAwait(false);
        var restrictions=Catalog.Field(data,"restrictions") as IList;
        if(restrictions!=null)foreach(var restriction in restrictions)
            if(string.Equals(Convert.ToString(Catalog.Field(restriction,"code")),"UserUnauthenticated",StringComparison.OrdinalIgnoreCase))
                throw new IOException("Qobuz requires an authorized account for this recording.");
        Uri stream;
        if(Equals(Catalog.Field(data,"sample"),true) || Convert.ToInt32(Catalog.Field(data,"format_id"))<6 ||
            !Uri.TryCreate(Convert.ToString(Catalog.Field(data,"url")),UriKind.Absolute,out stream) || stream.Scheme!=Uri.UriSchemeHttps)
            throw new IOException("Full lossless playback is unavailable for this recording. Try another version or search YouTube Music.");
        return stream.AbsoluteUri;
    }

    internal static Qobuz Connect(IWin32Window owner) {
        using(var dialog=DialogUI.Create("Connect Qobuz","Use credentials issued for your account and app. They stay in memory until Noctune closes.",520,440)){
            string[] labels={"App ID","App secret","User token"};var fields=new TextBox[3];
            for(int i=0;i<3;i++)fields[i]=DialogUI.Field(dialog,labels[i],"",24,112+i*85,472,i>0);
            var cancel=new SoftButton("Cancel",delegate{dialog.DialogResult=DialogResult.Cancel;});cancel.SetBounds(248,382,100,36);dialog.Controls.Add(cancel);dialog.CancelButton=cancel;
            Qobuz session=null;var connect=new SoftButton("Connect",delegate{try{session=new Qobuz(fields[0].Text,fields[1].Text,fields[2].Text);dialog.DialogResult=DialogResult.OK;}catch(ArgumentException ex){DialogUI.Show(dialog,ex.Message,"Check your credentials");}},true);connect.SetBounds(360,382,136,36);dialog.Controls.Add(connect);dialog.AcceptButton=connect;
            return dialog.ShowDialog(owner)==DialogResult.OK?session:null;
        }
    }
}
