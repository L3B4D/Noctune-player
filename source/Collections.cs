using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal partial class Player {
    public sealed class SavedPlaylist {public string Id{get;set;}public string Name{get;set;}public string Cover{get;set;}public List<Track> Songs{get;set;}}
    public sealed class ListeningScore {public int Plays{get;set;}public int Skips{get;set;}}
    public sealed class FeatureState {
        public List<SavedPlaylist> Playlists{get;set;}public Dictionary<string,ListeningScore> Scores{get;set;}public List<string> Hidden{get;set;}
        public List<Track> Queue{get;set;}public int Index{get;set;}public long Position{get;set;}public int Volume{get;set;}
        public string TranslationServer{get;set;}public string TranslationModel{get;set;}public string TranslationLanguage{get;set;}public bool? GpuPlayer{get;set;}public bool CanvasVideo{get;set;}public float PitchSemitones{get;set;}public bool SmartAutomix{get;set;}public bool FloatingNavigation{get;set;}public int DockX{get;set;}public int DockY{get;set;}public bool BassSwap{get;set;}public bool LosslessFallback{get;set;}public bool SponsorSkip{get;set;}public bool SkipNonMusic{get;set;}public bool SkipFiller{get;set;}public bool CacheNext{get;set;}public float PlaybackRate{get;set;}public Dictionary<string,int> LyricOffsets{get;set;} public List<Track> SavedAlbums{get;set;} public List<Track> FollowedArtists{get;set;} public bool EqualizerEnabled{get;set;} public float[] Equalizer{get;set;} public bool NormalizeVolume{get;set;} public bool? Gapless{get;set;}
        public bool InAppPlayerMoved{get;set;}public int InAppPlayerX{get;set;}public int InAppPlayerY{get;set;}public bool? MiniEnabled{get;set;}public bool CompatibilityAudio{get;set;}public int DownloadLimitMb{get;set;}public List<string> BlockedArtists{get;set;}public int MiniX{get;set;}public int MiniY{get;set;}public bool MiniPositionSet{get;set;}public bool? MiniPinned{get;set;}
        public int Crossfade{get;set;}public bool Autoplay{get;set;}public bool MinimizeToTray{get;set;}
    }
    readonly string featurePath=Path.Combine(Catalog.Root,"data","features.json");
    FeatureState featuresState=new FeatureState{Playlists=new List<SavedPlaylist>(),Scores=new Dictionary<string,ListeningScore>(),Hidden=new List<string>(),Queue=new List<Track>(),Index=-1,Volume=70,Autoplay=true};
    readonly Surface collectionPage=new Surface();readonly ListView collectionRows=new ResultsList();
    readonly Label collectionTitle=new Label(),collectionNote=new Label();
    ArtButton collectionCover,collectionManage;ChoiceButton categoryPicker;Track displayedCollection;SavedPlaylist editingPlaylist;
    CancellationTokenSource collectionRequest;string searchKind="Songs";long restoredPosition;
    void LoadFeatures(){
        try{if(File.Exists(featurePath)){var data=Catalog.Json().Deserialize<FeatureState>(File.ReadAllText(featurePath));if(data!=null)featuresState=data;}}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
        if(featuresState.BlockedArtists==null)featuresState.BlockedArtists=new List<string>();if(featuresState.DownloadLimitMb<100)featuresState.DownloadLimitMb=2048;
        if(featuresState.Playlists==null)featuresState.Playlists=new List<SavedPlaylist>();if(featuresState.Scores==null)featuresState.Scores=new Dictionary<string,ListeningScore>();if(featuresState.Hidden==null)featuresState.Hidden=new List<string>();
        if(featuresState.PlaybackRate<.5f||featuresState.PlaybackRate>2f)featuresState.PlaybackRate=1f;if(featuresState.LyricOffsets==null)featuresState.LyricOffsets=new Dictionary<string,int>();if(featuresState.SavedAlbums==null)featuresState.SavedAlbums=new List<Track>();if(featuresState.FollowedArtists==null)featuresState.FollowedArtists=new List<Track>();if(featuresState.Equalizer==null||featuresState.Equalizer.Length!=10)featuresState.Equalizer=new float[10];
        featuresState.Playlists.RemoveAll(p=>p==null||string.IsNullOrEmpty(p.Id));foreach(var p in featuresState.Playlists)if(p.Songs==null)p.Songs=new List<Track>();
    }
    void SaveFeatures(){
        featuresState.Queue=queue.ToList();featuresState.Index=index;featuresState.Position=active?Math.Max(0,audio.Time):restoredPosition;featuresState.Volume=volume.Value;
        try{Directory.CreateDirectory(Path.GetDirectoryName(featurePath));string tmp=featurePath+".tmp";File.WriteAllText(tmp,Catalog.Json().Serialize(featuresState));if(File.Exists(featurePath))File.Replace(tmp,featurePath,null);else File.Move(tmp,featurePath);}catch(IOException){status.Text="Could not save library changes.";}catch(UnauthorizedAccessException){status.Text="Library storage is read-only.";}
    }
    static string AskText(IWin32Window owner,string title,string initial){
        using(var dialog=DialogUI.Create(title,"Give your music a place of its own.",440,270)){
            var input=DialogUI.Field(dialog,"Playlist name",initial,24,117,392);input.MaxLength=150;
            var cancel=new SoftButton("Cancel",delegate{dialog.DialogResult=DialogResult.Cancel;});cancel.SetBounds(196,212,100,36);dialog.Controls.Add(cancel);dialog.CancelButton=cancel;
            var ok=new SoftButton("Save",delegate{if(input.Text.Trim().Length>0)dialog.DialogResult=DialogResult.OK;},true);ok.SetBounds(306,212,110,36);dialog.Controls.Add(ok);dialog.AcceptButton=ok;dialog.Shown+=delegate{input.Focus();input.SelectAll();};
            return dialog.ShowDialog(owner)==DialogResult.OK?input.Text.Trim():null;
        }
    }
    SavedPlaylist NewPlaylist(){string name=AskText(this,"Create playlist","");if(name==null)return null;var p=new SavedPlaylist{Id=Guid.NewGuid().ToString("N"),Name=name,Songs=new List<Track>()};featuresState.Playlists.Add(p);SaveFeatures();return p;}
    Track PlaylistTrack(SavedPlaylist p){return new Track{Title=p.Name,Artist=p.Songs.Count+" songs",Kind="Local playlist",BrowseId="local:"+p.Id,Artwork=p.Songs.Count>0?p.Songs[0].Artwork:null};}
    void BuildCollections(){
        LoadFeatures();collectionPage.BackColor=Theme.Ink;stage.Controls.Add(collectionPage);
        collectionTitle.Font=Theme.Font(23,true);collectionTitle.ForeColor=Theme.Paper;collectionTitle.AutoEllipsis=true;collectionTitle.BackColor=collectionNote.BackColor=Color.Transparent;collectionNote.ForeColor=Theme.Muted;
        collectionPage.Controls.Add(collectionTitle);collectionPage.Controls.Add(collectionNote);
        collectionCover=Make(collectionPage,"Collection artwork",delegate{},delegate(Graphics g,Rectangle r){if(displayedCollection!=null)DrawRowArtwork(g,displayedCollection,r);});collectionCover.TabStop=false;
        var play=Pill(collectionPage,"Play all",delegate{PlayAlbum(false);},true);
        var save=Pill(collectionPage,"Save as playlist",delegate{var p=NewPlaylist();if(p==null)return;p.Songs=collectionRows.Items.Cast<ListViewItem>().Select(x=>(Track)x.Tag).Where(x=>string.IsNullOrEmpty(x.Kind)).ToList();SaveFeatures();OpenCollection(PlaylistTrack(p));});
        var manage=collectionManage=Pill(collectionPage,"Manage",ManagePlaylist);
        SetList(collectionRows,collectionPage,"Collection tracks");DarkScrollHost.Wrap(collectionRows).Visible=true;collectionRows.Visible=true;InstallTrackMenu(collectionRows);collectionRows.AllowDrop=true;
        EnableCollectionReorder();
        collectionPage.Resize+=delegate{int w=collectionPage.Width,h=collectionPage.Height;Place(collectionCover,18,18,105,105);Place(collectionTitle,140,20,w-158,65);Place(collectionNote,140,87,w-158,30);Place(play,18,140,95,35);Place(save,121,140,140,35);Place(manage,269,140,92,35);Place(collectionRows,12,190,w-24,h-202);manage.Visible=editingPlaylist!=null;};
        ArtButton menu=Pill(library,"Collections",delegate{ShowPage("Collections");});
        library.Resize+=delegate{Place(menu,16,104,125,30);Place(libraryTitle,150,106,Math.Max(1,library.Width-166),31);};
        var filter=categoryPicker=new ChoiceButton{BackColor=Theme.Card,ForeColor=Theme.Paper,FlatStyle=FlatStyle.Flat,AccessibleName="Search category"};filter.Items.AddRange(new object[]{"Songs","Artists","Albums","Playlists"});filter.SelectedIndex=0;searchPage.Controls.Add(filter);
        searchPage.Resize+=delegate{Place(filter,Math.Max(242,searchPage.Width-166),57,Math.Min(145,Math.Max(60,searchPage.Width-250)),28);};filter.SelectedIndexChanged+=delegate{searchKind=filter.SelectedItem.ToString();if(useQobuz&&searchKind!="Songs"){filter.SelectedIndex=0;searchStatus.Text="Qobuz search currently supports songs.";return;}if(query.Text.Trim().Length>0)RunSearch();};
        FormClosing+=delegate{SaveFeatures();if(collectionRequest!=null)collectionRequest.Cancel();};
        for(int i=0;i<features.Length;i++){int k=i;features[i].MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Right)ShowTrackMenu(HomePicks[(hero+k)%HomePicks.Length],features[k],e.Location);};}
        for(int i=0;i<shelf.Length;i++){int k=i;shelf[i].MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Right)ShowTrackMenu(ShelfTrack(k),shelf[k],e.Location);};}
        miniTrack.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Right)ShowTrackMenu(Current,miniTrack,e.Location);};InstallTrackMenu(queueView);
        InitOffline();
    }
    bool FillSpecialLibrary(){
        if(listMode=="Playlists"){libraryRows.Items.Clear();libraryTitle.Text="Your playlists";foreach(var p in featuresState.Playlists)libraryRows.Items.Add(Row(PlaylistTrack(p)));return true;}
        if(listMode=="Offline"){libraryRows.Items.Clear();libraryTitle.Text="Offline music";foreach(var t in OfflineTracks())libraryRows.Items.Add(Row(t));return true;}
        if(listMode=="Most played"){libraryRows.Items.Clear();libraryTitle.Text="Most played";foreach(var t in recent.Concat(liked).GroupBy(Key).Select(g=>g.First()).OrderByDescending(Score))libraryRows.Items.Add(Row(t));return true;}
        return false;
    }
    void FillCollection(IEnumerable<Track> tracks){collectionContents=tracks.ToList();RenderCollection();}
    async void OpenCollection(Track entity){
        RememberCollectionNavigation(entity);if(collectionRequest!=null)collectionRequest.Cancel();displayedCollection=entity;editingPlaylist=null;collectionTitle.Text=entity.Title;collectionNote.Text="Loading…";collectionRows.Items.Clear();collectionContents.Clear();RefreshCollectionGallery();collectionQuery.Text="";artistSection.SelectedIndex=0;collectionRetry.Visible=false;artistFollow.Visible=entity.Kind=="Artist";UpdateFollowButton();LayoutCollectionExtras();
        if(entity.Kind=="Local playlist")editingPlaylist=featuresState.Playlists.Find(x=>"local:"+x.Id==entity.BrowseId);
        collectionManage.Visible=editingPlaylist!=null;ShowPage("Collection");if(editingPlaylist==null||string.IsNullOrEmpty(editingPlaylist.Cover))FetchArtwork(entity,true);
        if(editingPlaylist!=null){if(!string.IsNullOrEmpty(editingPlaylist.Cover)&&File.Exists(editingPlaylist.Cover)){try{using(var image=Image.FromFile(editingPlaylist.Cover)){Image old;if(images.TryGetValue(Key(entity),out old))replacedArt.Add(old);images[Key(entity)]=new Bitmap(image);}}catch(ArgumentException){}catch(IOException){}}FillCollection(editingPlaylist.Songs);collectionCover.Invalidate();return;}
        collectionNote.Text="Loading…";artistDescription.Text="";
        var req=new CancellationTokenSource(TimeSpan.FromSeconds(45));collectionRequest=req;
        try{List<Track> tracks;if(entity.Kind=="Artist"){var profile=await Catalog.Artist(entity.BrowseId,req.Token);tracks=profile.Tracks;if(!closing&&!req.IsCancellationRequested){artistDescription.Text=profile.Description.Length>0?profile.Description:"Explore songs, releases and related artists.";if(!string.IsNullOrEmpty(profile.Artwork)){entity.Artwork=profile.Artwork;FetchArtwork(entity,true);}}}else if((entity.BrowseId??"").StartsWith("spotify:"))tracks=await SpotifyCatalog.Browse(entity,req.Token);else if(entity.Kind=="Album")tracks=await Catalog.Album(entity,req.Token);else tracks=await Catalog.Browse(entity.BrowseId,req.Token,entity.BrowseParams);if(closing||req.IsCancellationRequested)return;FillCollection(tracks);}
        catch(OperationCanceledException){if(!closing&&collectionRequest==req){collectionNote.Text="Loading timed out. Try again.";collectionRetry.Visible=true;}}catch(Exception ex){if(!closing&&collectionRequest==req){collectionNote.Text=ex.Message;collectionRetry.Visible=true;}}
        finally{if(collectionRequest==req)collectionRequest=null;req.Dispose();}
    }
    bool ChoosePlaylistCover(SavedPlaylist p){
        using(var picker=new OpenFileDialog{Filter="Images|*.jpg;*.jpeg;*.png;*.bmp"}){
            if(picker.ShowDialog(this)!=DialogResult.OK)return false;
            try{string dir=Path.Combine(Catalog.Root,"data","playlist-covers");Directory.CreateDirectory(dir);string file=Path.Combine(dir,p.Id+".png");using(var src=Image.FromFile(picker.FileName))using(var scaled=new Bitmap(600,600))using(var g=Graphics.FromImage(scaled)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;RenderArtwork(g,src,new Rectangle(0,0,600,600),1);scaled.Save(file,System.Drawing.Imaging.ImageFormat.Png);}p.Cover=file;SaveFeatures();OpenCollection(PlaylistTrack(p));return true;}
            catch(Exception ex){DialogUI.Show(this,ex.Message,"Cover could not be saved");return false;}
        }
    }
    Form PlaylistEditor(SavedPlaylist p){
        var d=DialogUI.Create("Edit playlist","Make this collection your own.",520,410);
        var cover=Make(d,"Playlist cover",delegate{},delegate(Graphics g,Rectangle r){Artwork(g,Cover(PlaylistTrack(p)),r,14);});Place(cover,24,116,112,112);
        var name=DialogUI.Field(d,"Playlist name",p.Name,158,116,338);name.MaxLength=150;
        var change=new SoftButton("Change cover",delegate{if(ChoosePlaylistCover(p))cover.Invalidate();});Place(change,24,239,112,36);d.Controls.Add(change);
        DialogUI.Label(d,p.Songs.Count+" songs · Drag rows in the playlist to reorder them.",158,209,338,66,10);
        var remove=new SoftButton("Remove selected song",delegate{if(collectionRows.SelectedIndices.Count>0){p.Songs.Remove((Track)collectionRows.SelectedItems[0].Tag);SaveFeatures();FillCollection(p.Songs);d.Close();}}){Enabled=collectionRows.SelectedIndices.Count>0};Place(remove,24,294,186,35);d.Controls.Add(remove);
        var delete=new SoftButton("Delete playlist",delegate{if(DialogUI.Show(d,"Delete “"+p.Name+"”? Your songs and audio files will be kept.","Delete playlist",MessageBoxButtons.YesNo)==DialogResult.Yes){featuresState.Playlists.Remove(p);SaveFeatures();d.Close();ShowPage("Collections");}});Place(delete,24,351,130,36);d.Controls.Add(delete);
        var cancel=new SoftButton("Cancel",delegate{d.DialogResult=DialogResult.Cancel;});Place(cancel,276,351,100,36);d.Controls.Add(cancel);d.CancelButton=cancel;
        var save=new SoftButton("Save changes",delegate{if(name.Text.Trim().Length==0){name.Focus();return;}p.Name=name.Text.Trim();SaveFeatures();OpenCollection(PlaylistTrack(p));d.DialogResult=DialogResult.OK;},true);Place(save,386,351,110,36);d.Controls.Add(save);d.AcceptButton=save;return d;
    }
    void ManagePlaylist(){if(editingPlaylist!=null)using(var dialog=PlaylistEditor(editingPlaylist))dialog.ShowDialog(this);}
    void AddTrackFeatures(ContextMenuStrip m,Track t){
        var add=new ToolStripMenuItem("Add to playlist");add.DropDownItems.Add("New playlist…",null,delegate{var p=NewPlaylist();if(p!=null){AddToPlaylist(p,t);}});foreach(var entry in featuresState.Playlists){var p=entry;add.DropDownItems.Add(p.Name,null,delegate{AddToPlaylist(p,t);});}m.Items.Add(add);
        m.Items.Add("Go to artist",null,async delegate{if(!string.IsNullOrEmpty(t.ArtistId))OpenCollection(new Track{Kind="Artist",BrowseId=t.ArtistId,Title=t.Artist,Artwork=t.Artwork});else{ShowPage("Search");query.Text=t.Artist;if(categoryPicker.SelectedIndex!=1)categoryPicker.SelectedIndex=1;else await Search();}});
        if(!string.IsNullOrEmpty(t.AlbumId))m.Items.Add("Go to album",null,delegate{OpenCollection(new Track{Kind="Album",BrowseId=t.AlbumId,Title=t.Album,Artwork=t.Artwork});});
        m.Items.Add("Show less like this",null,delegate{filteredDefaults=null;if(!featuresState.Hidden.Contains(Key(t)))featuresState.Hidden.Add(Key(t));SaveFeatures();if(recommended!=null){recommended=recommended.Where(x=>!featuresState.Hidden.Contains(Key(x))).ToArray();if(recommended.Length==0)recommended=null;hero=0;PaintPicks();}});
        m.Items.Add("Reload artwork",null,delegate{qualityLoaded.Remove(Key(t)??"");fullArtwork.Remove(Key(t)??"");FetchArtwork(t);});
        if(!string.IsNullOrWhiteSpace(t.Artist))m.Items.Add("Don’t recommend this artist",null,delegate{BlockArtist(t);});
        AddOfflineMenu(m,t);
    }
    double Score(Track t){ListeningScore s;return (featuresState.Scores.TryGetValue(Key(t)??"",out s)?s.Plays-s.Skips*2:0)+(liked.Exists(x=>Key(x)==Key(t))?5:0);}
    void CountListen(Track t,bool skip){string key=Key(t);if(key==null)return;ListeningScore s;if(!featuresState.Scores.TryGetValue(key,out s))featuresState.Scores[key]=s=new ListeningScore();if(skip)s.Skips++;else s.Plays++;SaveFeatures();}
    async void RefreshPersonalMix(){
        if(radioRequest!=null)radioRequest.Cancel();var req=new CancellationTokenSource(TimeSpan.FromSeconds(35));radioRequest=req;
        var seeds=liked.Concat(recent).Where(t=>!string.IsNullOrEmpty(t.Id)&&Recommendable(t)).GroupBy(Key).Select(g=>g.First()).OrderByDescending(Score).Take(3).ToList();
        if(seeds.Count==0){req.Dispose();radioRequest=null;return;}
        try{var stations=await System.Threading.Tasks.Task.WhenAll(seeds.Select(seed=>Catalog.Radio(seed,req.Token)));var mixed=new List<Track>();for(int i=0;i<seeds.Count;i++){mixed.Add(seeds[i]);mixed.AddRange(stations[i].Take(7));}if(closing||req.IsCancellationRequested)return;
            recommended=DiverseRecommendations(mixed).Take(24).ToArray();if(recommended.Length==0)recommended=null;hero=0;homeReason.Text="From your listening history and liked songs";homeReason.Visible=true;layoutHome();AnimateCards();home.Invalidate(true);if(discoverLoaded&&!discoverBody.IsDisposed)LoadDiscoveryGallery(discoverBody);
        }catch(OperationCanceledException){}catch(Exception ex){System.Diagnostics.Debug.WriteLine("Personal mix: "+ex.Message);}finally{if(radioRequest==req)radioRequest=null;req.Dispose();}
    }
    void AddFeatureSettings(ContextMenuStrip menu){
        menu.Items.Add(featuresState.Autoplay?"Autoplay similar songs: on":"Autoplay similar songs: off",null,delegate{featuresState.Autoplay=!featuresState.Autoplay;SaveFeatures();});
        var fades=new ToolStripMenuItem("Crossfade");foreach(int seconds in new[]{0,2,4,6,8}){int value=seconds;var item=new ToolStripMenuItem(seconds==0?"Off":seconds+" seconds"){Checked=featuresState.Crossfade==seconds};item.Click+=delegate{featuresState.Crossfade=value;SaveFeatures();};fades.DropDownItems.Add(item);}menu.Items.Add(fades);
        menu.Items.Add("Refresh personal mix",null,delegate{RefreshPersonalMix();});
        menu.Items.Add("Reset hidden recommendations",null,delegate{featuresState.Hidden.Clear();SaveFeatures();});
        menu.Items.Add(featuresState.MinimizeToTray?"Minimize to tray: on":"Minimize to tray: off",null,delegate{featuresState.MinimizeToTray=!featuresState.MinimizeToTray;SaveFeatures();});
        menu.Items.Add("Offline storage…",null,delegate{ShowOfflineStorage();});
    }
}

