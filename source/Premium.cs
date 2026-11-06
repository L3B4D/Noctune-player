using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal partial class Player {
    Label welcome, welcomeNote, nextHeading;
    ArtButton mixHero;
    readonly ArtButton[] nextTracks=new ArtButton[3];
    readonly Surface discovery=new Surface();
    Control searchPlay,searchAdd;
    Action layoutDiscovery;
    bool showingResults;
    readonly System.Collections.Generic.List<string> recentQueries=new System.Collections.Generic.List<string>();
    readonly string queryHistoryPath=System.IO.Path.Combine(Catalog.Root,"data","searches.json");
    void RememberSearch(){
        string term=query.Text.Trim();recentQueries.RemoveAll(x=>string.Equals(x,term,StringComparison.OrdinalIgnoreCase));recentQueries.Insert(0,term);
        if(recentQueries.Count>4)recentQueries.RemoveRange(4,recentQueries.Count-4);
        try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(queryHistoryPath));System.IO.File.WriteAllText(queryHistoryPath,Catalog.Json().Serialize(recentQueries));}catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
    }

    void RoundPlayerBar(){
        if(mini.Width<4||mini.Height<4)return;
        var old=mini.Region;using(var shape=Theme.Round(mini.ClientRectangle,mini.Height/2))mini.Region=new Region(shape);
        if(old!=null)old.Dispose();
    }
    void PaintFloatingBar(Graphics g,Rectangle r){
        if(r.Width<2||r.Height<2)return;g.SmoothingMode=SmoothingMode.AntiAlias;
        var outer=new Rectangle(1,1,r.Width-3,r.Height-3);
        using(var shape=Theme.Round(outer,outer.Height/2)){
            using(var ground=new SolidBrush(Color.FromArgb(31,36,39)))g.FillPath(ground,shape);
            if(glassBackdrop!=null&&!glassBackdropBounds.IsEmpty){var state=g.Save();g.SetClip(shape,CombineMode.Intersect);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(glassBackdrop,new Rectangle(glassBackdropBounds.Left-mini.Left,glassBackdropBounds.Top-mini.Top,glassBackdropBounds.Width,glassBackdropBounds.Height));g.Restore(state);}
            using(var material=new LinearGradientBrush(outer,Color.FromArgb(125,58,64,67),Color.FromArgb(150,31,36,39),90))g.FillPath(material,shape);
            using(var edge=new Pen(Color.FromArgb(68,224,232,235),1))g.DrawPath(edge,shape);
        }
        var inner=new Rectangle(2,2,r.Width-5,r.Height-5);
        using(var shape=Theme.Round(inner,inner.Height/2))using(var light=new LinearGradientBrush(inner,Color.FromArgb(30,255,255,255),Color.FromArgb(0,255,255,255),90))using(var edge=new Pen(light,1))g.DrawPath(edge,shape);
    }
    void DrawCompactTrack(Graphics g,Rectangle r,Track t,float h){
        Fill(g,new Rectangle(0,0,r.Width,r.Height-2),12,Motion.Mix(Theme.Card,Theme.Edge,h*.6f));
        Artwork(g,Cover(t),new Rectangle(9,9,42,42),8);
        TextAt(g,t.Title,new Rectangle(61,7,r.Width-71,24),10,Theme.Paper,true);
        TextAt(g,t.Artist,new Rectangle(61,31,r.Width-71,20),9,Theme.Muted);
        if(h>.01f){var badge=new Rectangle(16,16,28,28);Fill(g,badge,14,Color.FromArgb((int)(235*h),Theme.Paper));Glyph(g,"\uE768",badge,Theme.Ink,10);}
    }
    void DrawAlbumTile(Graphics g,Rectangle r,Track t,float h){
        int size=Math.Min(r.Width-4,r.Height-49);var tile=new Rectangle(2,6-(int)(h*5),size,size);
        Artwork(g,Cover(t),tile,12);HoverFinish(g,tile,12,h,false);
        TextAt(g,t.Title,new Rectangle(3,size+10,r.Width-6,22),11,Theme.Paper,true);
        TextAt(g,t.Artist,new Rectangle(3,size+32,r.Width-6,18),9,Theme.Muted);
    }
    Track UpNext(int i){int n=Math.Max(0,index+1)+i;return n<queue.Count?queue[n]:HomePicks[(hero+i)%HomePicks.Length];}
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    void DarkTitleBar(){try{int enabled=1;if(DwmSetWindowAttribute(Handle,20,ref enabled,4)!=0)DwmSetWindowAttribute(Handle,19,ref enabled,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
    void BuildPremium(){
        HandleCreated+=delegate{DarkTitleBar();};if(IsHandleCreated)DarkTitleBar();
        welcome=LabelOn(home,DateTime.Now.Hour<12?"Good morning":DateTime.Now.Hour<18?"Good afternoon":"Good evening",25);welcomeNote=LabelOn(home,"Rediscover favorites. Find your next repeat.",10);welcomeNote.ForeColor=Theme.Muted;
        mixHero=Make(home,"Play your mix",delegate{int first=queue.Count;foreach(var t in HomePicks)Add(t,false);Play(first);},delegate(Graphics g,Rectangle r){
            Fill(g,r,18,Color.FromArgb(23,23,27));
            int d=Math.Min(142,r.Height-24);int x=r.Width-d-16;
            if(r.Width>440){Artwork(g,Cover(HomePicks[0]),new Rectangle(x,12,d,d),12);if(r.Width>620)Artwork(g,Cover(HomePicks[1]),new Rectangle(x-d-10,20,d-16,d-16),12);}
            int tw=r.Width>620?r.Width-d*2-45:r.Width>440?x-40:r.Width-40;
            TextAt(g,"YOUR DAILY SOUNDTRACK",new Rectangle(22,15,tw,21),9,Theme.Muted,true);
            TextAt(g,"Your personal mix",new Rectangle(22,40,tw,37),23,Theme.Paper,true);
            TextAt(g,"Favorites and fresh discoveries, together.",new Rectangle(22,79,tw,25),10,Theme.Muted);
            var button=new Rectangle(22,r.Height-44,113,30);Fill(g,button,15,Theme.Paper);TextAt(g,"▶   Play mix",button,10,Theme.Ink,true,true);
        });
        nextHeading=LabelOn(home,"For your next play",14);
        for(int i=0;i<3;i++){int k=i;nextTracks[i]=Make(home,"Play suggested song",delegate{int n=Math.Max(0,index+1)+k;if(n<queue.Count)Play(n);else Add(UpNext(k),true);},delegate(Graphics g,Rectangle r){DrawCompactTrack(g,r,UpNext(k),nextTracks[k].HoverAmount);});nextTracks[i].AnimateHover=true;}
        try{if(System.IO.File.Exists(queryHistoryPath)){var terms=Catalog.Json().Deserialize<string[]>(System.IO.File.ReadAllText(queryHistoryPath));if(terms!=null)foreach(var term in terms)if(!string.IsNullOrWhiteSpace(term)&&recentQueries.Count<4)recentQueries.Add(term);}}catch(System.IO.IOException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(UnauthorizedAccessException){}
        BuildDiscovery();
        home.Resize+=delegate{if(welcome!=null){welcome.Visible=welcomeNote.Visible=Wide;mixHero.Visible=false;nextHeading.Visible=false;foreach(var c in nextTracks)c.Visible=false;}};
        query.TextChanged+=delegate{if(query.TextLength==0){showingResults=false;UpdateDiscovery();}};
        searchPage.VisibleChanged+=delegate{if(searchPage.Visible)UpdateDiscovery();};
        searchPage.Resize+=delegate{UpdateDiscovery();};
        layoutHome();UpdateDiscovery();
    }
    void LayoutPremiumHome(Control heading,Control keep,Control playAll,Control left,Control right,ArtButton[] chips,ArtButton[] top){
        int w=home.Width,m=22,gap=18,content=w-m*2;
        foreach(var c in top)c.Visible=false;
        if(welcome!=null){welcome.Visible=welcomeNote.Visible=true;mixHero.Visible=nextHeading.Visible=false;foreach(var c in nextTracks)c.Visible=false;Place(welcome,m,8,content,43);Place(welcomeNote,m,52,content,23);}
        int cx=m;foreach(var chip in chips){int cw=TextRenderer.MeasureText(chip.Text,Font).Width+22;chip.Visible=cx+cw<w-180;Place(chip,cx,87,cw,32);cx+=cw+8;}
        int cols=content<700?3:4,slot=(content-gap*(cols-1))/cols;
        Place(heading,m,139,content-210,32);Place(homeReason,m,175,content,23);Place(playAll,w-m-110,139,110,33);Place(left,w-m-188,139,32,33);Place(right,w-m-149,139,32,33);
        int y=homeReason.Text.Length>0?207:185,size=Math.Min(300,slot);
        for(int i=0;i<features.Length;i++){features[i].Visible=i<cols;if(i<cols)Place(features[i],m+i*(slot+gap),y,slot,size+8);}
        int ky=y+size+27;Place(keep,m,ky,content,32);int count=w<900?4:6,sw=(content-gap*(count-1))/count,ss=Math.Min(174,sw);
        for(int i=0;i<shelf.Length;i++){shelf[i].Visible=i<count;if(i<count)Place(shelf[i],m+i*(sw+gap),ky+42,sw,ss+51);}
        int bottom=ky+42+ss+66;LayoutHomeShortcuts(bottom);int needed=bottom+130;homeDesiredHeight=needed;if(homeScroll.AutoScroll&&home.Height!=needed)home.Height=needed;
    }

    readonly Track[] moodCategories=new Track[4];
    readonly string[] moodNames={"Chill","Energize","Focus","Sleep"};
    bool moodLoading;DateTime moodRetryAfter;
    async void LoadMoodCards(){
        if(moodLoading||DateTime.UtcNow<moodRetryAfter||closing)return;
        bool complete=true;foreach(var m in moodCategories)if(m==null||string.IsNullOrEmpty(m.Artwork))complete=false;
        if(complete){foreach(var m in moodCategories)FetchArtwork(m);return;}
        moodLoading=true;moodRetryAfter=DateTime.UtcNow.AddMinutes(1);
        using(var request=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(35))){
            EventHandler stop=delegate{request.Cancel();};Disposed+=stop;
            try{
                var categories=await Catalog.MoodCategories(request.Token);
                if(closing||IsDisposed)return;
                var jobs=new System.Collections.Generic.List<System.Threading.Tasks.Task>();
                for(int i=0;i<moodNames.Length;i++){
                    var mood=categories.Find(x=>string.Equals(x.Title,moodNames[i],StringComparison.OrdinalIgnoreCase));
                    if(mood==null)continue;moodCategories[i]=mood;jobs.Add(LoadMoodArtwork(mood,request.Token));
                }
                await System.Threading.Tasks.Task.WhenAll(jobs);
            }catch(OperationCanceledException){}catch(System.Net.Http.HttpRequestException){}catch(System.IO.IOException){}catch(InvalidOperationException){}catch(ArgumentException){}
            finally{Disposed-=stop;moodLoading=false;}
        }
    }
    async System.Threading.Tasks.Task LoadMoodArtwork(Track mood,System.Threading.CancellationToken token){
        try{await Catalog.ResolveMoodArtwork(mood,token);if(!closing&&!IsDisposed){FetchArtwork(mood);discovery.Invalidate(true);}}
        catch(OperationCanceledException){}catch(System.Net.Http.HttpRequestException){}catch(System.IO.IOException){}catch(InvalidOperationException){}catch(ArgumentException){}
    }
    Image MoodArtwork(int index){
        var mood=moodCategories[index];if(mood==null)return null;
        var art=Cover(mood);return art==images["logo"]?null:art;
    }
    void DrawMoodCard(Graphics g,Rectangle r,string title,string caption,Color accent,int mood,float hover){
        if(r.Width<4||r.Height<4)return;
        var bounds=new Rectangle(1,1,r.Width-3,r.Height-3);
        using(var outline=Theme.Round(bounds,16)){
            var state=g.Save();g.SetClip(outline,CombineMode.Intersect);
            using(var background=new LinearGradientBrush(r,Motion.Mix(accent,Color.FromArgb(70,70,80),hover*.25f),Color.FromArgb(19,19,23),35))g.FillRectangle(background,r);
            // Fixed-size cached artwork: hovering moves covers without resampling them.
            int size=Math.Min(104,Math.Max(70,r.Width/2));
            int x=r.Width-size-12, y=15-(int)(hover*5);
            var artwork=MoodArtwork(mood);
            if(artwork!=null){
                Fill(g,new Rectangle(x-3,y+4,size+6,size+6),12,Color.FromArgb(90,0,0,0));
                Artwork(g,artwork,new Rectangle(x,y,size,size),10);
            }else{
                Fill(g,new Rectangle(x,y,size,size),10,Color.FromArgb(30,255,255,255));
                Glyph(g,new[]{"\uEC46","\uE945","\uE7F4","\uE708"}[mood],new Rectangle(x,y,size,size),Theme.Muted,25);
            }
            using(var shade=new LinearGradientBrush(r,Color.FromArgb(0,0,0,0),Color.FromArgb(245,10,10,13),90))g.FillRectangle(shade,r);
            g.Restore(state);
            using(var edge=new Pen(Motion.Mix(Color.FromArgb(50,50,56),Color.FromArgb(116,116,127),hover)))g.DrawPath(edge,outline);
        }
        TextAt(g,title,new Rectangle(16,r.Height-57,r.Width-55,28),15,Theme.Paper,true);
        TextAt(g,caption,new Rectangle(16,r.Height-29,r.Width-30,20),9,Color.FromArgb(191,191,201));
        var arrow=new Rectangle(r.Width-37,r.Height-55,24,24);
        Fill(g,arrow,12,Color.FromArgb(45+(int)(hover*35),255,255,255));
        Glyph(g,"\uE72A",arrow,Theme.Paper,10);
    }
    void BuildDiscovery(){
        searchPage.Controls.Add(discovery);discovery.BackColor=Theme.Ink;
        var again=LabelOn(discovery,"Jump back in",16);var note=LabelOn(discovery,recent.Count>0?"Your listening history, ready for another spin.":"A few favorites to get you started.",10);note.ForeColor=Theme.Muted;
        var albums=new ArtButton[4];
        for(int i=0;i<4;i++){int k=i;albums[i]=Make(discovery,"Play recent song",delegate{Add(ShelfTrack(k),true);},delegate(Graphics g,Rectangle r){DrawAlbumTile(g,r,ShelfTrack(k),albums[k].HoverAmount);});albums[i].AnimateHover=true;}
        var browse=LabelOn(discovery,"Explore by mood",16);string[] moods=moodNames;
        var tiles=new ArtButton[4];Color[] colors={Color.FromArgb(30,51,54),Color.FromArgb(65,39,29),Color.FromArgb(39,49,37),Color.FromArgb(41,37,59)};
        string[] captions={"Slow down. Tune in.","Turn it up.","Find your flow.","Wind down. Rest easy."};
        for(int i=0;i<4;i++){int k=i;tiles[i]=Make(discovery,moods[i],delegate{if(moodCategories[k]!=null)OpenCollection(moodCategories[k]);else SearchFor(moods[k]+" music");},delegate(Graphics g,Rectangle r){
            DrawMoodCard(g,r,moods[k],captions[k],colors[k],k,tiles[k].HoverAmount);
        });tiles[i].AnimateHover=true;}
        var historyTitle=LabelOn(discovery,"Recent searches",13);
        var historyButtons=new ArtButton[4];
        for(int i=0;i<4;i++){int k=i;historyButtons[i]=Make(discovery,"Recent search",delegate{if(k<recentQueries.Count)SearchFor(recentQueries[k]);},delegate(Graphics g,Rectangle r){Fill(g,r,14,Theme.Card);TextAt(g,k<recentQueries.Count?recentQueries[k]:"",new Rectangle(12,0,r.Width-24,r.Height),10,Theme.Paper,false,true);});}
        layoutDiscovery=delegate{
            int w=discovery.ClientSize.Width;bool history=recentQueries.Count>0;int offset=history?80:0;
            historyTitle.Visible=history;Place(historyTitle,0,0,w,26);
            int hw=Math.Max(60,(w-30)/4);
            for(int i=0;i<4;i++){historyButtons[i].Visible=i<recentQueries.Count;Place(historyButtons[i],i*(hw+10),33,hw,32);}
            Place(again,0,2+offset,w,30);Place(note,0,35+offset,w,23);
            int cols=w<550?2:4,gap=15,slot=(w-gap*(cols-1))/cols,d=Math.Min(166,slot);
            for(int i=0;i<4;i++){albums[i].Visible=i<cols;Place(albums[i],i*(slot+gap),69+offset,slot,d+49);}
            int by=69+d+64+offset;Place(browse,0,by,w,32);
            for(int i=0;i<4;i++){int row=i/cols,col=i%cols;Place(tiles[i],col*(slot+gap),by+45+row*164,slot,150);}
            discovery.AutoScrollMinSize=new Size(0,by+45+(4/cols)*164);
        };discovery.AutoScroll=true;discovery.Resize+=delegate{layoutDiscovery();};
    }
    void ShowSearchResults(){RememberSearch();showingResults=true;UpdateDiscovery();}
    void UpdateDiscovery(){
        if(layoutDiscovery==null)return;
        ScrollVisible(discovery,!showingResults);resultClip.Visible=resultScroll.Visible=showingResults;
        searchPlay.Visible=searchAdd.Visible=showingResults;
        searchStatus.Visible=showingResults;
        Place(discovery,18,177,Math.Max(1,searchPage.Width-38),Math.Max(1,searchPage.Height-187));
        if(discovery.Visible){discovery.Invalidate(true);layoutDiscovery();LoadMoodCards();}
        suggestions.BringToFront();
    }
}
