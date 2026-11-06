using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
internal partial class Player {
    readonly Surface settingsPage=new Surface(),collectionsHub=new Surface();
    readonly List<Control> hubCards=new List<Control>();Action layoutHub;
    void BuildPolishedPages(){
        help.OwnerDraw=true;help.Popup+=delegate(object sender,PopupEventArgs e){using(var font=Theme.Font(9)){var size=TextRenderer.MeasureText(help.GetToolTip(e.AssociatedControl),font,new Size(400,0),TextFormatFlags.WordBreak);e.ToolTipSize=new Size(size.Width+18,size.Height+12);}};
        help.Draw+=delegate(object sender,DrawToolTipEventArgs e){using(var bg=new SolidBrush(Theme.Card))e.Graphics.FillRectangle(bg,e.Bounds);using(var border=new Pen(Theme.Edge))e.Graphics.DrawRectangle(border,0,0,e.Bounds.Width-1,e.Bounds.Height-1);using(var font=Theme.Font(9))TextRenderer.DrawText(e.Graphics,e.ToolTipText,font,new Rectangle(9,6,e.Bounds.Width-18,e.Bounds.Height-12),Theme.Paper,TextFormatFlags.WordBreak);};
        BuildSettingsPage();BuildCollectionsHub();
        foreach(var panel in new[]{settingsPage,collectionsHub}){panel.BackColor=Theme.Ink;panel.AutoScroll=true;stage.Controls.Add(panel);DarkScrollHost.Wrap(panel).Visible=true;panel.Visible=true;}
    }
    Surface PreferenceRow(string title,string description,string icon,Control action){
        var row=new Surface{BackColor=Theme.Ink};settingsPage.Controls.Add(row);row.PaintSurface=delegate(Graphics g,Rectangle r){Fill(g,r,14,Theme.Card);};
        var symbol=new Label{Text=icon,Font=new Font("Segoe MDL2 Assets",17),ForeColor=Theme.Muted,BackColor=Theme.Card,Bounds=new Rectangle(14,23,32,32)};row.Controls.Add(symbol);
        var heading=LabelOn(row,title,12);var note=LabelOn(row,description,9);note.ForeColor=Theme.Muted;note.Font=Theme.Font(9);note.TextAlign=ContentAlignment.TopLeft;heading.BackColor=note.BackColor=Theme.Card;row.Controls.Add(action);
        row.Resize+=delegate{int aw=action is ToggleButton?64:action is Meter?138:124;bool narrow=row.Width<520;Place(heading,58,14,narrow?row.Width-76:row.Width-aw-88,27);Place(note,58,46,narrow?row.Width-76:row.Width-aw-88,43);Place(action,row.Width-aw-15,narrow?96:27,aw,38);};return row;
    }
    void ShowNoctuneAbout(){
        using(var d=DialogUI.Create("About Noctune","A Windows music player by L3B4D.",620,410)){
            DialogUI.Label(d,"Built on YumaPlayer",24,126,570,30,15,true);
            DialogUI.Label(d,"With recognition and thanks to MuwMx, creator of YumaPlayer, whose work provided the foundation for this Windows adaptation. Upstream credits include ArchiveTune, Metrolist and SimpMusic.",24,168,570,84,11);
            DialogUI.Label(d,"Noctune retains the GNU GPL v3 license and applicable third-party notices. Rebranding does not replace the original authors' credits.",24,264,570,55,10);
            var upstream=new SoftButton("Yuma project",delegate{System.Diagnostics.Process.Start("https://github.com/MuwMx/YumaPlayer");});upstream.SetBounds(24,342,150,40);d.Controls.Add(upstream);
            var license=new SoftButton("View license",delegate{string path=System.IO.Path.Combine(Catalog.Root,"LICENSE.txt");if(System.IO.File.Exists(path))System.Diagnostics.Process.Start(path);});license.SetBounds(188,342,150,40);d.Controls.Add(license);
            var close=new SoftButton("Close",delegate{d.Close();},true);close.SetBounds(470,342,126,40);d.Controls.Add(close);d.CancelButton=close;d.ShowDialog(this);
        }
    }
    void BuildSettingsPage(){
        var title=LabelOn(settingsPage,"Settings",25);var subtitle=LabelOn(settingsPage,"Make Noctune feel right for you.",11);subtitle.ForeColor=Theme.Muted;
        var playback=LabelOn(settingsPage,"PLAYBACK",10);var appearance=LabelOn(settingsPage,"APP & APPEARANCE",10);var music=LabelOn(settingsPage,"YOUR MUSIC",10);
        var rows=new List<Surface>();
        rows.Add(PreferenceRow("About Noctune","Credits, open-source licenses and the story behind this Windows player.","\uE946",new SoftButton("About",ShowNoctuneAbout)));
        rows.Add(PreferenceRow("Playback & reliability","Retry playback, choose the audio engine and see keyboard shortcuts.","\uE713",new SoftButton("Open",ShowReliability)));
        rows.Add(PreferenceRow("Backup & restore","Export your library, playlists and preferences without account credentials.","\uE896",new SoftButton("Manage",ShowBackup)));
        rows.Add(PreferenceRow("Keep the music going","Play related songs when your queue ends.","\uE8B1",new ToggleButton("Autoplay",()=>featuresState.Autoplay,v=>{featuresState.Autoplay=v;SaveFeatures();})));
        var fades=new ChoiceButton{Font=Theme.Font(10)};fades.Items.AddRange(new object[]{"Off","2 seconds","4 seconds","6 seconds","8 seconds"});fades.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{0,2,4,6,8},featuresState.Crossfade));fades.SelectedIndexChanged+=delegate{featuresState.Crossfade=new[]{0,2,4,6,8}[fades.SelectedIndex];SaveFeatures();};
        rows.Add(PreferenceRow("Crossfade","Blend the end of one song into the next.","\uE8EE",fades));
        rows.Add(PreferenceRow("Equalizer","Shape your sound with ten frequency bands.","\uE9E9",new SoftButton("Adjust",ShowEqualizer)));
        rows.Add(PreferenceRow("R128 loudness normalization","K-weighted, gated loudness targeting −23 LUFS. Applies from the next song.","\uE767",new ToggleButton("Normalize volume",()=>featuresState.NormalizeVolume,v=>{featuresState.NormalizeVolume=v;ResetTransition();SaveFeatures();})));
        rows.Add(PreferenceRow("Gapless preparation","Preload the next song for a shorter transition when supported.","\uE8EE",new ToggleButton("Gapless preparation",()=>featuresState.Gapless!=false,v=>{featuresState.Gapless=v;ResetTransition();SaveFeatures();})));
        var loudness=new Meter{Maximum=100,Value=volume.Value};loudness.ValueChanged+=delegate{volume.Value=loudness.Value;};volume.ValueChanged+=delegate{if(!loudness.IsDisposed)loudness.Value=volume.Value;};rows.Add(PreferenceRow("Volume","Adjust playback volume.","\uE767",loudness));
        rows.Add(PreferenceRow("Fluid player","Composited glass, artwork gestures and synchronized lyrics.","\uE7F4",new ToggleButton("Fluid player",()=>featuresState.GpuPlayer!=false,v=>{featuresState.GpuPlayer=v;SaveFeatures();WindowLayout();})));
        rows.Add(PreferenceRow("Video backdrops","Use matching artwork-provider loops when available. Reduced motion pauses video.","\uE714",new ToggleButton("Video backdrops",()=>featuresState.CanvasVideo,v=>{featuresState.CanvasVideo=v;SaveFeatures();RefreshCanvas(Current);})));
        rows.Add(PreferenceRow("Your video backdrop","Choose a local MP4 or WebM loop for the current song.","\uE8B7",new SoftButton("Choose",ImportCanvas)));
        rows.Add(PreferenceRow("Pitch & audio engine","Independent pitch control and live loudness readout.","\uE9E9",new SoftButton("Adjust",ShowPrecisionAudio)));
        rows.Add(PreferenceRow("Animations","Gentle transitions and moving playback progress.","\uE7F4",new ToggleButton("Animations",()=>!reduceMotion,v=>{reduceMotion=!v;if(reduceMotion){Motion.FinishAll();revealTimer.Stop();cardReveal=1;PaintPicks();}SaveLibrary();})));
        rows.Add(PreferenceRow("Minimize to tray","Keep Noctune in the notification area when minimized.","\uE921",new ToggleButton("Minimize to tray",()=>featuresState.MinimizeToTray,v=>{featuresState.MinimizeToTray=v;SaveFeatures();})));
        rows.Add(PreferenceRow("Floating player","A movable glass player inside Noctune. Right-click it to reset its position.","\uE740",new SoftButton("Open",ShowMiniPlayer)));
        rows.Add(PreferenceRow("Offline storage","Review downloads, manage space, or cancel transfers.","\uE896",new SoftButton("Manage",ShowOfflineStorage)));
        rows.Add(PreferenceRow("Personal mix","Explore your mix, followed artists, and their releases.","\uE8D6",new SoftButton("Explore",ShowDiscoverMore)));
        rows.Add(PreferenceRow("Recommendation feedback","Let hidden songs appear in recommendations again.","\uE777",new SoftButton("Reset",delegate{featuresState.Hidden.Clear();featuresState.BlockedArtists.Clear();filteredDefaults=null;recommended=null;recommendationSeed=null;SaveFeatures();DialogUI.Show(this,"Hidden songs and artists can appear in recommendations again.","Feedback reset");})));
        rows.Add(PreferenceRow("Optional lyric translation","Connect an installed local model. No automatic downloads or API keys.","\uE8D2",new SoftButton("Configure",ShowTranslationSettings)));
        rows.Add(PreferenceRow("Spotify catalog","Sign in to browse your mixes, albums and playlists. No developer key.","\uE8D6",new SoftButton("Open",OpenSpotify)));
        rows.Add(PreferenceRow("Qobuz account","Connect your account for Qobuz song streaming.","\uE77B",new SoftButton("Connect",delegate{var session=Qobuz.Connect(this);if(session!=null){qobuz=session;SetSource(true);ShowPage("Search");}})));
        rows.Add(PreferenceRow("Local music","Add music files from your computer to the queue.","\uE8B7",new SoftButton("Open files",OpenFiles)));
        rows.Add(PreferenceRow("Smart automix","Analyze decoded audio for tempo and cues. Low-confidence passages retain your crossfade setting.","\uE8EE",new ToggleButton("Smart automix",()=>featuresState.SmartAutomix,v=>{featuresState.SmartAutomix=v;ResetTransition();SaveFeatures();})));
        rows.Add(PreferenceRow("Bass-swap crossfade","Exchange low frequencies through the middle of a crossfade.","\uE9E9",new ToggleButton("Bass-swap crossfade",()=>featuresState.BassSwap,v=>{featuresState.BassSwap=v;ResetTransition();SaveFeatures();})));rows.Add(PreferenceRow("Lossless fallback","Find the same title and artist on YouTube Music if Qobuz is unavailable.","\uE8EE",new ToggleButton("Lossless fallback",()=>featuresState.LosslessFallback,v=>{featuresState.LosslessFallback=v;SaveFeatures();})));rows.Add(PreferenceRow("Floating navigation","Drag the dock by its top grip. It hides briefly while scrolling.","\uE8A7",new ToggleButton("Floating navigation",()=>featuresState.FloatingNavigation,v=>{featuresState.FloatingNavigation=v;LayoutFloatingNavigation();SaveFeatures();})));rows.Add(PreferenceRow("Playback tools","Speed, sleep timer, SponsorBlock, and upcoming-track downloads.","\uE713",new SoftButton("Open",ShowAdvancedPlayback)));settingsPage.Resize+=delegate{int width=Math.Min(900,settingsPage.ClientSize.Width-32),x=Math.Max(16,(settingsPage.ClientSize.Width-width)/2);Place(title,x,14,width,42);Place(subtitle,x,60,width,27);int y=110;Place(playback,x,y,width,25);y+=34;
            for(int i=0;i<rows.Count;i++){if(i==6){y+=15;Place(appearance,x,y,width,25);y+=34;}if(i==9){y+=15;Place(music,x,y,width,25);y+=34;}int rh=width<520?146:100;Place(rows[i],x,y,width,rh);y+=rh+12;}settingsPage.AutoScrollMinSize=new Size(0,y+10);};
    }
    void BuildCollectionsHub(){
        var title=LabelOn(collectionsHub,"Your collections",25);var subtitle=LabelOn(collectionsHub,"Your favorites, organized your way.",11);subtitle.ForeColor=Theme.Muted;
        var create=Pill(collectionsHub,"+  New playlist",delegate{var p=NewPlaylist();if(p!=null){RefreshCollectionsHub();OpenCollection(PlaylistTrack(p));}},true);
        var heading=LabelOn(collectionsHub,"Saved collections & playlists",18);var empty=Make(collectionsHub,"Create your first playlist",delegate{var p=NewPlaylist();if(p!=null)OpenCollection(PlaylistTrack(p));},delegate(Graphics g,Rectangle r){Fill(g,r,16,Theme.Card);Glyph(g,"\uE710",new Rectangle(20,28,40,40),Theme.Peach,24);TextAt(g,"Create your first playlist",new Rectangle(80,23,r.Width-96,34),16,Theme.Paper,true);TextAt(g,"A home for every mood. Start collecting your favorite songs.",new Rectangle(80,66,r.Width-96,52),10,Theme.Muted,false,false,true);});
        var shortcuts=new ArtButton[3];string[] names={"Liked songs","Offline music","Most played"};string[] icons={"\uEB52","\uE896","\uE9D9"};
        for(int i=0;i<3;i++){int k=i;shortcuts[i]=Make(collectionsHub,names[k],delegate{listMode=k==0?"Liked songs":k==1?"Offline":"Most played";ShowPage("Library");},delegate(Graphics g,Rectangle r){Fill(g,r,14,Theme.Card);Glyph(g,icons[k],new Rectangle(15,14,31,31),Theme.Peach,21);TextAt(g,names[k],new Rectangle(15,53,r.Width-30,25),13,Theme.Paper,true);string count=k==0?liked.Count+" saved":k==1?offline.Count(x=>System.IO.File.Exists(x.Path))+" downloaded":"Your listening favorites";TextAt(g,count,new Rectangle(15,82,r.Width-30,22),9,Theme.Muted);});}
        layoutHub=delegate{int w=collectionsHub.ClientSize.Width,m=16;Place(title,m,14,w-32,42);Place(subtitle,m,61,w-32,25);Place(create,m,100,150,36);int shortcutCols=w<540?1:3,shortcutRows=3/shortcutCols,sw=(w-2*m-12*(shortcutCols-1))/shortcutCols;
            for(int i=0;i<3;i++)Place(shortcuts[i],m+(i%shortcutCols)*(sw+12),155+(i/shortcutCols)*130,sw,118);
            int section=155+shortcutRows*130+12;Place(heading,m,section,w-32,32);int gridY=section+49;
            int cols=Math.Max(1,(w-32)/200),gap=16,cw=(w-32-gap*(cols-1))/cols;for(int i=0;i<hubCards.Count;i++)Place(hubCards[i],m+(i%cols)*(cw+gap),gridY+(i/cols)*232,cw,218);empty.Visible=hubCards.Count==0;Place(empty,m,gridY,w-32,140);collectionsHub.AutoScrollMinSize=new Size(0,hubCards.Count==0?gridY+160:gridY+10+((hubCards.Count+cols-1)/cols)*232);};collectionsHub.Resize+=delegate{layoutHub();};
    }
    void RefreshCollectionsHub(){
        foreach(var c in hubCards){collectionsHub.Controls.Remove(c);c.Dispose();}hubCards.Clear();
        foreach(var playlist in featuresState.Playlists){var p=playlist;var t=PlaylistTrack(p);ArtButton tile=null;tile=Make(collectionsHub,p.Name,delegate{OpenCollection(PlaylistTrack(p));},delegate(Graphics g,Rectangle r){int d=Math.Min(160,r.Width-12),x=(r.Width-d)/2;var cover=new Rectangle(x,6-(int)(tile.HoverAmount*4),d,d);Artwork(g,Cover(t),cover,14);HoverFinish(g,cover,14,tile.HoverAmount,false);TextAt(g,p.Name,new Rectangle(6,d+15,r.Width-12,25),12,Theme.Paper,true);TextAt(g,p.Songs.Count+" songs",new Rectangle(6,d+43,r.Width-12,22),10,Theme.Muted);});tile.AnimateHover=true;hubCards.Add(tile);}
        foreach(var album in featuresState.SavedAlbums){var t=album;FetchArtwork(t);ArtButton tile=null;tile=Make(collectionsHub,t.Title,delegate{OpenCollection(t);},delegate(Graphics g,Rectangle r){int d=Math.Min(160,r.Width-12);Artwork(g,Cover(t),new Rectangle((r.Width-d)/2,6,d,d),14);TextAt(g,t.Title,new Rectangle(6,d+15,r.Width-12,25),12,Theme.Paper,true);TextAt(g,(t.Kind??"Collection")+" · "+t.Artist,new Rectangle(6,d+43,r.Width-12,22),10,Theme.Muted);});tile.AnimateHover=true;hubCards.Add(tile);}layoutHub();
    }
}
