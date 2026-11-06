using System;
using System.Drawing;
using System.Windows.Forms;
internal partial class Player {
    bool inAppDragging,miniDragMoved;Point inAppGrab;Bitmap glassBackdrop;Rectangle glassBackdropBounds;readonly System.Windows.Forms.Timer glassRefresh=new System.Windows.Forms.Timer{Interval=220};
    sealed class PlayerDragLayer:Control {
        internal Bitmap Page,PlayerImage;internal Rectangle PlayerBounds;
        internal PlayerDragLayer(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque,true);Cursor=Cursors.SizeAll;}
        protected override void OnPaint(PaintEventArgs e){
            if(Page!=null){Rectangle dirty=Rectangle.Intersect(e.ClipRectangle,ClientRectangle);if(!dirty.IsEmpty)e.Graphics.DrawImage(Page,dirty,dirty,GraphicsUnit.Pixel);}
            if(PlayerImage!=null){var saved=e.Graphics.Save();using(var shape=Theme.Round(PlayerBounds,PlayerBounds.Height/2))e.Graphics.SetClip(shape,System.Drawing.Drawing2D.CombineMode.Intersect);e.Graphics.DrawImageUnscaled(PlayerImage,PlayerBounds.Location);e.Graphics.Restore(saved);}
        }
        protected override void Dispose(bool disposing){if(disposing){if(Page!=null)Page.Dispose();if(PlayerImage!=null)PlayerImage.Dispose();}base.Dispose(disposing);}
    }
    PlayerDragLayer playerDragLayer;bool transferringDrag;
    void BeginPlayerDrag(){
        var layer=new PlayerDragLayer{Bounds=ClientRectangle,PlayerBounds=mini.Bounds};
        try{
            layer.Page=new Bitmap(ClientSize.Width,ClientSize.Height);
            using(var g=Graphics.FromImage(layer.Page)){g.Clear(BackColor);foreach(Control part in new Control[]{stageClip,nav})if(part.Visible&&part.Width>0&&part.Height>0){using(var bitmap=new Bitmap(part.Width,part.Height)){part.DrawToBitmap(bitmap,part.ClientRectangle);g.DrawImageUnscaled(bitmap,part.Location);}}}
            layer.PlayerImage=new Bitmap(mini.Width,mini.Height);mini.DrawToBitmap(layer.PlayerImage,mini.ClientRectangle);
            layer.MouseMove+=delegate{MovePlayerDrag();};layer.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)EndPlayerDrag();};
            layer.MouseCaptureChanged+=delegate{if(!transferringDrag&&!layer.Capture&&inAppDragging)EndPlayerDrag();};
            playerDragLayer=layer;Controls.Add(layer);layer.BringToFront();transferringDrag=true;layer.Capture=true;transferringDrag=false;
        }catch{transferringDrag=false;playerDragLayer=null;layer.Dispose();throw;}
    }
    void MovePlayerDrag(){
        if(!inAppDragging)return;Point point=PointToClient(Cursor.Position);Point next=new Point(point.X-inAppGrab.X,point.Y-inAppGrab.Y);UpdatePlayerDrag(next);
    }
    void UpdatePlayerDrag(Point next){
        if(!miniDragMoved&&Math.Abs(next.X-mini.Left)+Math.Abs(next.Y-mini.Top)<3)return;
        if(playerDragLayer==null)BeginPlayerDrag();miniDragMoved=true;
        Rectangle previous=playerDragLayer.PlayerBounds;Rectangle target=InAppPlayerBounds(next.X,next.Y,true);
        if(previous==target)return;playerDragLayer.PlayerBounds=target;
        playerDragLayer.Invalidate(Rectangle.Union(previous,target));
    }
    void EndPlayerDrag(){
        if(!inAppDragging)return;inAppDragging=false;
        var layer=playerDragLayer;
        if(layer!=null){Rectangle finalBounds=layer.PlayerBounds;featuresState.InAppPlayerMoved=true;featuresState.InAppPlayerX=finalBounds.X;featuresState.InAppPlayerY=finalBounds.Y;LayoutInAppPlayer();layer.Capture=false;playerDragLayer=null;Controls.Remove(layer);layer.Dispose();Invalidate(true);}
        mini.Capture=miniTrack.Capture=false;
        if(miniDragMoved)SaveFeatures();QueueGlassRefresh();BeginInvoke((Action)delegate{miniDragMoved=false;});
    }
    sealed class GlassScrollFilter:IMessageFilter {internal Player Owner;public bool PreFilterMessage(ref Message m){if((m.Msg==0x20a||m.Msg==0x115)&&Owner!=null&&!Owner.closing&&Form.FromChildHandle(m.HWnd)==Owner)Owner.QueueGlassRefresh();return false;}}
    void QueueGlassRefresh(){if(closing)return;glassRefresh.Stop();glassRefresh.Start();}
    void CaptureGlassBackdrop(){
        if(closing||!IsHandleCreated||!stageClip.Visible||stageClip.Width<2||stageClip.Height<2)return;
        try{using(var full=new Bitmap(stageClip.Width,stageClip.Height)){stageClip.DrawToBitmap(full,new Rectangle(Point.Empty,full.Size));var small=new Bitmap(Math.Max(2,full.Width/14),Math.Max(2,full.Height/14));using(var g=Graphics.FromImage(small)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(full,new Rectangle(Point.Empty,small.Size));}var old=glassBackdrop;glassBackdrop=small;glassBackdropBounds=stageClip.Bounds;if(old!=null)old.Dispose();}mini.Invalidate(true);}catch(ArgumentException){}catch(InvalidOperationException){}
    }
    void InitInAppPlayer(){
        glassRefresh.Tick+=delegate{glassRefresh.Stop();if(!inAppDragging)CaptureGlassBackdrop();};var filter=new GlassScrollFilter{Owner=this};Application.AddMessageFilter(filter);Shown+=delegate{QueueGlassRefresh();};Resize+=delegate{QueueGlassRefresh();};FormClosed+=delegate{Application.RemoveMessageFilter(filter);filter.Owner=null;glassRefresh.Dispose();if(glassBackdrop!=null)glassBackdrop.Dispose();};
        mini.AccessibleName="Floating player inside Noctune";mini.Cursor=Cursors.SizeAll;
        MouseEventHandler down=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;glassRefresh.Stop();inAppDragging=true;miniDragMoved=false;inAppGrab=mini.PointToClient(Cursor.Position);((Control)sender).Capture=true;};
        foreach(Control control in new Control[]{mini,miniTrack}){control.MouseDown+=down;control.MouseMove+=delegate{MovePlayerDrag();};control.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)EndPlayerDrag();};control.MouseCaptureChanged+=delegate{if(!transferringDrag&&!control.Capture&&inAppDragging&&playerDragLayer==null)EndPlayerDrag();};}
        Resize+=delegate{if(inAppDragging)EndPlayerDrag();};
        help.SetToolTip(miniTrack,"Click for full player · Drag to move inside Noctune");
        mini.ContextMenuStrip.Items.Add("Reset player position",null,delegate{ResetInAppPlayer();});
        LayoutInAppPlayer();
    }
    Rectangle InAppPlayerBounds(int requestedX,int requestedY,bool moved){
        int left=Wide?(ClientSize.Width>=1000?176:76)+18:10;
        int right=ClientSize.Width-18,bottom=Wide?ClientSize.Height-18:nav.Top-12;
        int width=Math.Min(520,Math.Max(1,right-left)),height=68;
        int x=moved?requestedX:left+(right-left-width)/2;
        int y=moved?requestedY:bottom-height;
        x=Math.Max(left,Math.Min(x,right-width));y=Math.Max(10,Math.Min(y,bottom-height));
        return new Rectangle(x,y,width,height);
    }
    void LayoutInAppPlayer(){
        Rectangle target=InAppPlayerBounds(featuresState.InAppPlayerX,featuresState.InAppPlayerY,featuresState.InAppPlayerMoved);
        Rectangle previous=mini.Bounds;bool resized=mini.Size!=target.Size;
        mini.Bounds=target;if(playerDragLayer==null)mini.BringToFront();if(resized||mini.Region==null)RoundPlayerBar();
        if(previous!=target){Rectangle exposed=Rectangle.Intersect(previous,stageClip.Bounds);if(!exposed.IsEmpty){exposed.Offset(-stageClip.Left,-stageClip.Top);stageClip.Invalidate(exposed,true);}Invalidate(previous,true);mini.Invalidate(true);}
    }
    void ResetInAppPlayer(){featuresState.InAppPlayerMoved=false;LayoutInAppPlayer();SaveFeatures();}
    void ShowFloatingPill(){if(page=="Player"||page=="Lyrics")ShowPage("Home");LayoutInAppPlayer();mini.Visible=true;miniTrack.Focus();}
}
