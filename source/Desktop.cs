using System;
using System.Drawing;
using System.Windows.Forms;

internal partial class Player {
    bool Wide { get { return ClientSize.Width>=780; } }
    readonly Panel homeScroll=new Panel();
    readonly Meter barPosition=new Meter();
    readonly Label barTime=new Label();
    ArtButton railBrand,railSettings,railFiles,railQueue,railDiscover,railDownloads,barQueue;
    Action layoutNav,layoutMini;
    bool seekingBar;Motion railSlide;float previousRow,selectedRow;
    float RailRow{get{return previousRow+(selectedRow-previousRow)*(railSlide==null?1:railSlide.Value);}}
    void ShiftRail(){int row=Array.IndexOf(new[]{"Home","Search","Library","Queue","Discover","Downloads"},page=="Library"&&listMode=="Offline"?"Downloads":page=="Collections"?"Library":page=="Collection"?(editingPlaylist!=null?"Library":"Search"):page);if(row<0||row==selectedRow||railSlide==null)return;previousRow=RailRow;selectedRow=row;railSlide.To(1,155,true);}
    void DrawRailPill(Graphics g,Rectangle r,int row){if(page=="Player"||page=="Lyrics"||page=="Settings")return;var pill=r;pill.Y+=(int)Math.Round((RailRow-row)*48);if(pill.IntersectsWith(r))Fill(g,pill,11,blue);}
    void LayoutPages(){
        Control primary=page=="Home"?homeScroll:page=="Search"?searchPage:page=="Library"?library:
            page=="Spotify"?spotifyPage:page=="Discover"?discoverPage:page=="Settings"?settingsPage:page=="Collections"?collectionsHub:page=="Collection"?collectionPage:page=="Queue"?queuePage:page=="Lyrics"?lyricsPage:detail;
        if((UseGpu||gpuLoading)&&(page=="Player"||page=="Lyrics"))primary=gpuPage;primary=DarkScrollHost.Frame(primary);
        bool split=(page=="Player"||page=="Lyrics")&&stage.Width>=1120;
        int side=split?Math.Min(360,stage.Width/4):0;
        foreach(Control c in stage.Controls){
            c.Dock=DockStyle.None;
            c.Visible=c==primary||(split&&c==queuePage);
        }
        Place(primary,0,0,stage.Width-(split?side+18:0),stage.Height);
        if(split)Place(queuePage,stage.Width-side,0,side,stage.Height);
        primary.BringToFront();
    }
    int homeDesiredHeight=740;
    void LayoutHomeViewport(){
        if(homeScroll.Width<=0)return;
        if(Wide){homeScroll.AutoScroll=true;home.Dock=DockStyle.Top;int wanted=homeDesiredHeight;if(home.Height!=wanted)home.Height=wanted;return;}
        int minimumHeight=650;
        bool scroll=homeScroll.Height<minimumHeight;
        homeScroll.AutoScroll=scroll;home.Dock=scroll?DockStyle.Top:DockStyle.Fill;
        if(scroll)home.Height=minimumHeight;
    }
    void BuildDesktopControls(){
        railSlide=new Motion(delegate{if(Wide&&nav.Visible)nav.Invalidate(true);});nav.Disposed+=delegate{railSlide.Dispose();};
        homeScroll.Resize+=delegate{LayoutHomeViewport();};
        homeScroll.VisibleChanged+=delegate{if(homeScroll.Visible)LayoutHomeViewport();};
        railBrand=Make(nav,"Noctune home",delegate{ShowPage("Home");},delegate(Graphics g,Rectangle r){
            g.Clear(Theme.Card);if(r.Width>95)g.DrawImage(images["brand"],new Rectangle(9,5,118,39));
            else g.DrawImage(images["logo"],new Rectangle((r.Width-31)/2,7,31,31));
        });
        railQueue=RailAction("Queue","\uE8FD",delegate{ShowPage("Queue");});
        railDiscover=RailAction("Discover more","\uE774",ShowDiscoverMore);
        railDownloads=RailAction("Downloads","\uE896",ShowDownloadedSongs);
        railFiles=RailAction("Open files","\uE8E5",OpenFiles);
        railSettings=RailAction("Settings","\uE713",Settings);
        barQueue=Make(mini,"Queue",delegate{ShowPage("Queue");},delegate(Graphics g,Rectangle r){Glyph(g,"\uE8FD",r,Theme.Paper,14);});
        mini.Controls.Add(volume);mini.Controls.Add(barPosition);mini.Controls.Add(barTime);
        barPosition.Maximum=1000;barPosition.AccessibleName="Seek";barTime.ForeColor=Theme.Muted;barTime.Font=Theme.Font(9);
        barTime.BackColor=Color.FromArgb(22,22,24);barTime.TextAlign=ContentAlignment.MiddleCenter;
        barPosition.MouseDown+=delegate{seekingBar=true;};barPosition.MouseUp+=delegate{seekingBar=false;SeekBar();};barPosition.KeyUp+=delegate{SeekBar();};
        timer.Tick+=delegate{if(!Wide)return;barTime.Text=clock.Text;barPosition.Enabled=position.Enabled;if(!seekingBar)barPosition.Value=position.Value;};
    }
    void ShowDownloadedSongs(){listMode="Offline";libraryQuery.Text="";libraryFilter.SelectedIndex=0;ShowPage("Library");FillLibrary();}
    void SeekBar(){if(active && audio.Length>0)SeekTo(audio.Length*barPosition.Value/1000);}
    ArtButton RailAction(string name,string icon,Action action){
        return Make(nav,name,action,delegate(Graphics g,Rectangle r){
            bool selected=(name=="Downloads"&&page=="Library"&&listMode=="Offline")||name==page||(name=="Discover more"&&page=="Discover");g.Clear(Theme.Card);if(name=="Queue")DrawRailPill(g,r,3);if(name=="Discover more")DrawRailPill(g,r,4);if(name=="Downloads")DrawRailPill(g,r,5);
            bool labels=nav.Width>100;
            Glyph(g,icon,new Rectangle(labels?9:0,0,labels?32:r.Width,r.Height),selected?Theme.Peach:Theme.Muted,14);
            if(labels)TextAt(g,name,new Rectangle(49,0,r.Width-53,r.Height),10,Theme.Paper,selected);
        });
    }
    void DrawRailEntry(Graphics g,Rectangle r,string name,string symbol,bool selected){
        DrawRailPill(g,r,Array.IndexOf(new[]{"Home","Search","Library"},name));bool labels=nav.Width>100;
        g.DrawImage(images[symbol],new Rectangle(labels?14:(r.Width-22)/2,(r.Height-22)/2,22,22));
        if(labels)TextAt(g,name,new Rectangle(49,0,r.Width-53,r.Height),10,Theme.Paper,selected);
    }
    void LayoutRailExtras(){
        if(railBrand==null)return;
        foreach(Control c in new Control[]{railBrand,railQueue,railDiscover,railDownloads,railFiles,railSettings})c.Visible=Wide;
        if(!Wide)return;int w=nav.Width;
        Place(railBrand,8,16,w-16,50);Place(railQueue,10,238,w-20,42);Place(railDiscover,10,286,w-20,42);
        Place(railDownloads,10,334,w-20,42);
        Place(railFiles,10,Math.Max(398,nav.Height-210),w-20,42);Place(railSettings,10,Math.Max(446,nav.Height-162),w-20,42);
    }
    void DesktopBarVisible(bool visible){
        if(barQueue==null)return;foreach(Control c in new Control[]{barQueue,barPosition,barTime,volume})c.Visible=visible;
    }
    void LayoutPlayerBar(Control previous,Control next){
        DesktopBarVisible(false);int w=mini.Width,h=mini.Height;
        int buttons=148;Place(miniTrack,7,7,Math.Max(60,w-buttons-20),h-14);
        int first=w-buttons;Place(previous,first,(h-36)/2,36,36);Place(pause,first+44,(h-42)/2,42,42);Place(next,first+94,(h-36)/2,36,36);

    }
    static bool LandscapePlayer(Rectangle r){return r.Width>=620&&r.Width>r.Height*.9;}
    static Rectangle AlbumBounds(Rectangle r){
        int size=Math.Min((int)(r.Width*.44),r.Height-104);
        return new Rectangle(22,(r.Height-size)/2,Math.Max(1,size),Math.Max(1,size));
    }
}
