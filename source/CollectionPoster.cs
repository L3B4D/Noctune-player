using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
internal partial class Player {
    Bitmap collectionBackdrop;Image collectionBackdropSource;
    bool PosterCollection {get{return displayedCollection!=null&&(displayedCollection.Kind=="Album"||displayedCollection.Kind=="Playlist"||displayedCollection.Kind=="Local playlist");}}
    void PaintCollectionPoster(Graphics g,Rectangle r){
        var art=Cover(displayedCollection);int height=Math.Min(r.Height,300);if(r.Width<2||height<2)return;
        if(collectionBackdrop==null||collectionBackdrop.Size!=new Size(r.Width,height)||collectionBackdropSource!=art){
            if(collectionBackdrop!=null)collectionBackdrop.Dispose();collectionBackdrop=new Bitmap(r.Width,height,PixelFormat.Format32bppPArgb);collectionBackdropSource=art;
            using(var canvas=Graphics.FromImage(collectionBackdrop))using(var small=new Bitmap(112,56)){
                using(var sg=Graphics.FromImage(small)){sg.InterpolationMode=InterpolationMode.HighQualityBicubic;float scale=Math.Max(112f/art.Width,56f/art.Height);float w=art.Width*scale,h=art.Height*scale;sg.DrawImage(art,(112-w)/2,(56-h)/2,w,h);}
                canvas.InterpolationMode=InterpolationMode.HighQualityBicubic;canvas.DrawImage(small,new Rectangle(-6,-6,r.Width+12,height+12));
                using(var tint=new SolidBrush(Color.FromArgb(95,24,42,46)))canvas.FillRectangle(tint,0,0,r.Width,height);
                using(var fade=new LinearGradientBrush(new Rectangle(0,0,r.Width,height),Color.FromArgb(65,Theme.Ink),Theme.Ink,90))canvas.FillRectangle(fade,0,0,r.Width,height);
            }
        }
        g.DrawImageUnscaled(collectionBackdrop,0,0);
        int x=r.Width>=700?248:18,y=r.Width>=700?208:CollectionHeroHeight()+9;
        var box=new Rectangle(x,y,Math.Max(1,r.Width-x-24),46);
        using(var path=Theme.Round(box,14))using(var fill=new LinearGradientBrush(box,Color.FromArgb(125,100,116,119),Color.FromArgb(125,40,49,53),90))using(var pen=new Pen(Color.FromArgb(85,190,207,213))){g.FillPath(fill,path);g.DrawPath(pen,path);}
    }
    void LayoutCollectionPoster(){
        if(!PosterCollection)return;int w=collectionPage.Width,h=collectionPage.Height;if(w<700)return;
        Place(collectionBack,22,12,83,31);Place(collectionSearch,120,12,Math.Min(340,w-280),32);
        Place(collectionCover,30,61,190,190);Place(collectionKind,258,66,w-290,22);
        SetHeadingSize(collectionTitle,32);Place(collectionTitle,254,91,w-282,59);
        Place(collectionArtist,258,151,w-290,26);Place(collectionNote,258,178,w-290,24);
        foreach(Control c in collectionPage.Controls)if(c.Text=="Play all")Place(c,260,214,91,34);
        Place(albumShuffle,361,214,36,34);Place(albumSave,407,214,91,34);Place(albumMore,508,214,42,34);
        int top=273;if(editingPlaylist!=null){Place(duplicatesButton,30,top,166,32);Place(collectionRemove,208,top,166,32);top+=42;}
        Place(collectionColumns,24,top,w-48,27);Place(collectionRows,18,top+30,w-36,Math.Max(70,h-top-42));
        int rowHeight=44;if(collectionRows.SmallImageList.ImageSize.Height!=rowHeight)collectionRows.SmallImageList.ImageSize=new Size(1,rowHeight);
    }
}
