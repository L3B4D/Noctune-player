using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal partial class Player {
    string gpuError;readonly Surface gpuPage=new Surface();WebView2 gpu;bool gpuReady,gpuLoading,gpuUnavailable;Image gpuCover;string gpuArt,gpuKey;int gpuLyricRevision;string gpuSentRevision;
    readonly System.Windows.Forms.Timer gpuTimer=new System.Windows.Forms.Timer{Interval=100};
    bool UseGpu {get{return gpuReady&&featuresState.GpuPlayer!=false;}}
    ContextMenuStrip gpuOptionsMenu;DateTime gpuMenuDismissed=DateTime.MinValue;
    bool CloseGpuOptions(){var menu=gpuOptionsMenu;if(menu==null||menu.IsDisposed||!menu.Visible)return false;menu.Close();return true;}
    void TrackGpuOptions(ContextMenuStrip menu,Control owner){
        if(owner!=gpuPage)return;CloseGpuOptions();gpuOptionsMenu=menu;
        menu.Closed+=delegate(object sender,ToolStripDropDownClosedEventArgs e){if(gpuOptionsMenu==menu)gpuOptionsMenu=null;if(e.CloseReason==ToolStripDropDownCloseReason.AppClicked)gpuMenuDismissed=DateTime.UtcNow;};
    }
    void BuildGpuPlayer(){
        Deactivate+=delegate{CloseGpuOptions();};stage.Controls.Add(gpuPage);gpuPage.BackColor=Theme.Ink;gpuPage.Resize+=delegate{if(gpu!=null)gpu.SetBounds(0,0,gpuPage.Width,gpuPage.Height);};
        gpuTimer.Tick+=delegate{UpdateGpuPlayer();};gpuTimer.Start();FormClosing+=delegate{gpuTimer.Dispose();if(gpu!=null)gpu.Dispose();if(canvasRequest!=null)canvasRequest.Cancel();};
    }
    async void EnsureGpuPlayer(){
        if(gpuReady||gpuLoading||gpuUnavailable||featuresState.GpuPlayer==false)return;gpuLoading=true;
        try{
            gpu=new WebView2{DefaultBackgroundColor=Theme.Ink,Dock=DockStyle.Fill};gpuPage.Controls.Add(gpu);WindowLayout();
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Catalog.Root,"data","player-webview"));if(closing)return;await gpu.EnsureCoreWebView2Async(env);if(closing)return;
            Directory.CreateDirectory(CanvasCache.Root);gpu.CoreWebView2.SetVirtualHostNameToFolderMapping("yuma-assets.local",CanvasCache.Root,CoreWebView2HostResourceAccessKind.DenyCors);
            gpu.CoreWebView2.ProcessFailed+=delegate{if(closing)return;gpuUnavailable=true;gpuReady=gpuLoading=false;WindowLayout();};gpu.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;gpu.CoreWebView2.Settings.IsStatusBarEnabled=false;gpu.CoreWebView2.Settings.IsZoomControlEnabled=false;
            gpu.CoreWebView2.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){if(e.Uri!="about:blank"&&!(gpuLoading&&e.Uri.StartsWith("data:text/html",StringComparison.OrdinalIgnoreCase)))e.Cancel=true;};gpu.CoreWebView2.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
            gpu.CoreWebView2.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){if(e.Source!="about:blank")return;try{var data=Catalog.Json().DeserializeObject(e.WebMessageAsJson);string command=Convert.ToString(Catalog.Field(data,"cmd"));double value;double.TryParse(Convert.ToString(Catalog.Field(data,"value")),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out value);if(double.IsNaN(value)||double.IsInfinity(value))return;GpuCommand(command,value);}catch(ArgumentException){}};
            using(var stream=typeof(Player).Assembly.GetManifestResourceStream("GpuPlayer.html"))using(var reader=new StreamReader(stream))gpu.NavigateToString(reader.ReadToEnd());
            await Task.Delay(10000);if(!closing&&!gpuReady){gpuUnavailable=true;gpuLoading=false;if(gpu!=null){gpu.Dispose();gpu=null;}WindowLayout();}
        }catch(Exception ex){if(closing)return;gpuError=ex.ToString();System.Diagnostics.Debug.WriteLine("GPU player: "+ex.GetType().Name);if(gpu!=null){gpu.Dispose();gpu=null;}gpuUnavailable=true;gpuReady=false;gpuLoading=false;WindowLayout();}
    }
    void GpuCommand(string command,double value){
        if(closing)return;
        switch(command){
            case "ready":gpuReady=true;gpuLoading=false;WindowLayout();UpdateGpuPlayer();break;
            case "play":Toggle();break;case "retry":RetryPlayback();break;case "floating":ShowMiniPlayer();break;case "seekBy":if(audio.Seekable)SeekTo((long)Math.Max(0,Math.Min(audio.Length,audio.Time+Math.Max(-5000,Math.Min(5000,value)))));break;case "volumeBy":volume.Value=Math.Max(0,Math.Min(100,volume.Value+(int)Math.Max(-5,Math.Min(5,value))));break;case "next":Step(1);break;case "previous":Step(-1);break;
            case "back":ShowPage(page=="Lyrics"?"Player":"Home");break;
            case "lyrics":ShowPage("Lyrics");break;case "queue":ShowPage("Queue");break;
            case "shuffle":shuffle=!shuffle;ResetTransition();break;case "repeat":repeat=!repeat;ResetTransition();break;
            case "volume":volume.Value=Math.Max(0,Math.Min(100,(int)value));break;
            case "seek":if(audio.Seekable)SeekTo((long)(audio.Length*Math.Max(0,Math.Min(1,value))));break;
            case "line":int i=(int)value;if(i>=0&&i<richLyrics.Count){lyricRows.SelectedIndex=i;if(richLyrics[i].Time>=0&&audio.Seekable)SeekTo(Math.Max(0,richLyrics[i].Time-LyricOffset));}break;
            case "dismissMenu":CloseGpuOptions();break;
            case "escape":if(!CloseGpuOptions())ShowPage(page=="Lyrics"?"Player":"Home");break;
            case "menu":if(CloseGpuOptions()||(DateTime.UtcNow-gpuMenuDismissed).TotalMilliseconds<200)break;if(page=="Lyrics")LyricOptions(gpuPage);else ShowTrackMenu(Current,gpuPage,new Point(Math.Max(0,gpuPage.Width-230),64));break;
            case "like":int found=liked.FindIndex(t=>Key(t)==Key(Current));if(found<0){MarkLibraryAdded(Current);liked.Add(Current);UpdatePicks(Current);}else liked.RemoveAt(found);SaveLibrary();RefreshSurfaces();break;
        }
    }
    void UpdateGpuPlayer(){
        if(closing||page!="Player"&&page!="Lyrics")return;if(!gpuReady){EnsureGpuPlayer();return;}if(!UseGpu||gpu==null||gpu.CoreWebView2==null)return;
        var cover=Cover(Current);bool artChanged=gpuCover!=cover;if(artChanged){gpuCover=cover;using(var buffer=new MemoryStream()){cover.Save(buffer,ImageFormat.Png);gpuArt="data:image/png;base64,"+Convert.ToBase64String(buffer.ToArray());}}
        string key=Key(Current);if(gpuKey!=key){gpuKey=key;RefreshCanvas(Current);}
        var palette=AdaptivePalette.From(ArtworkTone(cover));
        var lines=richLyrics.Select(l=>new{text=l.Text,time=l.Time,pronunciation=l.Pronunciation,translation=l.Translation,words=l.Words==null?null:l.Words.Select(w=>new{text=w.Text,start=w.Start,end=w.End}).ToArray()}).ToArray();
        string revision=lyricVersion+"|"+gpuLyricRevision+"|"+richLyrics.Count+"|"+(richLyrics.Count>0?richLyrics[0].Translation:null)+"|"+(richLyrics.Count>0?richLyrics[0].Pronunciation:null);
        var state=new{page=page,title=Current.Title??"Choose a song",artist=Current.Artist??"",source=Current.QobuzId>0?"QOBUZ · LOSSLESS":Current.File!=null?"LOCAL AUDIO":"YOUTUBE MUSIC",quality=audio.Managed?"48 kHz · Float PCM · "+(double.IsNaN(audio.Loudness)?"Preparing":audio.Loudness.ToString("0.0")+" LUFS"):"Compatibility audio",position=Math.Max(0,audio.Time),length=Math.Max(0,audio.Length),status=PlaybackDescription(),canRetry=index>=0&&(starting!=null||!active||audio.Buffering),playing=active&&audio.State==3&&!audio.Buffering,rate=featuresState.PlaybackRate,volume=volume.Value,shuffle=shuffle,repeat=repeat,like=liked.Any(t=>Key(t)==key),reduced=reduceMotion,art=artChanged?gpuArt:null,accent=AdaptivePalette.Hex(palette.Accent),tone=AdaptivePalette.Hex(palette.Tone),canvas=featuresState.CanvasVideo&&!reduceMotion?canvasUrl:null,revision=revision,lines=revision!=gpuSentRevision?lines:null,lyricStatus=lyricSource.Text,offset=LyricOffset};
        gpuSentRevision=revision;try{gpu.CoreWebView2.PostWebMessageAsJson(Catalog.Json().Serialize(state));}catch(InvalidOperationException){}
    }
}
