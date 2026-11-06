using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class Theme {
    internal static readonly Color Ink=Color.FromArgb(11,13,15), Card=Color.FromArgb(23,26,29),
        Edge=Color.FromArgb(51,58,63), Paper=Color.FromArgb(239,242,244), Muted=Color.FromArgb(158,170,178);
    internal static Color Peach=Color.FromArgb(177,220,237);
    internal static Font Font(float n,bool bold=false){return new Font(bold?"Segoe UI Semibold":"Segoe UI",n);}
    internal static GraphicsPath Round(Rectangle r,int radius){
        var p=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        if(d<=0)return p;
        p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
}
internal sealed class SoftButton : Button {
    internal bool Primary,GlassTransport;
    readonly bool transport;
    readonly Motion hoverMotion,pressMotion,playMix;
    internal SoftButton(string name,Action action,bool primary=false,bool icon=false) {
        hoverMotion=new Motion(Invalidate);pressMotion=new Motion(Invalidate);playMix=new Motion(Invalidate);
        Text=name;AccessibleName=name;Primary=primary;transport=icon;Cursor=Cursors.Hand;Font=Theme.Font(10,true);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Margin=Padding.Empty;Dock=DockStyle.None;
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);
        Click+=delegate{action();};
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hoverMotion.To(1,105);}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverMotion.To(0,145);pressMotion.To(0,100);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left)pressMotion.To(1,70);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressMotion.To(0,120);}
    protected override void Dispose(bool disposing){if(disposing){hoverMotion.Dispose();pressMotion.Dispose();playMix.Dispose();}base.Dispose(disposing);}
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);AccessibleName=Text;if(playMix!=null&&(Text=="Play"||Text=="Pause"))playMix.To(Text=="Pause"?1:0,125);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent.BackColor);Surface.Background(this,g);
        var r=new Rectangle(1,1,Width-3,Height-3);
        int inset=(int)Math.Round(pressMotion.Value*2);r.Inflate(-inset,-inset);
        Color normal=Primary?Theme.Peach:(Parent.BackColor==Theme.Ink?Color.FromArgb(62,64,66):Theme.Card);
        if(GlassTransport){using(var path=Theme.Round(r,r.Height/2))using(var fill=new SolidBrush(Color.FromArgb((int)(24+hoverMotion.Value*22),255,255,255)))using(var edge=new Pen(Color.FromArgb((int)(48+hoverMotion.Value*32),255,255,255))){g.FillPath(fill,path);g.DrawPath(edge,path);}}
        else using(var path=Theme.Round(r,transport?22:10))using(var brush=new SolidBrush(Motion.Mix(normal,Primary?Color.FromArgb(205,230,243):Theme.Edge,hoverMotion.Value)))g.FillPath(brush,path);
        var fg=!Enabled?Theme.Muted:GlassTransport?Color.FromArgb(244,247,249):Primary?Theme.Ink:Theme.Paper;
        if(!transport)TextRenderer.DrawText(g,Text,Font,ClientRectangle,fg,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        else {
            int x=Width/2,y=Height/2;
            using(var b=new SolidBrush(fg)){
                if(Text=="Play"||Text=="Pause"){
                    float mix=playMix.Value;
                    if(mix<1)using(var triangle=new SolidBrush(Color.FromArgb((int)(255*(1-mix)),fg)))g.FillPolygon(triangle,new[]{new Point(x-5,y-7),new Point(x+6,y),new Point(x-5,y+7)});
                    if(mix>0)using(var bars=new SolidBrush(Color.FromArgb((int)(255*mix),fg))){g.FillRectangle(bars,x-6,y-7,4,14);g.FillRectangle(bars,x+2,y-7,4,14);}
                }
                else if(Text=="Previous"){g.FillPolygon(b,new[]{new Point(x+6,y-6),new Point(x-3,y),new Point(x+6,y+6)});g.FillRectangle(b,x-7,y-6,2,12);}
                else {g.FillPolygon(b,new[]{new Point(x-5,y-7),new Point(x+6,y),new Point(x-5,y+7)});if(Text=="Next")g.FillRectangle(b,x+7,y-6,2,12);}
            }
        }
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(5,5,Width-10,Height-10),fg,Primary?Theme.Peach:Theme.Card);
    }
}
internal sealed partial class Meter : TrackBar {
    internal Meter(){InitProgress();AutoSize=false;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);TickStyle=TickStyle.None;Cursor=Cursors.Hand;}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(Parent.BackColor);Surface.Background(this,g);g.SmoothingMode=SmoothingMode.AntiAlias;
        int y=Height/2,x=7+(int)((Width-14)*DisplayFraction);
        using(var pen=new Pen(Theme.Edge,4)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,7,y,Math.Max(7,Width-7),y);}
        if(Enabled){DrawPlayed(g,x,y);using(var b=new SolidBrush(Theme.Peach))g.FillEllipse(b,x-5,y-5,10,10);}
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle);
    }
    void SetFromX(int x){Value=(int)Math.Round(Math.Max(0,Math.Min(1,(x-7.0)/Math.Max(1,Width-14)))*Maximum);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Capture=true;SetFromX(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture&&e.Button==MouseButtons.Left)SetFromX(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){if(e.Button==MouseButtons.Left)SetFromX(e.X);base.OnMouseUp(e);Capture=false;}
    protected override void OnValueChanged(EventArgs e){base.OnValueChanged(e);Invalidate();}
}
internal sealed class Surface : Panel {
    internal Action<Graphics,Rectangle> PaintSurface;
    internal static void Background(Control c,Graphics g){var surface=c.Parent as Surface;if(surface==null||surface.PaintSurface==null)return;var st=g.Save();g.TranslateTransform(-c.Left,-c.Top);surface.PaintSurface(g,surface.ClientRectangle);g.Restore(st);}
    internal Surface(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);Margin=Padding.Empty;}
    protected override void OnPaintBackground(PaintEventArgs e){base.OnPaintBackground(e);if(PaintSurface!=null)PaintSurface(e.Graphics,ClientRectangle);}
}
internal sealed class ArtButton : Button {
    readonly Action<Graphics,Rectangle> draw;
    internal bool AnimateHover;
    readonly Motion hoverMotion,pressMotion;
    internal float HoverAmount{get{return hoverMotion.Value;}}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hoverMotion.To(1,110);}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverMotion.To(0,145);pressMotion.To(0,100);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left)pressMotion.To(1,75);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressMotion.To(0,125);}
    protected override void Dispose(bool disposing){if(disposing){hoverMotion.Dispose();pressMotion.Dispose();}base.Dispose(disposing);}
    internal ArtButton(string label,Action click,Action<Graphics,Rectangle> paint){
        hoverMotion=new Motion(Invalidate);pressMotion=new Motion(Invalidate);
        Text=label;AccessibleName=label;draw=paint;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Margin=Padding.Empty;
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);Click+=delegate{click();};
    }
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.Clear(Parent.BackColor);Surface.Background(this,g);draw(g,ClientRectangle);
        if(!Enabled){using(var brush=new SolidBrush(Color.FromArgb(145,Parent.BackColor)))g.FillRectangle(brush,ClientRectangle);return;}
        float tint=(AnimateHover?0:HoverAmount*39)+pressMotion.Value*32;
        if(tint>=1&&Width>3&&Height>3){using(var shape=Theme.Round(new Rectangle(1,1,Width-2,Height-2),AnimateHover?22:12))using(var b=new SolidBrush(Color.FromArgb((int)tint,Theme.Peach)))g.FillPath(b,shape);}
        if(!AnimateHover&&HoverAmount>.01f&&Width>4&&Height>4){using(var shape=Theme.Round(new Rectangle(1,1,Width-3,Height-3),12))using(var pen=new Pen(Color.FromArgb((int)(100*HoverAmount),Theme.Peach)))g.DrawPath(pen,shape);}
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(3,3,Width-6,Height-6));
    }
}
internal partial class Player {
    readonly System.Collections.Generic.Dictionary<string,Image> images=new System.Collections.Generic.Dictionary<string,Image>();
    readonly System.Collections.Generic.HashSet<string> loading=new System.Collections.Generic.HashSet<string>();
    readonly System.Collections.Generic.List<Track> recent=new System.Collections.Generic.List<Track>(), liked=new System.Collections.Generic.List<Track>();
    readonly Panel stageClip=new Panel();PageMotion scene;
    Motion coverMix;Image previousCover;
    Track[] picks;
    readonly Surface home=new Surface(),mini=new Surface(),nav=new Surface(),detail=new Surface(),queuePage=new Surface();
    readonly Surface searchPage=new Surface(),library=new Surface();readonly Panel stage=new Panel();
    readonly Label queueTitle=new Label(),queueEmpty=new Label();
    readonly ListView libraryRows=new ResultsList();
    readonly Label libraryTitle=new Label();
    ArtButton miniTrack;ArtButton[] navButtons=new ArtButton[3],features=new ArtButton[4],shelf=new ArtButton[6];
    readonly ToolTip help=new ToolTip(); string page="Home",listMode="Recent";int hero;
    readonly Color blue=Color.FromArgb(39,54,63);
    bool shuffle,repeat; readonly Random order=new Random();
    readonly System.Threading.SemaphoreSlim coverSlots=new System.Threading.SemaphoreSlim(8);
    static readonly System.Net.Http.HttpClient artClient=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(8)};
    Track Current {get{return index>=0&&index<queue.Count?queue[index]:picks[2];}}
    static string Key(Track t){return (t.BrowseId==null?null:t.BrowseId+(string.IsNullOrEmpty(t.BrowseParams)?"":":"+t.BrowseParams))??t.File??(t.QobuzId>0?"q:"+t.QobuzId:t.Id);}
    static Image Resource(string name){
        using(var s=typeof(Player).Assembly.GetManifestResourceStream(name))using(var img=Image.FromStream(s))return new Bitmap(img);
    }
    void Remember(Track t){MarkLibraryAdded(t);CountListen(t,false);recent.RemoveAll(x=>Key(x)==Key(t));recent.Insert(0,t);if(recent.Count>50)recent.RemoveAt(50);SaveLibrary();UpdatePicks(t);if(page=="Library")FillLibrary();}
    Image Cover(Track t){Image art;if(images.TryGetValue(Key(t)??"",out art)){artAccess[Key(t)??""]=++artClock;return art;}if(t.Kind=="Local playlist"){var p=featuresState.Playlists.Find(x=>"local:"+x.Id==t.BrowseId);if(p!=null&&p.Songs.Count>0)return Cover(p.Songs[0]);}return images["logo"];}
    readonly System.Collections.Generic.HashSet<string> qualityLoaded=new System.Collections.Generic.HashSet<string>();
    readonly System.Collections.Generic.List<Image> replacedArt=new System.Collections.Generic.List<Image>();
    readonly System.Collections.Generic.HashSet<string> fullArtwork=new System.Collections.Generic.HashSet<string>(), fullArtworkWanted=new System.Collections.Generic.HashSet<string>();
    readonly System.Threading.SemaphoreSlim fullCoverSlots=new System.Threading.SemaphoreSlim(2);
    async void FetchArtwork(Track t,bool full=false){
        if(t==null||closing||IsDisposed||!IsHandleCreated)return;
        if(InvokeRequired){try{BeginInvoke((Action)delegate{FetchArtwork(t,full);});}catch(InvalidOperationException){}return;}
        string key=Key(t);Uri uri;
        if(key==null||!Uri.TryCreate(t.Artwork,UriKind.Absolute,out uri)||uri.Scheme!="https")return;
        if(full&&!fullArtwork.Contains(key))fullArtworkWanted.Add(key);
        if((qualityLoaded.Contains(key)&&!fullArtworkWanted.Contains(key))||!loading.Add(key))return;
        try{
            if(!qualityLoaded.Contains(key)){
                await coverSlots.WaitAsync();try{if(!closing)InstallArtwork(key,await CoverQuality.LoadSize(t.Artwork,artClient,false));}finally{coverSlots.Release();}
            }
            if(fullArtworkWanted.Remove(key)&&!closing){
                await fullCoverSlots.WaitAsync();try{if(!closing){var art=await CoverQuality.LoadSize(t.Artwork,artClient,true);if(art!=null){InstallArtwork(key,art);fullArtwork.Add(key);}}}finally{fullCoverSlots.Release();}
            }
        }finally{loading.Remove(key);}
    }
    void InstallArtwork(string key,Bitmap decoded){
        if(decoded==null)return;if(closing||IsDisposed||!IsHandleCreated){decoded.Dispose();return;}
        if(InvokeRequired){try{BeginInvoke((Action)delegate{InstallArtwork(key,decoded);});}catch(InvalidOperationException){decoded.Dispose();}return;}
        Image old;if(images.TryGetValue(key,out old)){
            if(old.Width>=decoded.Width&&old.Height>=decoded.Height){decoded.Dispose();qualityLoaded.Add(key);return;}
            replacedArt.Add(old);
        }
        images[key]=decoded;qualityLoaded.Add(key);artAccess[key]=++artClock;TrimArtwork();ArtworkArrived(key);if(key==Key(Current))RefreshSurfaces();
    }
    static void Fill(Graphics g,Rectangle r,int radius,Color color){
        if(r.Width<2||r.Height<2)return;
        using(var shape=Theme.Round(new Rectangle(r.X,r.Y,r.Width-1,r.Height-1),radius))
        using(var b=new SolidBrush(color))g.FillPath(b,shape);
    }
    static void TextAt(Graphics g,string value,Rectangle r,float size,Color color,bool bold=false,bool center=false,bool wrap=false){
        using(var f=Theme.Font(size,bold))TextRenderer.DrawText(g,value,f,r,color,TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|
            (wrap?TextFormatFlags.WordBreak:TextFormatFlags.SingleLine)|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|
            (center?TextFormatFlags.HorizontalCenter:TextFormatFlags.Left));
    }
    sealed class Thumbnail {internal Image Source;internal Bitmap Pixels;internal int Radius;}
    readonly System.Collections.Generic.LinkedList<Thumbnail> thumbnails=new System.Collections.Generic.LinkedList<Thumbnail>();
    void Artwork(Graphics g,Image img,Rectangle r,int radius){
        if(r.Width<2||r.Height<2)return;
        if(r.Width>300||r.Height>300){RenderArtwork(g,img,r,radius);return;}
        for(var node=thumbnails.First;node!=null;node=node.Next){
            var t=node.Value;if(t.Source!=img||t.Radius!=radius||t.Pixels.Size!=r.Size)continue;
            thumbnails.Remove(node);thumbnails.AddFirst(node);g.DrawImageUnscaled(t.Pixels,r.Location);return;
        }
        var pixels=new Bitmap(r.Width,r.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var canvas=Graphics.FromImage(pixels)){
            canvas.SmoothingMode=SmoothingMode.AntiAlias;canvas.InterpolationMode=InterpolationMode.HighQualityBicubic;canvas.PixelOffsetMode=PixelOffsetMode.HighQuality;
            RenderArtwork(canvas,img,new Rectangle(Point.Empty,r.Size),radius);
        }
        thumbnails.AddFirst(new Thumbnail{Source=img,Pixels=pixels,Radius=radius});
        if(thumbnails.Count>64){thumbnails.Last.Value.Pixels.Dispose();thumbnails.RemoveLast();}
        g.DrawImageUnscaled(pixels,r.Location);
    }
    static void RenderArtwork(Graphics g,Image img,Rectangle r,int radius){
        if(r.Width<2||r.Height<2)return;var saved=g.Save();
        using(var clip=Theme.Round(r,radius)){g.SetClip(clip,System.Drawing.Drawing2D.CombineMode.Intersect);
            float scale=Math.Max((float)r.Width/img.Width,(float)r.Height/img.Height);
            float w=img.Width*scale,h=img.Height*scale;g.DrawImage(img,r.X+(r.Width-w)/2,r.Y+(r.Height-h)/2,w,h);
        }g.Restore(saved);
    }
    static void Glyph(Graphics g,string glyph,Rectangle r,Color color,float size){
        using(var font=new Font("Segoe MDL2 Assets",size))TextRenderer.DrawText(g,glyph,font,r,color,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
    }
    ArtButton Make(Control parent,string name,Action click,Action<Graphics,Rectangle> paint){
        var b=new ArtButton(name,click,paint);parent.Controls.Add(b);help.SetToolTip(b,name);return b;
    }
    ArtButton Pill(Control parent,string title,Action fn,bool primary=false){
        ArtButton button=null;button=Make(parent,title,fn,delegate(Graphics g,Rectangle r){Fill(g,r,r.Height/2,primary?Theme.Peach:Theme.Card);TextAt(g,button.Text,r,10,primary?Theme.Ink:Theme.Paper,true,true);});return button;
    }
    static void Place(Control c,int x,int y,int w,int h){DarkScrollHost.Frame(c).SetBounds(x,y,Math.Max(1,w),Math.Max(1,h));}
    void BuildWindow(){
        Text="Noctune";StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Ink;ForeColor=Theme.Paper;Font=Theme.Font(10);
        AutoScaleMode=AutoScaleMode.Dpi;var area=Screen.FromPoint(Cursor.Position).WorkingArea;ClientSize=new Size(Math.Min(1220,area.Width-54),Math.Min(800,area.Height-72));MinimumSize=new Size(360,570);
        using(var icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath))Icon=(Icon)icon.Clone();
        foreach(string key in new[]{"brand","history","calendar","alerts","settings","logo","navhome","navsearch","navlibrary"})images[key]=Resource(key+".png");
        using(var s=typeof(Player).Assembly.GetManifestResourceStream("picks.json"))using(var r=new System.IO.StreamReader(s))picks=Catalog.Json().Deserialize<Track[]>(r.ReadToEnd());
        picks[2].Title="Kogoesouna Kisetsu Kara";for(int i=0;i<picks.Length;i++)images[Key(picks[i])]=Resource("pick"+(i+1)+".jpg");
        scene=new PageMotion(stage);Controls.Add(stageClip);stageClip.BackColor=Theme.Ink;stageClip.Controls.Add(stage);Controls.Add(mini);Controls.Add(nav);stage.Controls.Add(homeScroll);homeScroll.Controls.Add(home);homeScroll.AutoScroll=true;homeScroll.Dock=DockStyle.Fill;
        foreach(Control c in new Control[]{searchPage,library,detail,queuePage,lyricsPage}){stage.Controls.Add(c);c.Dock=DockStyle.Fill;c.BackColor=Theme.Ink;}
        BuildHome();BuildSearch();BuildLibrary();BuildPlayer();BuildQueue();BuildLyrics();BuildMiniNav();BuildDesktopControls();InitDiscover();BuildPremium();BuildDarkScrollbars();BuildCollections();BuildPolishedPages();BuildEnhancements();BuildVisualRefresh();BuildSpotify();BuildGpuPlayer();InitDesktopSession();
        ClientSizeChanged+=delegate{scene.End();WindowLayout();};WindowLayout();ShowPage("Home");
        KeyPreview=true;KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape&&suggestions.Visible){DismissSuggestions();query.Focus();e.SuppressKeyPress=true;}else if(e.Control&&e.KeyCode==Keys.F){ShowPage("Search");e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){ShowPage(page=="Queue"||page=="Lyrics"?"Player":"Home");e.SuppressKeyPress=true;}};
        FormClosed+=delegate{scene.Dispose();foreach(var thumb in thumbnails)thumb.Pixels.Dispose();thumbnails.Clear();foreach(var frame in cardFrames)frame.Pixels.Dispose();cardFrames.Clear();foreach(var image in images.Values)image.Dispose();foreach(var image in replacedArt)image.Dispose();help.Dispose();};
    }
    void WindowLayout(){
        int w=ClientSize.Width,h=ClientSize.Height;
        bool immersive=page=="Player"||page=="Queue"||page=="Lyrics";
        if(Wide){
            int rail=w>=1000?176:76;
            bool showBar=page!="Player"&&page!="Lyrics";int bottom=showBar?104:0;
            Place(nav,0,0,rail,h);
            Place(stageClip,rail+16,12,w-rail-36,h-bottom-24);mini.Visible=showBar;nav.Visible=true;
        }else{
            int nw=Math.Min(450,w-24);
            Place(nav,(w-nw)/2,h-65,nw,60);
            Place(stageClip,0,0,w,immersive?h:h-133);mini.Visible=nav.Visible=!immersive;
        }
        Place(stage,0,0,stageClip.Width,stageClip.Height);LayoutPages();LayoutHomeViewport();
        if(layoutNav!=null)layoutNav();
        LayoutInAppPlayer();if(layoutMini!=null)layoutMini();
        RoundPlayerBar();
    }
    void ShowPage(string next){
        CloseGpuOptions();
        QueueGlassRefresh();
        scene.End();bool animate=page!=next&&Visible&&WindowState!=FormWindowState.Minimized;
        page=next;WindowLayout();ShiftRail();
        if(next=="Collections")RefreshCollectionsHub();if(next=="Lyrics")LoadLyrics();if(next=="Search")query.Focus();if(next=="Library")FillLibrary();
        RefreshSurfaces();
        if(animate)scene.Start(next=="Player");
    }
    void RefreshSurfaces(){Theme.Peach=AdaptivePalette.From(ArtworkTone(Cover(Current))).Accent;foreach(Control c in new Control[]{home,mini,nav,detail,queuePage})if(c.Visible)c.Invalidate(true);}
    void BuildHome(){
        var brand=Make(home,"Noctune home",delegate{hero=0;home.Invalidate(true);},(g,r)=>g.DrawImage(images["brand"],r));
        string[] icons={"history","calendar","alerts","settings"};
        Action[] actions={delegate{listMode="Recent";ShowPage("Library");},delegate{SearchFor("new music releases");},delegate{DialogUI.Show(this,index<0?"Nothing playing.":now.Text+"\n"+status.Text,"Player activity");},Settings};
        string[] titles={"Listening history","Find new releases","Player activity","Settings"};var top=new ArtButton[4];
        for(int i=0;i<4;i++){string key=icons[i];top[i]=Make(home,titles[i],actions[i],(g,r)=>g.DrawImage(images[key],r));}
        home.PaintSurface=delegate(Graphics g,Rectangle r){};
        var moods=new[]{"Workout","Relax","Energize","Party","Feel good"};var chips=new ArtButton[moods.Length];
        for(int i=0;i<moods.Length;i++){string term=moods[i];chips[i]=Make(home,term,delegate{SearchFor(term+" music");},delegate(Graphics g,Rectangle r){Fill(g,r,r.Height/2,Theme.Card);TextAt(g,term,r,11,Theme.Paper,true,true);});}
        var heading=homeTitle=LabelOn(home,"Quick picks",19);homeReason=LabelOn(home,"",10);homeReason.Visible=false;homeReason.Font=Theme.Font(10);homeReason.ForeColor=Theme.Muted;var keep=LabelOn(home,"Keep listening",17);
        var playAll=Pill(home,"▶   Play all",delegate{int first=queue.Count;for(int i=0;i<HomePicks.Length;i++)Add(HomePicks[(hero+i)%HomePicks.Length],false);Play(first);},true);
        for(int i=0;i<features.Length;i++){
            int offset=i;features[i]=Make(home,"Quick pick",delegate{if(Wide||offset==0){var t=HomePicks[(hero+offset)%HomePicks.Length];if(index>=0&&Key(Current)==Key(t))Toggle();else Add(t,true);}else {hero=(hero+offset)%HomePicks.Length;AnimateCards();} },
                delegate(Graphics g,Rectangle r){DrawFeature(g,r,HomePicks[(hero+offset)%HomePicks.Length],Wide||offset==0,features[offset].HoverAmount);});
            features[i].AnimateHover=true;help.SetToolTip(features[i],null);features[i].MouseWheel+=ScrollPicks;
        }
        for(int i=0;i<shelf.Length;i++){
            int k=i;shelf[i]=Make(home,"Keep listening",delegate{Add(ShelfTrack(k),true);},delegate(Graphics g,Rectangle r){
                Track t=ShelfTrack(k);float h=shelf[k].HoverAmount;
                if(Wide){DrawAlbumTile(g,r,t,h);return;}int d=Math.Min(r.Width-4,Math.Max(1,r.Height-45));
                var tile=new Rectangle(2,6-(int)Math.Round(5*h),d,d);
                Artwork(g,Cover(t),tile,12);HoverFinish(g,tile,12,h,false);
                TextAt(g,t.Title,new Rectangle(2,tile.Bottom+1,r.Width-4,21),10,Theme.Paper,true);
                TextAt(g,t.Artist,new Rectangle(2,tile.Bottom+21,r.Width-4,18),9,Theme.Muted);
            });
            shelf[i].AnimateHover=true;help.SetToolTip(shelf[i],null);
        }
        var leftArrow=Pill(home,"‹",delegate{hero=(hero+HomePicks.Length-1)%HomePicks.Length;AnimateCards();});var rightArrow=Pill(home,"›",delegate{hero=(hero+1)%HomePicks.Length;AnimateCards();});leftArrow.AccessibleName="Previous picks";rightArrow.AccessibleName="Next picks";
        layoutHome=delegate{
            int w=home.Width,h=home.Height;int m=16,chipY=68,sectionY=128,cardY=172;
                        brand.Visible=!Wide;leftArrow.Visible=rightArrow.Visible=Wide;int iconW=31;
            if(Wide){
                LayoutPremiumHome(heading,keep,playAll,leftArrow,rightArrow,chips,top);
                return;
            }
            foreach(var button in top)button.Visible=true;
            for(int i=0;i<features.Length;i++)features[i].Visible=i<3;
            Place(brand,m,8,115,40);
            for(int i=0;i<top.Length;i++)Place(top[i],w-m-((4-i)*43),13,iconW,30);
            int x=m;foreach(var chip in chips){int cw=TextRenderer.MeasureText(chip.Text,Font).Width+19;chip.Visible=x+cw<=w-180;Place(chip,x,chipY,cw,35);x+=cw+6;}
            if(homeReason.Text.Length>0)cardY+=19;Place(homeReason,m,sectionY+34,w-32,21);Place(heading,m,sectionY,w-145,36);Place(playAll,w-131,sectionY,115,36);
            int d=Math.Min(160,(int)(w*.335));int cardH=Math.Min(380,Math.Max(145,h-cardY-d-96));int cardW=Math.Min(440,(int)((w-m)*.77));
            Place(features[0],m,cardY,cardW,cardH);int peek=Math.Max(35,Math.Min(80,(w-m-cardW)/2-8));
            Place(features[1],m+cardW+10,cardY,peek,cardH);Place(features[2],m+cardW+peek+20,cardY,Math.Max(peek,w-m-cardW-peek-20),cardH);
            int titleY=cardY+cardH+18;Place(keep,m,titleY,w-32,32);
            for(int i=0;i<shelf.Length;i++){int sx=m+i*(d+16);shelf[i].Visible=sx<w;Place(shelf[i],sx,titleY+42,Math.Min(d,Math.Max(1,w-sx)),d+25);}
        };home.Resize+=delegate{layoutHome();};
    }
    Track ShelfTrack(int i){
        if(i<recent.Count)return recent[i];
        var remaining=new System.Collections.Generic.List<Track>();
        for(int j=0;j<picks.Length;j++){var t=picks[(j+2)%picks.Length];if(!recent.Exists(x=>Key(x)==Key(t)))remaining.Add(t);}
        return remaining.Count==0?picks[i%picks.Length]:remaining[(i-recent.Count)%remaining.Count];
    }
    sealed class CardFrame {
        internal Image Art;internal Track Track;internal Bitmap Pixels;internal bool Caption;
    }
    readonly System.Collections.Generic.LinkedList<CardFrame> cardFrames=new System.Collections.Generic.LinkedList<CardFrame>();
    int wheelRemainder;
    void ScrollPicks(object sender,MouseEventArgs e){
        var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
        int delta=SystemInformation.MouseWheelScrollDelta;
        wheelRemainder+=e.Delta;int steps=wheelRemainder/delta;wheelRemainder%=delta;
        if(steps==0)return;
        int count=HomePicks.Length;hero=((hero-steps)%count+count)%count;
        AnimateCards(false);
    }
    Bitmap CardPixels(Track t,Size size,bool caption){
        Image art=Cover(t);
        for(var node=cardFrames.First;node!=null;node=node.Next){
            var frame=node.Value;
            if(frame.Art==art&&frame.Track==t&&frame.Pixels.Size==size&&frame.Caption==caption){
                cardFrames.Remove(node);cardFrames.AddFirst(node);return frame.Pixels;
            }
        }
        var bitmap=new Bitmap(size.Width,size.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(bitmap)){
            var r=new Rectangle(Point.Empty,size);int radius=Math.Min(23,r.Width/2);
            g.Clear(Theme.Ink);g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            Artwork(g,art,r,radius);
            using(var clip=Theme.Round(r,radius))
            using(var shade=new LinearGradientBrush(r,Color.Transparent,Color.FromArgb(222,0,0,0),90f))g.FillPath(shade,clip);
            if(caption){
                TextAt(g,t.Title,new Rectangle(16,r.Height-74,r.Width-32,45),13,Color.White,true,false,true);
                TextAt(g,t.Artist,new Rectangle(16,r.Height-29,r.Width-32,22),10,Theme.Paper);
            }
        }
        cardFrames.AddFirst(new CardFrame{Art=art,Track=t,Pixels=bitmap,Caption=caption});
        if(cardFrames.Count>16){cardFrames.Last.Value.Pixels.Dispose();cardFrames.RemoveLast();}
        return bitmap;
    }
    void DrawFeature(Graphics g,Rectangle r,Track t,bool caption,float hover){
        if(r.Width<2||r.Height<2)return;

        var tile=new Rectangle(2,6-(int)Math.Round(5*hover),Math.Max(1,r.Width-4),Math.Max(1,r.Height-8));
        g.DrawImageUnscaled(CardPixels(t,tile.Size,caption),tile.Location);
        if(caption)HoverFinish(g,tile,Math.Min(23,tile.Width/2),hover,true);
        if(cardReveal<1)Fill(g,tile,Math.Min(23,tile.Width/2),Color.FromArgb((int)(120*(1-cardReveal)),16,20,22));
    }
    static void HoverFinish(Graphics g,Rectangle tile,int radius,float h,bool large){
        if(h<=.005f||tile.Width<34||tile.Height<35)return;
        var edge=new Rectangle(tile.X+1,tile.Y+1,tile.Width-3,tile.Height-3);
        using(var shape=Theme.Round(edge,radius))using(var line=new Pen(Color.FromArgb((int)(176*h),Theme.Peach),1.5f))g.DrawPath(line,shape);
        int size=large?38:30;
        var badge=new Rectangle(tile.Right-size-10,tile.Top+9+(int)((1-h)*13),size,size);
        Fill(g,badge,size/2,Color.FromArgb((int)(252*h),218,241,250));
        Glyph(g,"\uE768",badge,Color.FromArgb((int)(255*h),16,44,60),large?14:11);
    }
    static Label LabelOn(Control parent,string text,int size){var l=new Label{BackColor=Color.Transparent,Text=text,Font=Theme.Font(size,true),ForeColor=Theme.Paper,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};parent.Controls.Add(l);return l;}
    void SearchFor(string term){ShowPage("Search");query.Text=term;RunSearch();}
    async void RunSearch(){await Search();}
    void MiniCover(Graphics g,Track t,Rectangle rect,int radius){
        Artwork(g,Cover(t),rect,radius);
        if(previousCover==null||coverMix.Value>=1)return;
        using(var shape=Theme.Round(rect,radius))
        using(var tint=new System.Drawing.Imaging.ImageAttributes()){
            var state=g.Save();g.SetClip(shape,CombineMode.Intersect);
            try{
                var fade=new System.Drawing.Imaging.ColorMatrix();fade.Matrix33=1-coverMix.Value;tint.SetColorMatrix(fade);
                int side=Math.Min(previousCover.Width,previousCover.Height);
                g.DrawImage(previousCover,rect,(previousCover.Width-side)/2,(previousCover.Height-side)/2,side,side,GraphicsUnit.Pixel,tint);
            }finally{g.Restore(state);}
        }
    }
    void BuildMiniNav(){
        coverMix=new Motion(delegate{if(mini.Visible)miniTrack.Invalidate();},delegate{previousCover=null;});mini.Disposed+=delegate{coverMix.Dispose();};
        mini.BackColor=nav.BackColor=Theme.Ink;
        mini.PaintSurface=PaintFloatingBar;
        miniTrack=Make(mini,"Open now playing",delegate{if(!miniDragMoved)ShowPage("Player");},delegate(Graphics g,Rectangle r){
            var t=Current;int size=Math.Min(46,r.Height-6);MiniCover(g,t,new Rectangle(6,(r.Height-size)/2,size,size),size/2);int x=size+14;
            TextAt(g,t.Title,new Rectangle(x,1,r.Width-x-3,24),10,Theme.Paper,true);
            TextAt(g,starting!=null?"Finding audio…":active&&audio.Buffering?"Buffering…":t.Artist,new Rectangle(x,24,r.Width-x-3,19),8,Theme.Muted);
        });
        var prev=new SoftButton("Previous",delegate{Step(-1);},false,true);
        pause=new SoftButton("Play",Toggle,true,true);var next=new SoftButton("Next",delegate{Step(1);},false,true);prev.GlassTransport=next.GlassTransport=true;((SoftButton)pause).GlassTransport=true;((SoftButton)pause).Primary=false;
        foreach(var b in new[]{prev,pause,next})mini.Controls.Add(b);
        layoutMini=delegate{LayoutPlayerBar(prev,next);};mini.Resize+=delegate{layoutMini();};
        nav.PaintSurface=delegate(Graphics g,Rectangle r){if(Wide)g.Clear(Theme.Card);else Fill(g,r,24,Theme.Card);};
        string[] names={"Home","Search","Library"};string[] glyphs={"navhome","navsearch","navlibrary"};
        for(int i=0;i<3;i++){string label=names[i],symbol=glyphs[i];navButtons[i]=Make(nav,label,delegate{ShowPage(label);},delegate(Graphics g,Rectangle r){
            g.Clear(Theme.Card);bool selected=page==label;if(Wide){DrawRailEntry(g,r,label,symbol,selected);return;}var mark=new Rectangle((r.Width-54)/2,3,54,29);
            if(selected)Fill(g,mark,16,blue);g.DrawImage(images[symbol],new Rectangle(mark.X+(mark.Width-23)/2,mark.Y+3,23,23));
            TextAt(g,label,new Rectangle(0,32,r.Width,24),9,Theme.Paper,selected,true);
        });}
        layoutNav=delegate{LayoutRailExtras();if(Wide){for(int i=0;i<3;i++)Place(navButtons[i],10,94+i*48,nav.Width-20,42);return;}int w=(nav.Width-38)/3;for(int i=0;i<3;i++)Place(navButtons[i],19+w*i,2,w,nav.Height-4);};nav.Resize+=delegate{layoutNav();};
    }
    void BuildSearch(){
        var title=LabelOn(searchPage,"Find your next favorite",23);var spotify=Pill(searchPage,"Spotify",OpenSpotify);
        var yt=SourceButton("YouTube Music",false);var qb=SourceButton("Qobuz",true);
        sourceLabel=LabelOn(searchPage,"YouTube Music",10);
        BuildSearchField();
        query.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;RunSearch();}};
        searchPage.Controls.Add(searchStatus);searchStatus.ForeColor=Theme.Muted;searchStatus.Text="Search songs or artists";
        BuildResultViewport();var play=Pill(searchPage,"Play",delegate{Selected(results,true);},true);var add=Pill(searchPage,"Add to queue",delegate{Selected(results,false);});
        searchPage.Resize+=delegate{int w=searchPage.Width,h=searchPage.Height;
            Place(title,16,7,w-136,43);Place(spotify,w-108,12,92,34);Place(yt,16,54,134,34);Place(qb,157,54,77,34);Place(sourceLabel,242,54,w-250,34);sourceLabel.Visible=false;
            Place(searchField,16,101,w-32,46);Place(searchStatus,16,150,w-32,24);
            Place(resultClip,10,180,w-37,h-232);Place(resultScroll,w-26,180,16,h-232);Place(play,16,h-47,88,37);Place(add,111,h-47,133,37);};
        searchPlay=play;searchAdd=add;
    }
    ArtButton SourceButton(string title,bool q){return Make(searchPage,title,delegate{SetSource(q);},delegate(Graphics g,Rectangle r){bool selected=q==useQobuz;Fill(g,r,r.Height/2,selected?blue:Theme.Card);TextAt(g,title,r,10,selected?Theme.Peach:Theme.Paper,true,true);});}
    Label sourceLabel;
    void SetSource(bool q){if(q&&categoryPicker!=null){searchKind="Songs";categoryPicker.SelectedIndex=0;}DismissSuggestions();if(q!=useQobuz){useQobuz=q;if(searching!=null){var old=searching;searching=null;old.Cancel();}results.Items.Clear();searchStatus.Text="Search songs or artists";}sourceLabel.Text=q?"Qobuz":"YouTube Music";searchPage.Invalidate(true);query.Focus();}
    void Settings(){ShowPage("Settings");}
    void BuildLibrary(){
        var title=LabelOn(library,"Library",22);var open=Pill(library,"Open files…",OpenFiles,true);
        var recentTab=Pill(library,"Recent",delegate{listMode="Recent";FillLibrary();});
        var likedTab=Pill(library,"Liked songs",delegate{listMode="Liked songs";FillLibrary();});
        var queueTab=Pill(library,"Queue",delegate{ShowPage("Queue");});
        library.Controls.Add(libraryTitle);libraryTitle.Font=Theme.Font(13,true);SetList(libraryRows,library,"Library songs");
        library.Resize+=delegate{int w=library.Width,h=library.Height;Place(title,16,8,w-145,45);Place(open,w-128,11,112,36);
            Place(recentTab,16,67,91,34);Place(likedTab,113,67,115,34);Place(queueTab,234,67,78,34);
            Place(libraryTitle,16,124,w-32,33);Place(libraryRows,10,166,w-20,h-175);};
    }
    void FillLibrary(){RenderLibrary();}
    void SetList(ListView list,Control parent,string name){
        parent.Controls.Add(list);list.View=View.Details;list.HeaderStyle=ColumnHeaderStyle.None;list.Columns.Add("Track");
        list.FullRowSelect=true;list.MultiSelect=false;list.HideSelection=false;list.BorderStyle=BorderStyle.None;list.BackColor=Theme.Ink;
        list.ForeColor=Theme.Paper;list.AccessibleName=name;list.SmallImageList=new ImageList{ImageSize=new Size(1,54)};list.OwnerDraw=true;
        Action fitColumn=delegate{if(list.IsHandleCreated)list.Columns[0].Width=list==results?Math.Max(1,resultClip.ClientSize.Width-2):Math.Max(1,list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-3);};
        list.Resize+=delegate{fitColumn();};list.HandleCreated+=delegate{fitColumn();};list.VisibleChanged+=delegate{if(list.Visible)fitColumn();};
        ListViewItem hot=null,leaving=null;
        Action<ListViewItem> repaint=delegate(ListViewItem item){if(item!=null&&item.ListView==list&&list.Visible)list.Invalidate(item.Bounds);};
        var highlight=new Motion(delegate{repaint(hot);repaint(leaving);});
        Action<ListViewItem> hover=delegate(ListViewItem next){
            if(next==hot)return;var older=leaving;leaving=hot;hot=next;repaint(older);highlight.To(1,145,true);
        };
        list.MouseMove+=delegate(object sender,MouseEventArgs e){hover(list.GetItemAt(e.X,e.Y));};
        list.MouseLeave+=delegate{hover(null);};list.Disposed+=delegate{highlight.Dispose();};
        list.DrawSubItem+=delegate(object s,DrawListViewSubItemEventArgs e){if(e.ColumnIndex!=0)return;var t=(Track)e.Item.Tag;FetchArtwork(t);var r=e.Bounds;
            DrawRefinedRow(e.Graphics,r,t,list,e.ItemIndex,e.Item.Selected,e.Item==hot?highlight.Value:e.Item==leaving?1-highlight.Value:0);

        };
        bool playPointer=false;list.MouseDown+=delegate(object sender,MouseEventArgs e){playPointer=RowPlayHit(list,e.Location)!=null;};list.KeyDown+=delegate{playPointer=false;};list.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left&&e.Clicks==1)PlayRowButton(list,e.Location);};list.ItemActivate+=delegate{if(!playPointer)Selected(list,true);};
    }
    ListViewItem RowPlayHit(ListView list,Point point){var item=list.GetItemAt(point.X,point.Y);if(item==null||!string.IsNullOrEmpty(((Track)item.Tag).Kind))return null;var r=item.Bounds;return new Rectangle(r.X+8,r.Y+(r.Height-28)/2,28,28).Contains(point)?item:null;}
    void PlayRowButton(ListView list,Point point){var item=RowPlayHit(list,point);if(item==null)return;var t=(Track)item.Tag;if(starting!=null&&Key(t)==Key(Current))return;if(active&&Key(t)==Key(Current)){if(audio.State==4)Toggle();return;}if(list==queueView)Play(item.Index);else Add(t,true);}
    void Selected(ListView list,bool play){if(list.SelectedItems.Count==0)return;var chosen=(Track)list.SelectedItems[0].Tag;if(!string.IsNullOrEmpty(chosen.Kind)){OpenCollection(chosen);return;}if(list==queueView)Play(list.SelectedIndices[0]);else Add((Track)list.SelectedItems[0].Tag,play);}
    Bitmap detailLayer;Image layerArt;
    void PaintPlayerSurface(Graphics g,Rectangle r){
        if(r.Width<2||r.Height<2)return;
        var art=Cover(Current);
        if(detailLayer==null||detailLayer.Size!=r.Size||layerArt!=art){
            if(detailLayer!=null)detailLayer.Dispose();
            detailLayer=new Bitmap(r.Width,r.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);layerArt=art;
            using(var canvas=Graphics.FromImage(detailLayer)){
                canvas.SmoothingMode=SmoothingMode.AntiAlias;canvas.InterpolationMode=InterpolationMode.HighQualityBicubic;canvas.PixelOffsetMode=PixelOffsetMode.HighQuality;
                DrawPlayerSurface(canvas,r);
            }
        }
        g.DrawImageUnscaled(detailLayer,0,0);
    }
    void DrawPlayerSurface(Graphics g,Rectangle r){PaintFocusedPlayer(g,r);}
    void BuildPlayer(){
        detail.PaintSurface=PaintPlayerSurface;detail.Disposed+=delegate{if(detailLayer!=null)detailLayer.Dispose();foreach(var frame in immersiveFrames)frame.Pixels.Dispose();immersiveFrames.Clear();};
        var close=Pill(detail,"⌄",delegate{ShowPage("Home");});var menu=Pill(detail,"•••",delegate{ShowTrackMenu(Current,detail,new Point(detail.Width-210,53));});
        detail.Controls.Add(now);detail.Controls.Add(status);now.Font=Theme.Font(18,true);now.BackColor=status.BackColor=Color.Transparent;
        now.Text=picks[2].Title;status.Text=picks[2].Artist;status.ForeColor=Theme.Paper;
        var heart=Make(detail,"Like song",delegate{Track t=Current;int i=liked.FindIndex(x=>Key(x)==Key(t));if(i<0){MarkLibraryAdded(t);liked.Add(t);}else liked.RemoveAt(i);SaveLibrary();if(i<0)UpdatePicks(t);RefreshSurfaces();},delegate(Graphics g,Rectangle r){Glyph(g,liked.Exists(x=>Key(x)==Key(Current))?"\uEB52":"\uEB51",r,Theme.Paper,18);});
        ConfigureMeter(position,1000,"Track position");position.Enabled=false;detail.Controls.Add(position);position.BackColor=Color.FromArgb(12,12,14);
        position.MouseDown+=delegate{dragging=true;};position.MouseUp+=delegate{dragging=false;Seek();};position.KeyUp+=delegate{Seek();};
        clock.BackColor=Color.Transparent;clock.ForeColor=Theme.Paper;clock.Text="0:00 / 0:00";detail.Controls.Add(clock);
        var prev=new SoftButton("Previous",delegate{Step(-1);},false,true);var toggle=new SoftButton("Play",Toggle,true,true);var next=new SoftButton("Next",delegate{Step(1);},false,true);
        detail.Controls.Add(prev);detail.Controls.Add(toggle);detail.Controls.Add(next);detail.BackColor=Color.FromArgb(12,12,14);
        var mix=Make(detail,"Shuffle",delegate{shuffle=!shuffle;detail.Invalidate(true);},delegate(Graphics g,Rectangle r){Glyph(g,"\uE8B1",r,shuffle?Theme.Peach:Theme.Muted,15);});
        var loop=Make(detail,"Repeat track",delegate{repeat=!repeat;detail.Invalidate(true);},delegate(Graphics g,Rectangle r){Glyph(g,"\uE8ED",r,repeat?Theme.Peach:Theme.Muted,15);});
        stationButton=Make(detail,"Song radio",StartStation,delegate(Graphics g,Rectangle r){
            Fill(g,r,r.Height/2,blue);TextAt(g,stationButton.Text,r,10,Theme.Peach,true,true);});
        var lyricsLink=Pill(detail,"Lyrics",delegate{ShowPage("Lyrics");});
        var queueLink=Pill(detail,"☰   Queue",delegate{ShowPage("Queue");});
        detail.Resize+=delegate{int w=detail.Width,h=detail.Height,y=Math.Min(h-213,(int)(h*.65));Place(close,16,16,40,36);Place(menu,w-59,16,43,36);
                        if(LandscapePlayer(detail.ClientRectangle)){
                detail.BackColor=Theme.Ink;var cover=AlbumBounds(detail.ClientRectangle);
                int x=cover.Right+25,width=w-x-24,mid=x+width/2,top=h/2-116;
                Place(now,x,top,width-42,55);Place(status,x,top+55,width,34);Place(heart,w-62,top+4,36,36);
                Place(position,x,top+98,width,26);Place(clock,x,top+126,width,22);
                Place(prev,mid-90,top+166,46,42);Place(toggle,mid-27,top+158,54,54);Place(next,mid+43,top+166,46,42);
                Place(mix,x,top+222,36,36);Place(loop,w-59,top+222,36,36);
                Place(lyricsLink,x,h-58,88,35);Place(queueLink,x+98,h-58,82,35);Place(stationButton,x+188,h-58,Math.Min(106,width-188),35);return;
            }
            detail.BackColor=Color.FromArgb(12,12,14);Place(now,22,y,w-83,43);Place(status,22,y+44,w-44,33);Place(heart,w-52,y,39,39);
            Place(position,16,y+83,w-32,23);Place(clock,22,y+109,w-44,20);
            Place(prev,w/2-103,h-91,53,43);Place(toggle,w/2-28,h-100,56,56);Place(next,w/2+50,h-91,53,43);Place(mix,10,h-88,40,38);Place(loop,w-50,h-88,40,38);Place(lyricsLink,14,h-36,76,29);Place(queueLink,w-100,h-36,92,29);Place(stationButton,w/2-51,h-36,102,29);};
        ConfigureMeter(volume,100,"Volume");volume.Value=75;volume.ValueChanged+=delegate{audio.Volume(volume.Value);if(nextAudio!=null&&nextAudio.Managed)nextAudio.Volume(volume.Value);if(outgoing!=null&&outgoing.Managed)outgoing.Volume(volume.Value);};
        timer.Tick+=delegate{if(toggle.Text!=pause.Text)toggle.Text=pause.Text;stationButton.Visible=!string.IsNullOrEmpty(Current.Id);};
    }
    static void ConfigureMeter(TrackBar t,int max,string name){t.Maximum=max;t.AccessibleName=name;}
    void BuildQueue(){
        queuePage.BackColor=Color.FromArgb(81,82,85);
        queuePage.PaintSurface=delegate(Graphics g,Rectangle r){
            if(r.Width<2||r.Height<2)return;if(Wide){g.Clear(Theme.Card);return;}
            using(var grad=new LinearGradientBrush(r,Color.FromArgb(132,132,135),Color.FromArgb(55,56,60),90f))g.FillRectangle(grad,r);
            Fill(g,new Rectangle(6,69,r.Width-12,r.Height-61),22,Color.FromArgb(82,83,87));
        };
        var bar=Make(queuePage,"Back to now playing",delegate{ShowPage("Player");},delegate(Graphics g,Rectangle r){
            Fill(g,r,25,Color.Black);Glyph(g,"\uE70D",new Rectangle(7,0,42,r.Height),Theme.Paper,13);
            Artwork(g,Cover(Current),new Rectangle(52,8,r.Height-16,r.Height-16),r.Height/2);
            TextAt(g,Current.Title,new Rectangle(103,4,r.Width-125,28),11,Theme.Paper,true);
            TextAt(g,Current.Artist,new Rectangle(103,30,r.Width-125,19),9,Theme.Muted);
        });
        queuePage.Controls.Add(queueTitle);queueTitle.Font=Theme.Font(12,true);queueTitle.Text="Queue";queueTitle.BackColor=Color.Transparent;
        queuePage.Controls.Add(queueEmpty);queueEmpty.Text="Your queue is empty.";queueEmpty.BackColor=Color.Transparent;queueEmpty.ForeColor=Theme.Paper;
        SetList(queueView,queuePage,"Play queue");EnableQueueReorder();queueView.BackColor=Color.FromArgb(82,83,87);ScrollVisible(queueView,false);
        queuePage.Resize+=delegate{int w=queuePage.Width,h=queuePage.Height;if(Wide){bar.Visible=false;queueView.BackColor=Theme.Card;Place(queueTitle,18,14,w-36,38);Place(queueEmpty,18,67,w-36,36);Place(queueView,8,66,w-16,h-78);return;}bar.Visible=true;queueView.BackColor=Color.FromArgb(82,83,87);Place(bar,18,8,w-36,52);Place(queueTitle,18,76,w-36,29);Place(queueEmpty,18,115,w-36,36);Place(queueView,12,110,w-24,h-124);};
    }
}