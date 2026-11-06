using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal partial class Player {
    readonly TextBox libraryQuery=new TextBox(),collectionQuery=new TextBox();
    readonly ChoiceButton libraryFilter=new ChoiceButton(),librarySort=new ChoiceButton(),artistSection=new ChoiceButton();
    List<Track> collectionContents=new List<Track>();
    Surface librarySearch,collectionSearch;ArtButton artistFollow,collectionRetry,duplicatesButton,collectionRemove;
    Label artistDescription;bool renderingLibrary;
    Surface SearchBox(Control parent,TextBox input,string placeholder){
        var box=new Surface{BackColor=Theme.Ink};parent.Controls.Add(box);
        box.PaintSurface=delegate(Graphics g,Rectangle r){Surface.Background(box,g);Fill(g,r,11,Theme.Card);using(var p=Theme.Round(new Rectangle(0,0,r.Width-1,r.Height-1),11))using(var pen=new Pen(box.ContainsFocus?Theme.Peach:Theme.Edge))g.DrawPath(pen,p);Glyph(g,"\uE721",new Rectangle(10,5,25,r.Height-10),Theme.Muted,12);};
        input.BorderStyle=BorderStyle.None;input.BackColor=Theme.Card;input.ForeColor=Theme.Paper;input.Font=Theme.Font(10);input.AccessibleName=placeholder;box.Controls.Add(input);
        input.HandleCreated+=delegate{SendMessage(input.Handle,0x1501,new IntPtr(1),placeholder);};input.GotFocus+=delegate{box.Invalidate();};input.LostFocus+=delegate{box.Invalidate();};
        box.Resize+=delegate{input.SetBounds(40,Math.Max(5,(box.Height-23)/2),Math.Max(1,box.Width-51),23);};return box;
    }
    void BuildEnhancements(){
        BuildDiscoverPage();
        InstallLoadingStates();
        librarySearch=SearchBox(library,libraryQuery,"Filter songs, artists or albums");
        libraryFilter.Items.AddRange(new object[]{"All music","Liked","Downloaded","Artists","Albums"});libraryFilter.SelectedIndex=0;library.Controls.Add(libraryFilter);
        librarySort.Items.AddRange(new object[]{"Recently added","Most played","Title A–Z","Artist A–Z","Album A–Z"});librarySort.SelectedIndex=0;library.Controls.Add(librarySort);
        libraryQuery.TextChanged+=delegate{FillLibrary();};libraryFilter.SelectedIndexChanged+=delegate{FillLibrary();};librarySort.SelectedIndexChanged+=delegate{FillLibrary();};
        library.Resize+=delegate{int w=library.Width;Place(librarySearch,16,145,w-32,42);int cw=(w-44)/2;Place(libraryFilter,16,197,cw,34);Place(librarySort,28+cw,197,cw,34);Place(libraryRows,10,244,w-20,library.Height-254);};
        collectionSearch=SearchBox(collectionPage,collectionQuery,"Search this collection");collectionQuery.TextChanged+=delegate{RenderCollection();};
        artistSection.Items.AddRange(new object[]{"Top songs","Albums","Singles & EPs","Related artists","All"});artistSection.SelectedIndex=0;collectionPage.Controls.Add(artistSection);artistSection.SelectedIndexChanged+=delegate{RenderCollection();};
        artistFollow=Pill(collectionPage,"Follow artist",ToggleFollow,true);artistDescription=LabelOn(collectionPage,"",10);artistDescription.ForeColor=Theme.Muted;artistDescription.AutoEllipsis=true;
        collectionRetry=Pill(collectionPage,"Retry",delegate{if(displayedCollection!=null)OpenCollection(displayedCollection);});collectionRetry.Visible=false;
        duplicatesButton=Pill(collectionPage,"Remove duplicates",RemoveDuplicates);collectionRemove=Pill(collectionPage,"Remove selected",RemoveCollectionSelection);
        collectionRows.MultiSelect=true;collectionRows.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Delete&&editingPlaylist!=null){RemoveCollectionSelection();e.SuppressKeyPress=true;}};
        collectionPage.Resize+=delegate{LayoutCollectionExtras();};
        queueView.MultiSelect=true;var remove=Pill(queuePage,"Remove selected",RemoveQueueSelection);var up=Pill(queuePage,"Move up",delegate{NudgeQueue(-1);});var down=Pill(queuePage,"Move down",delegate{NudgeQueue(1);});
        queueView.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Delete){RemoveQueueSelection();e.SuppressKeyPress=true;}else if(e.Alt&&(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)){NudgeQueue(e.KeyCode==Keys.Up?-1:1);e.SuppressKeyPress=true;}};
        queuePage.Resize+=delegate{int y=Wide?59:112;Place(remove,16,y,130,34);Place(up,154,y,67,34);Place(down,229,y,85,34);Place(queueEmpty,18,y+48,queuePage.Width-36,45);Place(queueView,10,y+48,queuePage.Width-20,queuePage.Height-y-58);};
        // A compact, deliberately designed retry surface beside search status.
        var retry=Pill(searchPage,"Retry",RunSearch);retry.Visible=false;searchStatus.TextChanged+=delegate{retry.Visible=searching==null?false:searchStatus.Text!="Searching…"&&!searchStatus.Text.Contains("results for")&&!searchStatus.Text.StartsWith("No matches");};
        searchPage.Resize+=delegate{Place(retry,searchPage.Width-99,145,80,28);};
        var more=Pill(home,"Discover more",ShowDiscoverMore,true);Action layoutMore=delegate{Place(more,Math.Max(14,home.Width-164),Wide?87:68,148,34);more.Visible=true;more.BringToFront();};home.Resize+=delegate{layoutMore();};home.VisibleChanged+=delegate{if(home.Visible)layoutMore();};layoutMore();
    }
    IEnumerable<Track> LibraryData(){
        if(listMode=="Playlists")return featuresState.Playlists.Select(PlaylistTrack);
        if(listMode=="Offline")return OfflineTracks();
        if(listMode=="Liked songs")return liked.AsEnumerable().Reverse();
        return recent.Concat(liked.AsEnumerable().Reverse()).GroupBy(Key).Select(g=>g.First());
    }
    void RenderLibrary(){
        if(renderingLibrary||libraryFilter.Items.Count==0)return;renderingLibrary=true;
        try{IEnumerable<Track> data=LibraryData();int filter=libraryFilter.SelectedIndex;string term=libraryQuery.Text.Trim();
            if(filter==1)data=data.Where(t=>liked.Any(x=>Key(x)==Key(t)));
            if(filter==2)data=data.Where(t=>OfflinePath(t)!=null||!string.IsNullOrEmpty(t.File)&&System.IO.File.Exists(t.File));
            if(filter==3)data=data.Where(t=>!string.IsNullOrEmpty(t.ArtistId)).GroupBy(t=>t.ArtistId).Select(g=>new Track{Kind="Artist",BrowseId=g.Key,Title=g.First().Artist,Artist="Artist",Artwork=g.First().Artwork}).Concat(featuresState.FollowedArtists).GroupBy(Key).Select(g=>g.First());
            if(filter==4)data=data.Where(t=>!string.IsNullOrEmpty(t.AlbumId)).GroupBy(t=>t.AlbumId).Select(g=>new Track{Kind="Album",BrowseId=g.Key,Title=g.First().Album,Artist=g.First().Artist,Artwork=g.First().Artwork});
            if(term.Length>0)data=data.Where(t=>Matches(t,term));
            switch(librarySort.SelectedIndex){case 0:data=data.OrderByDescending(t=>t.AddedAtUtc);break;case 1:data=data.OrderByDescending(Score);break;case 2:data=data.OrderBy(t=>t.Title);break;case 3:data=data.OrderBy(t=>t.Artist).ThenBy(t=>t.Title);break;case 4:data=data.OrderBy(t=>t.Album).ThenBy(t=>t.Title);break;}
            if(listMode=="Most played")data=data.OrderByDescending(Score);
            var rows=data.ToList();libraryRows.BeginUpdate();try{libraryRows.Items.Clear();foreach(var t in rows)libraryRows.Items.Add(Row(t));}finally{libraryRows.EndUpdate();}libraryTitle.Text=listMode=="Offline"?(rows.Count==0?"No downloads yet · Right-click a song → Save offline":rows.Count+" downloaded songs · Available offline"):rows.Count+" items · "+listMode;
        }finally{renderingLibrary=false;}
    }
    void MarkLibraryAdded(Track t){if(t.AddedAtUtc!=DateTime.MinValue)return;var existing=recent.Concat(liked).FirstOrDefault(x=>Key(x)==Key(t)&&x.AddedAtUtc!=DateTime.MinValue);t.AddedAtUtc=existing==null?DateTime.UtcNow:existing.AddedAtUtc;}
    static bool Matches(Track t,string text){return ((t.Title??"")+" "+(t.Artist??"")+" "+(t.Album??"")).IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0;}
    void LayoutCollectionExtras(){
        LayoutRefinedCollection();
    }

    void RenderCollection(){
        if(collectionSearch==null)return;IEnumerable<Track> rows=collectionContents;bool artist=displayedCollection!=null&&displayedCollection.Kind=="Artist";
        if(artist){switch(artistSection.SelectedIndex){case 0:rows=rows.Where(t=>string.IsNullOrEmpty(t.Kind)&&t.Section!="Videos"&&t.Section!="Live performances");break;case 1:rows=rows.Where(t=>t.Kind=="Album"&&!IsSingle(t));break;case 2:rows=rows.Where(IsSingle);break;case 3:rows=rows.Where(t=>t.Kind=="Artist");break;}}
        string term=collectionQuery.Text.Trim();if(term.Length>0)rows=rows.Where(t=>Matches(t,term));var view=rows.ToList();
        foreach(Control action in collectionPage.Controls)if(action.Text=="Play all"||action.Text=="Save as playlist")action.Enabled=view.Any(t=>string.IsNullOrEmpty(t.Kind));
        collectionRows.BeginUpdate();try{collectionRows.Items.Clear();foreach(var t in view)collectionRows.Items.Add(Row(t));}finally{collectionRows.EndUpdate();}RefreshCollectionGallery();
        int known=view.Where(t=>string.IsNullOrEmpty(t.Kind)).Sum(t=>Math.Max(0,t.DurationSeconds));int unknown=view.Count(t=>string.IsNullOrEmpty(t.Kind)&&t.DurationSeconds<=0);
        collectionNote.Text=view.Count+" items"+(known>0?" · "+Stamp(known*1000L)+(unknown>0?" + unknown durations":" total"):unknown>0?" · Duration unavailable":"")+(view.Count==0?" · No matches in this section":"");
        duplicatesButton.Text="Duplicates ("+(editingPlaylist==null?0:editingPlaylist.Songs.Count-editingPlaylist.Songs.GroupBy(Key).Count())+")";LayoutCollectionExtras();
        foreach(var t in view.Take(10))FetchArtwork(t);
    }
    static bool IsSingle(Track t){return t.Kind=="Album"&&((t.Section??"").IndexOf("single",StringComparison.OrdinalIgnoreCase)>=0||(t.Section??"").IndexOf("EP",StringComparison.OrdinalIgnoreCase)>=0||(t.Artist??"").StartsWith("Single",StringComparison.OrdinalIgnoreCase));}
    void ToggleFollow(){if(displayedCollection==null||displayedCollection.Kind!="Artist")return;var artist=displayedCollection;if(featuresState.FollowedArtists.Any(t=>t.BrowseId==artist.BrowseId))featuresState.FollowedArtists.RemoveAll(t=>t.BrowseId==artist.BrowseId);else featuresState.FollowedArtists.Add(artist);SaveFeatures();discoverLoaded=false;UpdateFollowButton();}
    void UpdateFollowButton(){if(artistFollow!=null)artistFollow.Text=displayedCollection!=null&&featuresState.FollowedArtists.Any(t=>t.BrowseId==displayedCollection.BrowseId)?"Following ✓":"Follow artist";}
    void AddToPlaylist(SavedPlaylist p,Track t){if(p.Songs.Any(x=>Key(x)==Key(t))){if(DialogUI.Show(this,"This song is already in “"+p.Name+"”. Add another copy?","Duplicate song",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;}p.Songs.Add(t);SaveFeatures();}
    void RemoveDuplicates(){if(editingPlaylist==null)return;int before=editingPlaylist.Songs.Count;editingPlaylist.Songs=editingPlaylist.Songs.GroupBy(Key).Select(g=>g.First()).ToList();SaveFeatures();FillCollection(editingPlaylist.Songs);collectionNote.Text+=(" · Removed "+(before-editingPlaylist.Songs.Count)+" duplicates");}
    void RemoveCollectionSelection(){if(editingPlaylist==null)return;foreach(ListViewItem row in collectionRows.SelectedItems)editingPlaylist.Songs.Remove((Track)row.Tag);SaveFeatures();FillCollection(editingPlaylist.Songs);}
    void EnableCollectionReorder(){
        collectionRows.ItemDrag+=delegate(object s,ItemDragEventArgs e){if(editingPlaylist!=null&&collectionQuery.Text.Length==0)collectionRows.DoDragDrop(e.Item,DragDropEffects.Move);};
        collectionRows.DragOver+=delegate(object s,DragEventArgs e){e.Effect=editingPlaylist!=null&&collectionQuery.Text.Length==0&&e.Data.GetDataPresent(typeof(ListViewItem))?DragDropEffects.Move:DragDropEffects.None;};
        collectionRows.DragDrop+=delegate(object s,DragEventArgs e){var item=e.Data.GetData(typeof(ListViewItem)) as ListViewItem;if(editingPlaylist==null||collectionQuery.Text.Length>0||item==null||item.ListView!=collectionRows)return;var pt=collectionRows.PointToClient(new Point(e.X,e.Y));var target=collectionRows.GetItemAt(pt.X,pt.Y);int to=target==null?editingPlaylist.Songs.Count:target.Index;var selected=collectionRows.SelectedIndices.Cast<int>().ToArray();Reorder(editingPlaylist.Songs,selected,to);SaveFeatures();FillCollection(editingPlaylist.Songs);};
    }
    static int[] MoveOrder(int count,int[] selected,int destination){var picked=new HashSet<int>(selected.Where(i=>i>=0&&i<count));var order=Enumerable.Range(0,count).Where(i=>!picked.Contains(i)).ToList();int insert=Math.Max(0,Math.Min(order.Count,destination-picked.Count(i=>i<destination)));order.InsertRange(insert,picked.OrderBy(i=>i));return order.ToArray();}
    static void Reorder(List<Track> tracks,int[] selected,int destination){var original=tracks.ToArray();var order=MoveOrder(original.Length,selected,destination);tracks.Clear();tracks.AddRange(order.Select(i=>original[i]));}
    void MoveQueue(int[] selected,int destination){if(selected.Length==0)return;var order=MoveOrder(queue.Count,selected,destination);var original=queue.ToArray();int old=index;ResetTransition();ClearPrime();queue.Clear();queue.AddRange(order.Select(i=>original[i]));index=Array.IndexOf(order,old);RebuildQueue();for(int i=0;i<order.Length;i++)if(selected.Contains(order[i]))queueView.Items[i].Selected=true;SaveFeatures();}
    void RebuildQueue(){queueView.BeginUpdate();try{queueView.Items.Clear();foreach(var t in queue)queueView.Items.Add(Row(t));}finally{queueView.EndUpdate();}queueTitle.Text="Queue · "+queue.Count;queueEmpty.Visible=queue.Count==0;ScrollVisible(queueView,queue.Count>0);RefreshSurfaces();}
    void NudgeQueue(int direction){var selected=queueView.SelectedIndices.Cast<int>().ToArray();if(selected.Length==0)return;MoveQueue(selected,direction<0?Math.Max(0,selected.Min()-1):Math.Min(queue.Count,selected.Max()+2));}
    void RemoveQueueSelection(){var selected=queueView.SelectedIndices.Cast<int>().ToArray();if(selected.Length==0)return;bool removedCurrent=selected.Contains(index);int next=index<0?-1:index-selected.Count(i=>i<index);bool resume=active&&audio.State==3;ResetTransition();ClearPrime();if(removedCurrent){if(starting!=null)starting.Cancel();audio.Stop();active=false;}for(int i=selected.Length-1;i>=0;i--)queue.RemoveAt(selected[i]);index=queue.Count==0?-1:Math.Min(next,queue.Count-1);RebuildQueue();if(removedCurrent){restoredPosition=lastPosition=0;now.Text=index>=0?Current.Title:"Nothing playing";status.Text=index>=0?"Ready to play · "+Current.Artist:"Choose a song";if(resume&&index>=0)Play(index);}SaveFeatures();}
    void EnableQueueReorder(){queueView.AllowDrop=true;queueView.ItemDrag+=delegate(object s,ItemDragEventArgs e){queueView.DoDragDrop(e.Item,DragDropEffects.Move);};queueView.DragOver+=delegate(object s,DragEventArgs e){e.Effect=e.Data.GetDataPresent(typeof(ListViewItem))?DragDropEffects.Move:DragDropEffects.None;};queueView.DragDrop+=delegate(object s,DragEventArgs e){var item=e.Data.GetData(typeof(ListViewItem)) as ListViewItem;if(item==null||item.ListView!=queueView)return;var pt=queueView.PointToClient(new Point(e.X,e.Y));var target=queueView.GetItemAt(pt.X,pt.Y);MoveQueue(queueView.SelectedIndices.Cast<int>().ToArray(),target==null?queue.Count:target.Index);};}
    void DrawRowArtwork(Graphics g,Track track,Rectangle rect){var image=Cover(track);if(image!=images["logo"]){Artwork(g,image,rect,8);return;}Fill(g,rect,8,Theme.Edge);Glyph(g,loading.Contains(Key(track)??"")?"\uE895":"\uE8D6",rect,Theme.Muted,17);}
}

internal partial class Player {
    readonly Surface discoverPage=new Surface{BackColor=Theme.Ink};
    readonly Surface discoverBody=new Surface{BackColor=Theme.Ink,AutoScroll=true};
    bool discoverLoaded;
    void BuildDiscoverPage(){
        stage.Controls.Add(discoverPage);discoverPage.Controls.Add(discoverBody);
        var frame=DarkScrollHost.Wrap(discoverBody);frame.Visible=true;discoverBody.Visible=true;
        var title=LabelOn(discoverPage,"Made for you",25);
        var subtitle=LabelOn(discoverPage,"Fresh picks, favorite artists, and releases worth exploring.",10);subtitle.ForeColor=Theme.Muted;
        var back=Pill(discoverPage,"‹  Home",delegate{ShowPage("Home");});var charts=Pill(discoverPage,"Charts",delegate{OpenCollection(new Track{Kind="Charts",Title="YouTube Music charts",Artist="YouTube Music",BrowseId="FEmusic_charts",BrowseParams="ggMGCgQIgAQ%3D"});});
        var refresh=Pill(discoverPage,"Refresh",delegate{RefreshPersonalMix();LoadDiscoveryGallery(discoverBody);},true);
        discoverPage.Resize+=delegate{int w=discoverPage.Width;Place(title,18,12,w-36,43);Place(subtitle,18,59,w-36,39);Place(back,18,108,95,34);Place(charts,123,108,86,34);charts.Visible=w>=360;Place(refresh,Math.Max(123,w-126),108,108,34);frame.SetBounds(18,157,Math.Max(1,w-36),Math.Max(1,discoverPage.Height-171));};
    }
    void ShowDiscoverMore(){
        ShowPage("Discover");if(!discoverLoaded){discoverLoaded=true;LoadDiscoveryGallery(discoverBody);}
    }
    int galleryVersion;
    async void LoadDiscoveryGallery(Surface body){
        int version=++galleryVersion;body.AutoScrollPosition=Point.Empty;var scroll=SmoothScroll.Find(body);if(scroll!=null)scroll.Jump(0);foreach(Control c in body.Controls.Cast<Control>().ToArray())c.Dispose();
        Label note=null;var groups=new List<Tuple<Label,List<ArtButton>>>();
        Action<string,IEnumerable<Track>> add=delegate(string title,IEnumerable<Track> tracks){var heading=LabelOn(body,title,17);var cards=new List<ArtButton>();foreach(var track in tracks.Take(12)){var t=track;ArtButton card=null;card=Make(body,t.Title,delegate{if(string.IsNullOrEmpty(t.Kind))Add(t,true);else OpenCollection(t);},delegate(Graphics g,Rectangle r){DrawAlbumTile(g,r,t,card.HoverAmount);});card.AnimateHover=true;cards.Add(card);FetchArtwork(t);}groups.Add(Tuple.Create(heading,cards));};
        Action layout=delegate{if(body.IsDisposed)return;int w=body.ClientSize.Width,cols=Math.Max(2,w/185),slot=(w-16*(cols-1))/cols,y=8;foreach(var group in groups){Place(group.Item1,0,y,w,33);y+=46;for(int i=0;i<group.Item2.Count;i++)Place(group.Item2[i],i%cols*(slot+16),y+i/cols*(Math.Min(slot,180)+63),slot,Math.Min(slot,180)+55);y+=Math.Max(0,(group.Item2.Count+cols-1)/cols)*(Math.Min(slot,180)+63)+20;}if(note!=null&&!note.IsDisposed){Place(note,0,y,w,50);y+=60;}body.AutoScrollMinSize=new Size(0,y);body.Invalidate(true);};
        EventHandler oldResize=body.Tag as EventHandler;if(oldResize!=null)body.Resize-=oldResize;EventHandler resize=delegate{layout();};body.Tag=resize;body.Resize+=resize;
        add(!string.IsNullOrEmpty(homeReason.Text)?homeReason.Text:"Your personal mix",HomePicks);add("Artists you follow",featuresState.FollowedArtists);layout();
        note=LabelOn(body,featuresState.FollowedArtists.Count==0?"Follow an artist from their page to see their releases here.":"Finding releases from artists you follow…",11);note.ForeColor=Theme.Muted;note.SetBounds(0,body.AutoScrollMinSize.Height,Math.Max(100,body.Width),50);body.AutoScrollMinSize=new Size(0,body.AutoScrollMinSize.Height+60);layout();
        var request=new CancellationTokenSource(TimeSpan.FromSeconds(35));EventHandler stop=delegate{request.Cancel();};body.Disposed+=stop;
        try{
            var artists=featuresState.FollowedArtists.ToArray();var jobs=artists.Select(t=>SafeArtistReleases(t,request.Token)).ToArray();if(jobs.Length==0)return;
            var profiles=await Task.WhenAll(jobs);if(closing||body.IsDisposed||version!=galleryVersion)return;
            var releases=profiles.SelectMany(p=>p).Where(t=>t.Kind=="Album").GroupBy(Key).Select(g=>g.First()).OrderByDescending(t=>t.Year).Take(12).ToList();note.Dispose();add("Releases from artists you follow",releases);layout();if(releases.Count==0){var empty=LabelOn(body,"No releases returned. Use Refresh to try again.",11);empty.SetBounds(0,body.AutoScrollMinSize.Height-130,body.Width,35);}
        }catch(OperationCanceledException){if(!body.IsDisposed&&version==galleryVersion&&!note.IsDisposed)note.Text="Releases took too long. Use Refresh to try again.";}catch(Exception ex){if(!body.IsDisposed&&version==galleryVersion&&!note.IsDisposed)note.Text="Could not load releases. Use Refresh to try again.";System.Diagnostics.Debug.WriteLine(ex.Message);}finally{body.Disposed-=stop;request.Dispose();}
    }
    readonly System.Threading.SemaphoreSlim releaseSlots=new System.Threading.SemaphoreSlim(3);
    async Task<List<Track>> SafeArtistReleases(Track artist,CancellationToken token){
        await releaseSlots.WaitAsync(token);try{return (await Catalog.Artist(artist.BrowseId,token)).Tracks;}catch(OperationCanceledException){throw;}catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex.Message);return new List<Track>();}finally{releaseSlots.Release();}
    }
    void ShowMiniPlayer(){ShowFloatingPill();}
    void ApplyEqualizer(){audio.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);if(nextAudio!=null)nextAudio.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);if(outgoing!=null)outgoing.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);}
    void ShowEqualizer(){
        using(var f=DialogUI.Create("Shape your sound","Ten-band equalizer · changes apply while you listen.",700,580)){
            var equalizerCards=new Surface[2];for(int col=0;col<2;col++){var card=equalizerCards[col]=new Surface{BackColor=Theme.Ink};f.Controls.Add(card);card.SetBounds(14+col*334,114,324,389);card.PaintSurface=delegate(Graphics g,Rectangle rect){Fill(g,rect,16,Theme.Card);};card.SendToBack();}
            var on=new ToggleButton("Equalizer",()=>featuresState.EqualizerEnabled,v=>{featuresState.EqualizerEnabled=v;ApplyEqualizer();SaveFeatures();});f.Controls.Add(on);Place(on,606,26,64,35);
            var names=new[]{"60 Hz","170 Hz","310 Hz","600 Hz","1 kHz","3 kHz","6 kHz","12 kHz","14 kHz","16 kHz"};var sliders=new Meter[10];var values=new Label[10];
            for(int i=0;i<10;i++){int k=i,col=i/5,row=i%5,x=10,y=14+row*75;var parent=equalizerCards[col];var label=DialogUI.Label(parent,names[i],x,y,70,25,10,true);label.BackColor=Color.Transparent;values[i]=DialogUI.Label(parent,"",x+235,y,64,25,10);values[i].BackColor=Color.Transparent;sliders[i]=new Meter{Minimum=0,Maximum=40,Value=(int)Math.Max(0,Math.Min(40,featuresState.Equalizer[i]+20)),AccessibleName=names[i]+" gain"};parent.Controls.Add(sliders[i]);Place(sliders[i],x,y+28,297,24);Action update=delegate{featuresState.Equalizer[k]=sliders[k].Value-20;values[k].Text=(featuresState.Equalizer[k]>0?"+":"")+featuresState.Equalizer[k]+" dB";ApplyEqualizer();};sliders[i].ValueChanged+=delegate{update();};update();}
            var reset=new SoftButton("Reset flat",delegate{foreach(var slider in sliders)slider.Value=20;});f.Controls.Add(reset);Place(reset,24,522,120,36);
            var close=new SoftButton("Done",delegate{SaveFeatures();f.Close();},true);f.Controls.Add(close);Place(close,552,522,124,36);f.FormClosed+=delegate{SaveFeatures();};f.ShowDialog(this);
        }
    }
}

