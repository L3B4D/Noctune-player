using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

internal partial class Player {
    sealed class AmbientFrame {internal Image Source;internal Bitmap Pixels;}
    readonly LinkedList<AmbientFrame> ambientFrames=new LinkedList<AmbientFrame>();
    readonly Dictionary<Image,Color> artworkTones=new Dictionary<Image,Color>();
    ArtButton collectionBack,collectionArtist;Label collectionKind;Surface collectionColumns;
    readonly Stack<Track> collectionHistory=new Stack<Track>();bool navigatingCollectionBack;string collectionOrigin="Library";
    ArtButton[] homeShortcuts;Label homeShortcutTitle;
    Color ArtworkTone(Image image){
        Color tone;if(artworkTones.TryGetValue(image,out tone))return tone;
        using(var sample=new Bitmap(12,12)){using(var g=Graphics.FromImage(sample))g.DrawImage(image,0,0,12,12);int red=0,green=0,blueValue=0,count=0;for(int y=0;y<12;y++)for(int x=0;x<12;x++){var p=sample.GetPixel(x,y);if(p.GetBrightness()<.1f||p.GetBrightness()>.85f)continue;red+=p.R;green+=p.G;blueValue+=p.B;count++;}tone=count>0?Color.FromArgb(red/count,green/count,blueValue/count):Color.FromArgb(82,106,118);}
        if(artworkTones.Count>128)artworkTones.Clear();artworkTones[image]=tone;return tone;
    }
    void DrawAtmosphere(Graphics g,Rectangle rect,Image source){
        if(rect.Width<2||rect.Height<2)return;
        Bitmap pixels=null;foreach(var frame in ambientFrames)if(frame.Source==source&&frame.Pixels.Size==rect.Size){pixels=frame.Pixels;break;}
        if(pixels==null){pixels=new Bitmap(rect.Width,rect.Height,PixelFormat.Format32bppPArgb);using(var canvas=Graphics.FromImage(pixels))using(var small=new Bitmap(24,24)){
            using(var sg=Graphics.FromImage(small))sg.DrawImage(source,0,0,24,24);
            canvas.Clear(Theme.Ink);canvas.InterpolationMode=InterpolationMode.HighQualityBicubic;canvas.DrawImage(small,new Rectangle(0,0,rect.Width,rect.Height));
            using(var shade=new LinearGradientBrush(new Rectangle(Point.Empty,rect.Size),Color.FromArgb(220,Theme.Ink),Color.FromArgb(253,Theme.Ink),90))canvas.FillRectangle(shade,0,0,rect.Width,rect.Height);
        }ambientFrames.AddFirst(new AmbientFrame{Source=source,Pixels=pixels});while(ambientFrames.Count>3){ambientFrames.Last.Value.Pixels.Dispose();ambientFrames.RemoveLast();}}
        g.DrawImageUnscaled(pixels,rect.Location);
    }
    void PaintGlass(Graphics g,Rectangle rect,int radius,Color tone){
        if(rect.Width<2||rect.Height<2)return;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=Theme.Round(rect,radius))using(var fill=new LinearGradientBrush(rect,Motion.Mix(Color.FromArgb(31,36,40),tone,.14f),Color.FromArgb(20,24,27),65))using(var edge=new Pen(Color.FromArgb(67,77,83))){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
    }
    void BuildVisualRefresh(){
        BuildAlbumActions();BuildCollectionGallery();collectionKind=LabelOn(collectionPage,"",10);collectionKind.ForeColor=Theme.Peach;
        collectionBack=Pill(collectionPage,"‹  Back",BackFromCollection);
        collectionArtist=Make(collectionPage,"Artist",OpenCollectionArtist,delegate(Graphics g,Rectangle r){TextAt(g,CollectionArtistName(),r,12,Theme.Paper,true);});
        collectionColumns=new Surface{BackColor=Theme.Ink};collectionPage.Controls.Add(collectionColumns);collectionColumns.PaintSurface=delegate(Graphics g,Rectangle r){TextAt(g,"#",new Rectangle(12,0,30,r.Height),9,Theme.Muted);TextAt(g,"TITLE",new Rectangle(94,0,Math.Max(1,r.Width-310),r.Height),9,Theme.Muted);if(r.Width>=700)TextAt(g,"ARTIST",new Rectangle((int)(r.Width*.65),0,150,r.Height),9,Theme.Muted);TextAt(g,"TIME",new Rectangle(r.Width-158,0,65,r.Height),9,Theme.Muted,false,true);using(var pen=new Pen(Theme.Edge))g.DrawLine(pen,6,r.Height-1,r.Width-6,r.Height-1);};
        collectionPage.PaintSurface=delegate(Graphics g,Rectangle r){if(displayedCollection==null)return;if(PosterCollection){PaintCollectionPoster(g,r);return;}int hero=CollectionHeroHeight();DrawAtmosphere(g,new Rectangle(0,0,r.Width,Math.Min(r.Height,hero+145)),Cover(displayedCollection));PaintGlass(g,new Rectangle(18,hero+9,Math.Max(1,r.Width-36),51),14,ArtworkTone(Cover(displayedCollection)));};
        collectionPage.Resize+=delegate{LayoutRefinedCollection();};collectionPage.VisibleChanged+=delegate{if(collectionPage.Visible)LayoutRefinedCollection();};
        homeShortcutTitle=LabelOn(home,"From your library",17);homeShortcuts=new ArtButton[3];string[] names={"Liked songs","Your playlists","Recently played"},icons={"\uEB51","\uE8F1","\uE81C"};
        for(int i=0;i<3;i++){int k=i;homeShortcuts[k]=Make(home,names[k],delegate{if(k==1)ShowPage("Collections");else{listMode=k==0?"Liked songs":"Recent";ShowPage("Library");}},delegate(Graphics g,Rectangle r){PaintGlass(g,new Rectangle(1,1,r.Width-3,r.Height-3),13,Color.FromArgb(50,64,74));Glyph(g,icons[k],new Rectangle(14,18,32,32),Theme.Peach,19);TextAt(g,names[k],new Rectangle(58,9,r.Width-72,27),11,Theme.Paper,true);TextAt(g,k==0?liked.Count+" saved songs":k==1?featuresState.Playlists.Count+" playlists":recent.Count+" tracks",new Rectangle(58,36,r.Width-72,23),9,Theme.Muted);});}
        home.Resize+=delegate{if(!Wide){homeShortcutTitle.Visible=false;foreach(var tile in homeShortcuts)tile.Visible=false;}};
        home.PaintSurface=delegate(Graphics g,Rectangle r){using(var brush=new LinearGradientBrush(new Rectangle(0,0,Math.Max(1,r.Width),Math.Max(1,Math.Min(260,r.Height))),Color.FromArgb(18,25,30),Theme.Ink,90))g.FillRectangle(brush,0,0,r.Width,Math.Min(260,r.Height));};
        queuePage.PaintSurface=delegate(Graphics g,Rectangle r){g.Clear(Theme.Ink);if(r.Width>0&&r.Height>0)DrawAtmosphere(g,new Rectangle(0,0,r.Width,Math.Min(155,r.Height)),Cover(Current));};queueTitle.ForeColor=Theme.Paper;
        searchPage.PaintSurface=delegate(Graphics g,Rectangle r){PaintPageHeading(g,r);};library.PaintSurface=delegate(Graphics g,Rectangle r){PaintPageHeading(g,r);};discoverPage.PaintSurface=delegate(Graphics g,Rectangle r){PaintPageHeading(g,r);};lyricsPage.PaintSurface=PaintImmersiveBackdrop;
        settingsPage.PaintSurface=delegate(Graphics g,Rectangle r){PaintPageHeading(g,r);};collectionsHub.PaintSurface=delegate(Graphics g,Rectangle r){PaintPageHeading(g,r);};
        if(barTime!=null)barTime.BackColor=Color.Transparent;
        FormClosed+=delegate{if(collectionBackdrop!=null)collectionBackdrop.Dispose();foreach(var frame in ambientFrames)frame.Pixels.Dispose();ambientFrames.Clear();artworkTones.Clear();};
        layoutHome();LayoutRefinedCollection();
    }
    void PaintPageHeading(Graphics g,Rectangle r){if(r.Width<1||r.Height<1)return;using(var brush=new LinearGradientBrush(new Rectangle(0,0,r.Width,160),Color.FromArgb(24,32,37),Theme.Ink,90))g.FillRectangle(brush,0,0,r.Width,Math.Min(160,r.Height));}
    void LayoutHomeShortcuts(int y){if(homeShortcuts==null)return;homeShortcutTitle.Visible=Wide;int w=home.Width,m=22,gap=16,slot=(w-2*m-gap*2)/3;Place(homeShortcutTitle,m,y,w-2*m,30);for(int i=0;i<3;i++){homeShortcuts[i].Visible=Wide;Place(homeShortcuts[i],m+i*(slot+gap),y+40,slot,72);}}
    int CollectionHeroHeight(){return collectionPage.Width>=700?Math.Min(300,Math.Max(260,collectionPage.Height/3+30)):collectionPage.Height<520?170:collectionPage.Height<650?205:240;}
    void SetHeadingSize(Label label,float size){if(Math.Abs(label.Font.Size-size)<.1f)return;var old=label.Font;label.Font=Theme.Font(size,true);old.Dispose();}
    void LayoutRefinedCollection(){
        if(collectionSearch==null||collectionKind==null)return;int w=collectionPage.Width,h=collectionPage.Height;bool wide=w>=700,artist=displayedCollection!=null&&displayedCollection.Kind=="Artist",local=editingPlaylist!=null;int hero=CollectionHeroHeight(),size=wide?hero-72:Math.Min(112,hero-64);
        Place(collectionBack,20,14,83,31);Place(collectionCover,24,64,size,size);int x=wide?size+48:154;
        collectionKind.Text=displayedCollection==null?"COLLECTION":(displayedCollection.Kind??"COLLECTION").ToUpperInvariant()+(displayedCollection.Year>0?"  ·  "+displayedCollection.Year:"");Place(collectionKind,x,wide?70:60,w-x-24,23);
        SetHeadingSize(collectionTitle,wide?32:22);Place(collectionTitle,x,wide?100:86,w-x-24,wide?82:48);
        Place(collectionArtist,x,wide?hero-76:hero-65,w-x-24,27);Place(collectionNote,x,hero-35,w-x-24,27);
        artistFollow.Visible=artist;artistSection.Visible=artist;artistDescription.Visible=artist;duplicatesButton.Visible=collectionRemove.Visible=local;
        int by=hero+17;foreach(Control c in collectionPage.Controls){if(c.Text=="Play all")Place(c,28,by,91,35);else if(c.Text=="Save as playlist")Place(c,129,by,131,35);else if(c==collectionManage)Place(c,270,by,Math.Max(58,Math.Min(90,w-300)),35);}
        bool album=displayedCollection!=null&&displayedCollection.Kind!="Artist";albumShuffle.Visible=albumMore.Visible=album&&!IsMoodCollection;albumSave.Visible=album&&!local;albumShuffle.Enabled=collectionContents.Any(t=>string.IsNullOrEmpty(t.Kind));collectionManage.Visible=false;foreach(Control c in collectionPage.Controls){if(c.Text=="Save as playlist")c.Visible=!album;if(c.Text=="Play all")c.Visible=!IsMoodCollection;}Place(albumShuffle,129,by,36,35);Place(albumSave,IsMoodCollection?28:175,by,91,35);Place(albumMore,276,by,42,35);if(artist)Place(artistFollow,w-178,wide?by:13,150,35);
        int contentY=hero+74;artistDescription.Visible=artist&&(wide||h>=620);if(artistDescription.Visible){Place(artistDescription,24,contentY,w-48,44);contentY+=53;}
        Place(collectionSearch,24,contentY,w-(artist?185:48),40);if(artist)Place(artistSection,w-149,contentY,125,40);
        contentY+=51;if(local){int half=Math.Min(166,(w-60)/2);Place(duplicatesButton,24,contentY,half,32);Place(collectionRemove,36+half,contentY,half,32);contentY+=43;}
        Place(collectionRetry,Math.Max(24,w-110),15,86,31);Place(collectionColumns,18,contentY,w-36,27);Place(collectionRows,12,contentY+31,w-24,Math.Max(70,h-contentY-43));
        collectionColumns.Visible=!IsMoodCollection;ScrollVisible(collectionRows,!IsMoodCollection);ScrollVisible(collectionGallery,IsMoodCollection);Place(collectionGallery,18,contentY,w-36,Math.Max(70,h-contentY-12));LayoutCollectionCards();if(!PosterCollection||w<700){if(collectionRows.SmallImageList.ImageSize.Height!=54)collectionRows.SmallImageList.ImageSize=new Size(1,54);}LayoutCollectionPoster();collectionPage.Invalidate();
    }
    string CollectionArtistName(){if(displayedCollection==null)return "";if(displayedCollection.Kind=="Artist")return "Artist · YouTube Music";var song=collectionContents.FirstOrDefault(t=>string.IsNullOrEmpty(t.Kind)&&!string.IsNullOrEmpty(t.Artist));string name=song==null?displayedCollection.Artist:song.Artist;int year;if(string.IsNullOrWhiteSpace(name)||int.TryParse(name,out year))return "YouTube Music";return name;}
    void OpenCollectionArtist(){if(displayedCollection==null||displayedCollection.Kind=="Artist")return;var song=collectionContents.FirstOrDefault(t=>!string.IsNullOrEmpty(t.ArtistId));if(song!=null)OpenCollection(new Track{Kind="Artist",BrowseId=song.ArtistId,Title=song.Artist,Artwork=song.Artwork});}
    void RememberCollectionNavigation(Track next){if(navigatingCollectionBack){navigatingCollectionBack=false;return;}if(page=="Collection"&&displayedCollection!=null&&Key(displayedCollection)!=Key(next))collectionHistory.Push(displayedCollection);else if(page!="Collection"){collectionHistory.Clear();collectionOrigin=page;}}
    void BackFromCollection(){if(collectionHistory.Count>0){navigatingCollectionBack=true;OpenCollection(collectionHistory.Pop());}else ShowPage(collectionOrigin=="Collection"?"Library":collectionOrigin);}
    void DrawRefinedRow(Graphics g,Rectangle r,Track track,ListView list,int row,bool selected,float hover){
        bool current=(list==queueView&&row==index)||(list==collectionRows&&active&&Key(track)==Key(Current));g.SmoothingMode=SmoothingMode.AntiAlias;using(var brush=new SolidBrush(Theme.Ink))g.FillRectangle(brush,r);
        if(selected||current||hover>.01f)Fill(g,new Rectangle(r.X+3,r.Y+2,r.Width-7,r.Height-4),8,Motion.Mix(Theme.Ink,Color.FromArgb(42,53,60),selected||current?1:hover*.7f));
        bool table=r.Width>=700;int left=r.X+8;
        if(string.IsNullOrEmpty(track.Kind)){var button=new Rectangle(left,r.Y+(r.Height-28)/2,28,28);Fill(g,button,14,hover>.01f?Theme.Edge:Theme.Card);Glyph(g,"\uE768",button,current?Theme.Peach:Theme.Paper,11);left+=35;}else if(list==collectionRows||list==queueView){TextAt(g,(row+1).ToString(),new Rectangle(left,r.Y,26,r.Height),9,Theme.Muted,false,true);left+=35;}
        int artSize=list==collectionRows&&PosterCollection&&table?32:44;DrawRowArtwork(g,track,new Rectangle(left,r.Y+(r.Height-artSize)/2,artSize,artSize));int tx=left+artSize+13;int right=r.Right-20;if(table&&list==collectionRows&&string.IsNullOrEmpty(track.Kind)){Glyph(g,liked.Exists(t=>Key(t)==Key(track))?"\uEB52":"\uEB51",new Rectangle(right-62,r.Y,30,r.Height),Theme.Muted,12);TextAt(g,"•••",new Rectangle(right-28,r.Y,28,r.Height),10,Theme.Muted,false,true);right-=80;}
        if(table){int artistX=r.X+(int)(r.Width*.65);TextAt(g,track.Title,new Rectangle(tx,r.Y,Math.Max(1,artistX-tx-18),r.Height),11,current?Theme.Peach:Theme.Paper,true);TextAt(g,track.Artist,new Rectangle(artistX,r.Y,Math.Max(1,right-artistX-84),r.Height),10,Theme.Muted);}
        else{TextAt(g,track.Title,new Rectangle(tx,r.Y+5,Math.Max(1,right-tx-52),25),11,current?Theme.Peach:Theme.Paper,true);TextAt(g,track.Artist,new Rectangle(tx,r.Y+29,Math.Max(1,right-tx-52),20),9,Theme.Muted);}
        string trailing=track.DurationSeconds>0?Stamp(track.DurationSeconds*1000L):!string.IsNullOrEmpty(track.Kind)?track.Kind:current?"Playing":list==queueView&&row==index+1?"Next":"";
        TextAt(g,trailing,new Rectangle(right-64,r.Y,64,r.Height),9,Theme.Muted,false,true);
    }
}
