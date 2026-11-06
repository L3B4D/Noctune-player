using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal partial class Player {
    readonly Surface spotifyPage=new Surface();readonly ListView spotifyRows=new ResultsList();
    Surface spotifyEmpty;Action layoutSpotify;Label spotifyStatus;TextBox spotifyQuery;CancellationTokenSource spotifyRequest;WebView2 spotifyLogin;bool capturingSpotify;int spotifyOffset;string spotifyOperation;ArtButton spotifyMore;
    void BuildSpotify(){
        stage.Controls.Add(spotifyPage);spotifyPage.BackColor=Theme.Ink;
        var title=LabelOn(spotifyPage,"Spotify",27);spotifyStatus=LabelOn(spotifyPage,"Your catalog and mixes. Playback matched through YouTube Music.",10);spotifyStatus.ForeColor=Theme.Muted;
        var signin=Pill(spotifyPage,"Sign in",SpotifySignIn,true);
        var home=Pill(spotifyPage,"Made for you",delegate{LoadSpotify("home");});
        var libraryButton=Pill(spotifyPage,"Your playlists",delegate{LoadSpotify("libraryV3");});
        var logout=Pill(spotifyPage,"Disconnect",delegate{if(spotifyRequest!=null)spotifyRequest.Cancel();SpotifyCatalog.Disconnect();if(spotifyLogin!=null){if(spotifyLogin.CoreWebView2!=null)spotifyLogin.CoreWebView2.CookieManager.DeleteAllCookies();spotifyLogin.Dispose();spotifyLogin=null;}spotifyRows.Items.Clear();layoutSpotify();spotifyStatus.Text="Disconnected. Your local music is unchanged.";});
        spotifyQuery=new TextBox{BackColor=Theme.Card,ForeColor=Theme.Paper,BorderStyle=BorderStyle.None,Font=Theme.Font(12),AccessibleName="Search Spotify"};spotifyPage.Controls.Add(spotifyQuery);
        var search=Pill(spotifyPage,"Search",delegate{LoadSpotify("searchDesktop");});spotifyQuery.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;LoadSpotify("searchDesktop");}};
        var searchFrame=new Surface{BackColor=Theme.Ink};spotifyPage.Controls.Add(searchFrame);searchFrame.PaintSurface=delegate(Graphics g,Rectangle r){PaintGlass(g,new Rectangle(1,1,r.Width-3,r.Height-3),18,Theme.Card);};searchFrame.Controls.Add(spotifyQuery);searchFrame.Controls.Add(search);
        spotifyEmpty=new Surface{BackColor=Theme.Ink};spotifyPage.Controls.Add(spotifyEmpty);spotifyEmpty.PaintSurface=delegate(Graphics g,Rectangle r){PaintGlass(g,new Rectangle(1,1,r.Width-3,r.Height-3),22,Color.FromArgb(35,75,60));Glyph(g,"\uE8D6",new Rectangle(24,24,52,52),Theme.Peach,30);TextAt(g,"Your Spotify, inside Noctune",new Rectangle(24,88,r.Width-48,76),23,Theme.Paper,true,false,true);TextAt(g,"Connect to explore the mixes and playlists in your account. Songs play through matched YouTube Music recordings.",new Rectangle(24,174,r.Width-48,100),11,Theme.Muted,false,false,true);};
        SetList(spotifyRows,spotifyPage,"Spotify catalog");DarkScrollHost.Wrap(spotifyRows);InstallTrackMenu(spotifyRows);
        spotifyMore=Pill(spotifyPage,"Load more",delegate{spotifyOffset+=spotifyOperation=="libraryV3"?50:30;LoadSpotify(spotifyOperation,true);});spotifyMore.Visible=false;
        var cancel=Pill(spotifyPage,"Close sign-in",delegate{if(spotifyLogin!=null)spotifyLogin.Visible=false;layoutSpotify();});
        layoutSpotify=delegate{int w=spotifyPage.Width;bool narrow=w<530;int searchY=narrow?202:158;Place(title,20,12,w-40,46);Place(spotifyStatus,20,63,w-40,40);Place(signin,20,111,88,34);Place(home,118,111,123,34);Place(libraryButton,narrow?20:251,narrow?153:111,123,34);Place(logout,narrow?153:384,narrow?153:111,110,34);Place(searchFrame,20,searchY,w-40,44);Place(spotifyQuery,14,11,Math.Max(40,w-172),25);Place(search,w-140,5,94,34);Place(spotifyRows,12,searchY+58,w-24,Math.Max(60,spotifyPage.Height-searchY-115));Place(spotifyMore,20,spotifyPage.Height-49,116,34);Place(spotifyEmpty,20,searchY+70,w-40,Math.Max(150,Math.Min(300,spotifyPage.Height-searchY-90)));spotifyEmpty.Visible=spotifyRows.Items.Count==0;ScrollVisible(spotifyRows,spotifyRows.Items.Count>0);Place(cancel,Math.Max(20,w-145),16,125,34);cancel.Visible=spotifyLogin!=null&&spotifyLogin.Visible;if(spotifyLogin!=null){Place(spotifyLogin,12,60,w-24,Math.Max(80,spotifyPage.Height-72));if(spotifyLogin.Visible)spotifyLogin.BringToFront();}};spotifyPage.Resize+=delegate{layoutSpotify();};

        FormClosing+=delegate{if(spotifyRequest!=null)spotifyRequest.Cancel();if(spotifyLogin!=null)spotifyLogin.Dispose();};
    }
    void OpenSpotify(){ShowPage("Spotify");if(spotifyRows.Items.Count==0&&SpotifyCatalog.Connected)LoadSpotify("home");}
    async void SpotifySignIn(){
        if(spotifyLogin!=null){spotifyLogin.Visible=true;spotifyLogin.BringToFront();layoutSpotify();return;}
        spotifyStatus.Text="Opening secure Spotify sign-in…";
        try{
            spotifyLogin=new WebView2{DefaultBackgroundColor=Theme.Ink};spotifyPage.Controls.Add(spotifyLogin);
            Place(spotifyLogin,12,60,spotifyPage.Width-24,spotifyPage.Height-72);spotifyLogin.BringToFront();layoutSpotify();
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Catalog.Root,"data","spotify-browser"));
            if(closing||spotifyLogin==null)return;await spotifyLogin.EnsureCoreWebView2Async(environment);if(closing||spotifyLogin==null)return;
            spotifyLogin.CoreWebView2.Settings.AreDevToolsEnabled=false;spotifyLogin.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            spotifyLogin.CoreWebView2.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;Uri url;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out url)&&url.Scheme=="https")spotifyLogin.CoreWebView2.Navigate(e.Uri);};
            spotifyLogin.NavigationCompleted+=async delegate{await CaptureSpotify();};
            spotifyLogin.CoreWebView2.Navigate("https://accounts.spotify.com/login?continue=https%3A%2F%2Fopen.spotify.com%2F");
            spotifyStatus.Text="Sign in directly to Spotify. Noctune stores your session encrypted for this Windows account.";
        }catch(Exception){if(spotifyLogin!=null){spotifyLogin.Dispose();spotifyLogin=null;}spotifyStatus.Text="Spotify sign-in could not open. Microsoft Edge WebView2 Runtime is required.";}
    }
    async Task CaptureSpotify(){
        if(capturingSpotify||spotifyLogin==null||spotifyLogin.CoreWebView2==null)return;capturingSpotify=true;
        try{var cookies=await spotifyLogin.CoreWebView2.CookieManager.GetCookiesAsync("https://open.spotify.com/");if(closing||spotifyLogin==null)return;var cookie=cookies.FirstOrDefault(x=>x.Name=="sp_dc");if(cookie==null)return;SpotifyCatalog.SaveSession(cookie.Value);spotifyLogin.Visible=false;layoutSpotify();spotifyStatus.Text="Connected. Loading your catalog…";LoadSpotify("home");}
        catch(Exception){if(!closing)spotifyStatus.Text="Could not save Spotify sign-in. Please try again.";}finally{capturingSpotify=false;}
    }
    async void LoadSpotify(string operation,bool append=false){
        if(!SpotifyCatalog.Connected){spotifyStatus.Text="Sign in to load your Spotify catalog. No developer key needed.";return;}
        if(operation=="searchDesktop"&&string.IsNullOrWhiteSpace(spotifyQuery.Text))return;
        if(!append)spotifyOffset=0;spotifyOperation=operation;if(spotifyRequest!=null)spotifyRequest.Cancel();var req=new CancellationTokenSource(TimeSpan.FromSeconds(45));spotifyRequest=req;spotifyStatus.Text="Loading Spotify…";
        try{
            object vars=operation=="home"?(object)new{homeEndUserIntegration="INTEGRATION_WEB_PLAYER",timeZone="UTC",sp_t="",facet="",sectionItemsLimit=15,includeEpisodeContentRatingsV2=false}:
                operation=="libraryV3"?(object)new{filters=new[]{"Playlists"},order=(string)null,textFilter="",features=new[]{"LIKED_SONGS","YOUR_EPISODES_V2","PRERELEASES","EVENTS"},limit=50,offset=spotifyOffset,flatten=true,expandedFolders=new string[0],folderUri=(string)null,includeFoldersWhenFlattening=false}:
                new{searchTerm=spotifyQuery.Text.Trim(),offset=spotifyOffset,limit=30,numberOfTopResults=5,includeAudiobooks=false,includeArtistHasConcertsField=false,includePreReleases=false,includeLocalConcertsField=false,includeAuthors=false};
            var data=await SpotifyCatalog.Query(operation,vars,req.Token);req.Token.ThrowIfCancellationRequested();if(closing)return;var tracks=SpotifyCatalog.Parse(data,false);
            spotifyRows.BeginUpdate();try{if(!append)spotifyRows.Items.Clear();var keys=new HashSet<string>(spotifyRows.Items.Cast<ListViewItem>().Select(x=>Key((Track)x.Tag)));foreach(var t in tracks)if(keys.Add(Key(t)))spotifyRows.Items.Add(Row(t));spotifyMore.Visible=operation!="home"&&tracks.Count>0;}finally{spotifyRows.EndUpdate();}
            layoutSpotify();foreach(var t in tracks.Take(15))FetchArtwork(t);spotifyStatus.Text=tracks.Count==0?"No items returned for this account.":spotifyRows.Items.Count+" items shown · Spotify catalog / YouTube Music playback";
        }catch(OperationCanceledException){if(!closing&&spotifyRequest==req)spotifyStatus.Text="Request cancelled or timed out. Try again.";}
        catch(Exception e){if(!closing&&spotifyRequest==req)spotifyStatus.Text=e is IOException?e.Message:"Spotify could not load. Please retry.";}
        finally{if(spotifyRequest==req)spotifyRequest=null;req.Dispose();}
    }
    static bool IsSpotifySong(Track track){return track!=null&&(track.Id??"").StartsWith("spotify:track:");}
    static readonly Dictionary<string,Track> spotifyMatches=new Dictionary<string,Track>();
    internal static async Task<Track> MatchSpotify(Track wanted,CancellationToken token){
        string key=wanted.Id+"|"+wanted.Title+"|"+wanted.Artist+"|"+wanted.DurationSeconds;Track cached;
        lock(spotifyMatches)if(spotifyMatches.TryGetValue(key,out cached))return cached;
        var matches=await Catalog.Search(wanted.Title+" "+wanted.Artist,token);token.ThrowIfCancellationRequested();
        var match=matches.FirstOrDefault(t=>SameRecording(wanted,t)&&wanted.DurationSeconds>0&&t.DurationSeconds>0);
        if(match==null){
            var possible=matches.Where(t=>MatchName(t.Title)==MatchName(wanted.Title)&&wanted.DurationSeconds>0&&t.DurationSeconds>0&&Math.Abs(t.DurationSeconds-wanted.DurationSeconds)<=5&&!string.IsNullOrEmpty(t.ArtistId)).ToList();
            if(possible.Count>0){var artists=await Catalog.SearchEntities(wanted.Artist,"Artists",token);var identities=new HashSet<string>(artists.Where(t=>MatchName(t.Title)==MatchName(wanted.Artist)).Select(t=>t.BrowseId));match=possible.FirstOrDefault(t=>identities.Contains(t.ArtistId));}
        }
        if(match==null)throw new IOException("No verified YouTube Music match for this Spotify recording. Search manually to choose a version.");
        lock(spotifyMatches){if(spotifyMatches.Count>=128)spotifyMatches.Clear();spotifyMatches[key]=match;}return match;
    }
}