internal sealed class LoadingVeil:Control {
    readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer{Interval=80};float phase;
    internal LoadingVeil(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Theme.Ink;Visible=false;animation.Tick+=delegate{phase=(phase+.08f)%1;Invalidate();};VisibleChanged+=delegate{if(Visible&&!Player.reduceMotion)animation.Start();else animation.Stop();};}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;int tone=36+(int)(Math.Sin(phase*Math.PI*2)*5);using(var brush=new SolidBrush(Color.FromArgb(tone,tone,tone+4))){for(int i=0;i<Math.Min(7,Height/64);i++){int y=8+i*64;using(var p=Theme.Round(new Rectangle(8,y,46,46),9))e.Graphics.FillPath(brush,p);using(var p=Theme.Round(new Rectangle(68,y+7,Math.Max(10,Math.Min(Width-90,240+i%3*50)),12),6))e.Graphics.FillPath(brush,p);using(var p=Theme.Round(new Rectangle(68,y+29,Math.Max(10,Math.Min(Width-90,145)),9),4))e.Graphics.FillPath(brush,p);}}}
    protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
}
internal partial class Player {
    void InstallLoadingStates(){
        var searchLoading=new LoadingVeil();resultClip.Controls.Add(searchLoading);searchLoading.Dock=DockStyle.Fill;
        searchStatus.TextChanged+=delegate{searchLoading.Visible=searchStatus.Text=="Searching…";if(searchLoading.Visible)searchLoading.BringToFront();};
        var collectionLoading=new LoadingVeil();collectionPage.Controls.Add(collectionLoading);var collectionFrame=DarkScrollHost.Frame(collectionRows);collectionFrame.Resize+=delegate{collectionLoading.Bounds=collectionFrame.Bounds;};collectionFrame.LocationChanged+=delegate{collectionLoading.Bounds=collectionFrame.Bounds;};
        collectionNote.TextChanged+=delegate{collectionLoading.Visible=collectionNote.Text=="Loading…";if(collectionLoading.Visible){collectionLoading.Bounds=DarkScrollHost.Frame(collectionRows).Bounds;collectionLoading.BringToFront();}};
    }
}
