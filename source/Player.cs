using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal partial class Player : Form {
    readonly TextBox query=new TextBox();
    bool useQobuz;
    Qobuz qobuz;
    readonly ListView results=new ResultsList(), queueView=new ResultsList();
    readonly Label status=new Label(), now=new Label(), clock=new Label(), searchStatus=new Label();
    readonly TrackBar position=new Meter(), volume=new Meter();
    readonly List<Track> queue=new List<Track>();
    Audio audio=new Audio();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=250 };
    CancellationTokenSource searching, starting, primeCancel;
    Task primeTask;string primeId;
    int index=-1;
    bool active, dragging, closing, playedAudio, triedIPv4;
    DateTime startTime;long resumeAt,lastPosition;
    Button pause;

    Player() {
        BuildWindow();InitTransitions();InitAdvancedPlayback();BuildFloatingNavigation();InitReliability();
        timer.Tick+=delegate{RefreshAudio();UpdateProgressMotion();};timer.Start();
        FormClosing+=delegate { closing=true;timer.Stop();ClearPrime();if(searching!=null)searching.Cancel();if(starting!=null)starting.Cancel();audio.Dispose(); };
        
    }
    static ListViewItem Row(Track t) { var r=new ListViewItem(new[]{t.Title,t.Artist});r.Tag=t;return r; }
    async Task Search() {
        DismissSuggestions();string q=query.Text.Trim();if(q.Length==0){query.Focus();return;}
        bool searchQobuz=useQobuz;
        if(searchQobuz && qobuz==null){qobuz=Qobuz.Connect(this);if(qobuz==null)return;}
        if(searching!=null)searching.Cancel();var req=new CancellationTokenSource(TimeSpan.FromSeconds(90));searching=req;
        ShowSearchResults();searchStatus.Text="Searching…";
        try {
            var found=searchQobuz?await qobuz.Search(q,req.Token):searchKind=="Songs"?await Catalog.Search(q,req.Token):await Catalog.SearchEntities(q,searchKind,req.Token);if(req.IsCancellationRequested||closing)return;
            results.BeginUpdate();try{results.Items.Clear();foreach(var t in found)results.Items.Add(Row(t));}finally{results.EndUpdate();}ResultRowsChanged();
            // Start the first viewport immediately; scrolling requests further rows on demand.
            for(int i=0;i<Math.Min(found.Count,Math.Max(1,resultClip.Height/54+2));i++)FetchArtwork(found[i]);
            searchStatus.Text=found.Count==0?"No matches. Try a different song or artist.":found.Count+" results for “"+q+"”";
        } catch(OperationCanceledException){if(searching==req && !closing)searchStatus.Text="Search timed out. Try again.";}
        catch(Exception e){if(searching==req && !closing)searchStatus.Text=e.Message;}
        finally{if(searching==req)searching=null;req.Dispose();}
    }
    void OpenFiles() {
        using(var d=new OpenFileDialog{Multiselect=true,Filter="Music files|*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wav;*.wma;*.aiff;*.mp4|All files|*.*"}) {
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            int first=queue.Count;
            foreach(string f in d.FileNames)Add(new Track{Title=Path.GetFileNameWithoutExtension(f),Artist="Local file",File=f},false);
            Play(first);
        }
    }
    void Add(Track t,bool play) { if(t.Kind=="Empty"){ShowPage("Settings");return;} if(!string.IsNullOrEmpty(t.Kind)){OpenCollection(t);return;} queue.Add(t);queueView.Items.Add(Row(t));queueEmpty.Visible=false;ScrollVisible(queueView,true);queueTitle.Text="Queue  ·  "+queue.Count;RefreshSurfaces();if(play) Play(queue.Count-1); }
    void ClearPrime(){
        var cancel=primeCancel;primeCancel=null;primeId=null;primeTask=null;
        if(cancel!=null)cancel.Cancel();
    }
    void PrimeNext(){
        int n=index+1;
        string wanted=!shuffle&&!repeat&&n<queue.Count&&queue[n].QobuzId==0&&queue[n].File==null&&!IsSpotifySong(queue[n])?queue[n].Id:null;
        if(wanted==primeId)return;
        ClearPrime();if(wanted==null)return;
        primeId=wanted;primeCancel=new CancellationTokenSource();primeTask=Prepare(queue[n],primeCancel);
    }
    async Task Prepare(Track track,CancellationTokenSource cancel){
        try{await Catalog.Prime(track,cancel.Token);}
        catch(OperationCanceledException){}
        catch(System.IO.IOException){}
        finally{if(primeCancel==cancel)primeCancel=null;cancel.Dispose();}
    }
    void Play(int n) { StartTrack(n,false); }
    async void StartTrack(int n,bool retryIPv4,long resume=0) {
        if(n<0||n>=queue.Count||closing)return;
        restoredPosition=0;ResetTransition();if(starting!=null)starting.Cancel();resumeAt=resume;lastPosition=0;active=false;playedAudio=false;triedIPv4=retryIPv4;audio.Stop();var outgoingTrack=Current;index=n;var t=queue[n];
        if(mini.Visible&&Key(outgoingTrack)!=Key(t)){previousCover=Cover(outgoingTrack);coverMix.To(1,175,true);}
        now.Text=t.Title;status.Text=retryIPv4?"Retrying audio…":"Finding audio…";pause.Text="Play";position.Enabled=false;position.Value=0;clock.Text="0:00 / 0:00";
        FetchArtwork(t,true);RefreshSurfaces();queueView.Items[n].Selected=true;queueView.Items[n].EnsureVisible();queueView.Invalidate();
        var req=new CancellationTokenSource(TimeSpan.FromSeconds(90));starting=req;
        try {
            if(IsSpotifySong(t)){status.Text="Matching Spotify recording on YouTube Music…";t=await MatchSpotify(t,req.Token);req.Token.ThrowIfCancellationRequested();if(closing)return;queue[n]=t;now.Text=t.Title;FetchArtwork(t,true);RebuildQueue();}
            var prepared=!retryIPv4&&primeId!=null&&primeId==t.Id?primeTask:null;
            if(prepared==null)ClearPrime();else await prepared;
            req.Token.ThrowIfCancellationRequested();
            string local=OfflinePath(t);if(t.QobuzId>0&&local==null&&qobuz==null){qobuz=Qobuz.Connect(this);if(qobuz==null)return;}string url;if(local!=null)url=new Uri(local).AbsoluteUri;else if(t.QobuzId>0){var resolved=await ResolveQobuzPlayback(t,req.Token);req.Token.ThrowIfCancellationRequested();url=resolved.Item1;if(resolved.Item2!=t){t=resolved.Item2;queue[n]=t;now.Text=t.Title;RebuildQueue();}}else url=await Catalog.Resolve(t,req.Token,retryIPv4,prepared!=null);if(closing||req.IsCancellationRequested)return;
            if(audio.Normalized!=featuresState.NormalizeVolume){audio.Dispose();audio=new Audio(featuresState.NormalizeVolume);}audio.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);audio.Start(url);audio.ExpectedLength(t.DurationSeconds*1000L);audio.SetRate(featuresState.PlaybackRate);audio.SetPitch(featuresState.PitchSemitones);audio.Volume(volume.Value);startTime=DateTime.UtcNow;active=true;
        } catch(OperationCanceledException){if(starting==req&&!closing)status.Text="Opening timed out. Press Play to retry.";}
        catch(Exception e){if(starting==req&&!closing)status.Text=e.Message;}
        finally{if(starting==req)starting=null;req.Dispose();}
    }
    void Toggle() {
        if(starting!=null)return;
        if(!active){if(queue.Count>0){long resume=restoredPosition;restoredPosition=0;StartTrack(Math.Max(0,index),false,resume);}else Add(picks[2],true);return;}
        if(audio.State==3 || audio.State==4)PausePlayback(audio.State==3);
    }
    void Step(int delta) { if(delta>0&&active&&audio.Time>0&&audio.Time<30000&&index>=0)CountListen(Current,true); int n=shuffle&&delta>0&&queue.Count>1?(Math.Max(0,index)+1+order.Next(queue.Count-1))%queue.Count:index+delta;if(n>=0 && n<queue.Count)Play(n); }
    void PausePlayback(bool paused){audio.Pause(paused);if(outgoing!=null)outgoing.Pause(paused);if(paused)reconnectKey=null;}
    void SeekTo(long position){ResetTransition();audio.Seek(position);}
    void Seek(){if(active&&audio.Length>0)SeekTo(audio.Length*position.Value/1000);}
    static string Stamp(long ms) { long seconds=Math.Max(0,ms/1000);return seconds/60+":"+(seconds%60).ToString("00"); }
    void RefreshAudio() {
        if(!active)return;
        int state=audio.State;
        if(audio.ReaderFailed&&!triedIPv4){StartTrack(index,true,lastPosition);return;}
        if(resumeAt>0&&state==3&&audio.Seekable){SeekTo(resumeAt);resumeAt=0;}
        if(audio.Time>0){if(!playedAudio)Remember(queue[index]);playedAudio=true;lastPosition=audio.Time;}
        if((state==6 || state==7) && !playedAudio) {
            if(!triedIPv4 && queue[index].File==null && queue[index].QobuzId==0) {StartTrack(index,true);return;}
            active=false;pause.Text="Play";status.Text="This stream could not play. Try another recording or music source.";return;
        }
        if(state==6) { if(!repeat&&!shuffle&&AdvancePrepared())return;active=false;pause.Text="Play";status.Text="Finished";if(repeat)Play(index);else Step(1);return; }
        if(state==3&&playedAudio){PrimeNext();if(featuresState.CacheNext&&index+1<queue.Count&&queue[index+1].QobuzId==0&&string.IsNullOrEmpty(queue[index+1].File)&&downloads.Count==0&&backgroundNextKey!=Key(queue[index+1])){backgroundNextKey=Key(queue[index+1]);DownloadTrack(queue[index+1],true);}}
        if(state==3 || state==4)startTime=DateTime.UtcNow;
        if(state==7 || ((state<=2 || state==5) && DateTime.UtcNow-startTime>TimeSpan.FromSeconds(45))) {
            active=false;audio.Stop();pause.Text="Play";status.Text="The audio stream could not be opened. Press Play to retry or choose another recording.";return;
        }
        pause.Text=state==3?"Pause":"Play";status.Text=state==4?"Paused":audio.Buffering?"Buffering…":state==3?(queue[index].QobuzId>0?"Qobuz FLAC · ":"")+queue[index].Artist:"Buffering…";
        long time=audio.Time,len=audio.Length;if(len>0&&index>=0)Current.DurationSeconds=(int)(len/1000);position.Enabled=audio.Seekable&&len>0;
        if(!dragging&&len>0)position.Value=(int)Math.Max(0,Math.Min(1000,time*1000/len));
        clock.Text=Stamp(time)+" / "+Stamp(len);
    }
    [STAThread] static void Main() {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try {Application.Run(new Player());} catch(Exception e){DialogUI.Show(e.Message,"Noctune",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
