using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
internal partial class Player {
    readonly LinkedList<AmbientFrame> immersiveFrames=new LinkedList<AmbientFrame>();
    void PaintImmersiveBackdrop(Graphics g,Rectangle r){
        if(r.Width<2||r.Height<2)return;var art=Cover(Current);Bitmap bitmap=null;foreach(var frame in immersiveFrames)if(frame.Source==art&&frame.Pixels.Size==r.Size){bitmap=frame.Pixels;break;}
        if(bitmap==null){bitmap=new Bitmap(r.Width,r.Height,PixelFormat.Format32bppPArgb);using(var c=Graphics.FromImage(bitmap))using(var small=new Bitmap(36,36)){
            using(var low=Graphics.FromImage(small))low.DrawImage(art,0,0,36,36);c.InterpolationMode=InterpolationMode.HighQualityBicubic;c.DrawImage(small,new Rectangle(-8,-8,r.Width+16,r.Height+16));
            using(var tint=new LinearGradientBrush(r,Color.FromArgb(140,37,29,68),Color.FromArgb(155,26,55,64),90))c.FillRectangle(tint,r);
            using(var dim=new SolidBrush(Color.FromArgb(65,0,0,0)))c.FillRectangle(dim,r);
        }immersiveFrames.AddFirst(new AmbientFrame{Source=art,Pixels=bitmap});while(immersiveFrames.Count>2){immersiveFrames.Last.Value.Pixels.Dispose();immersiveFrames.RemoveLast();}}
        g.DrawImageUnscaled(bitmap,r.Location);
    }
    void ImmersiveGlass(Graphics g,Rectangle r,int radius){if(r.Width<2||r.Height<2)return;g.SmoothingMode=SmoothingMode.AntiAlias;using(var shape=Theme.Round(r,radius))using(var brush=new LinearGradientBrush(r,Color.FromArgb(65,220,229,243),Color.FromArgb(35,130,160,175),90))using(var edge=new Pen(Color.FromArgb(48,235,244,255))){g.FillPath(brush,shape);g.DrawPath(edge,shape);}}
    void PaintFocusedPlayer(Graphics g,Rectangle r){
        PaintImmersiveBackdrop(g,r);var art=Cover(Current);
        if(LandscapePlayer(r)){var cover=AlbumBounds(r);Artwork(g,art,cover,22);int x=cover.Right+25,width=r.Width-x-24,top=r.Height/2-116;ImmersiveGlass(g,new Rectangle(x-14,top-24,width+28,326),24);ImmersiveGlass(g,new Rectangle(x+width/2-116,top+150,232,70),35);}
        else{int height=(int)(r.Height*.70);float scale=Math.Max((float)r.Width/art.Width,(float)height/art.Height);float sx=(art.Width-r.Width/scale)/2,sy=(art.Height-height/scale)/2;using(var attrs=new ImageAttributes()){for(int y=0;y<height;y+=4){int strip=Math.Min(4,height-y);float alpha=Math.Min(1f,(height-y)/180f);var matrix=new ColorMatrix();matrix.Matrix33=alpha;attrs.SetColorMatrix(matrix);g.DrawImage(art,new Rectangle(0,y,r.Width,strip),sx,sy+y/scale,r.Width/scale,strip/scale,GraphicsUnit.Pixel,attrs);}}ImmersiveGlass(g,new Rectangle(r.Width/2-116,r.Height-105,232,65),32);}
    }
}
