using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Native scrolling stays intact; its chrome lives beyond the clipped viewport.
internal sealed class DarkScrollHost : Panel {
    internal readonly Control Content;
    readonly Panel clip=new Panel();
    readonly DarkScrollThumb thumb;
    readonly ScrollWatch watch;readonly SmoothScroll smooth;
    internal static Control Frame(Control control){var clip=control.Parent;if(clip!=null&&clip.Parent is DarkScrollHost)return clip.Parent;return control;}
    internal static DarkScrollHost Wrap(Control content){
        var parent=content.Parent;var bounds=content.Bounds;bool visible=content.Visible;
        var host=new DarkScrollHost(content);parent.Controls.Add(host);host.Bounds=bounds;host.Visible=visible;return host;
    }
    DarkScrollHost(Control content){
        Content=content;BackColor=content.BackColor;Margin=Padding.Empty;
        clip.BackColor=content.BackColor;Controls.Add(clip);content.Dock=DockStyle.None;clip.Controls.Add(content);
        thumb=new DarkScrollThumb(content);Controls.Add(thumb);watch=new ScrollWatch(content,thumb.Invalidate);smooth=SmoothScroll.Attach(content,thumb.Invalidate);
        content.BackColorChanged+=delegate{BackColor=clip.BackColor=content.BackColor;thumb.BackColor=content.BackColor;};
        Resize+=delegate{Arrange();};Disposed+=delegate{watch.Dispose();};
    }
    void Arrange(){
        clip.SetBounds(0,0,Math.Max(1,Width-14),Height);thumb.SetBounds(Math.Max(0,Width-14),0,14,Height);
        Content.SetBounds(0,0,clip.Width+SystemInformation.VerticalScrollBarWidth,Math.Max(1,Height));if(smooth!=null)smooth.Layout();
    }
    sealed class ScrollWatch : NativeWindow,IDisposable {
        readonly Control owner;readonly Action changed;
        internal ScrollWatch(Control control,Action action){owner=control;changed=action;owner.HandleCreated+=Created;owner.HandleDestroyed+=Destroyed;if(owner.IsHandleCreated)AssignHandle(owner.Handle);}
        void Created(object s,EventArgs e){AssignHandle(owner.Handle);}void Destroyed(object s,EventArgs e){ReleaseHandle();}
        protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0xf||m.Msg==0x115||m.Msg==0x20a||m.Msg==0x100||m.Msg==5||m.Msg==0x197||m.Msg==0x1014)changed();}
        public void Dispose(){owner.HandleCreated-=Created;owner.HandleDestroyed-=Destroyed;ReleaseHandle();}
    }
}
internal sealed class DarkScrollThumb : Control {
    readonly Control owner;readonly Motion hover;int grip;double wheel;
    [StructLayout(LayoutKind.Sequential)]struct Info{internal int size,mask,min,max,page,pos,track;}
    [DllImport("user32.dll")]static extern bool GetScrollInfo(IntPtr hwnd,int bar,ref Info info);
    Info Range{get{var smooth=SmoothScroll.Find(owner);if(smooth!=null){var r=smooth.Range;return new Info{min=0,max=r.Total-1,page=r.Page,pos=r.Position};}var info=new Info{size=Marshal.SizeOf(typeof(Info)),mask=7};if(owner.IsHandleCreated)GetScrollInfo(owner.Handle,1,ref info);return info;}}
    internal DarkScrollThumb(Control content){owner=content;BackColor=content.BackColor;TabStop=false;AccessibleName=content.AccessibleName+" scrollbar";AccessibleRole=AccessibleRole.ScrollBar;hover=new Motion(Invalidate);SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
    Rectangle Thumb(Info i){int track=Height-8,total=i.max-i.min+1;if(track<1||i.page<=0||total<=i.page)return Rectangle.Empty;int length=Math.Min(track,Math.Max(28,(int)((long)track*i.page/total)));return new Rectangle(4,4+(int)((long)(track-length)*(i.pos-i.min)/(total-i.page)),6,length);}
    void ScrollTo(int target){var smooth=SmoothScroll.Find(owner);if(smooth!=null){smooth.Jump(target);Invalidate();return;}var i=Range;target=Math.Max(i.min,Math.Min(target,Math.Max(i.min,i.max-i.page+1)));var list=owner as ResultsList;var rows=owner as ListBox;var panel=owner as ScrollableControl;if(list!=null)list.ScrollTo(target);else if(rows!=null)rows.TopIndex=target;else if(panel!=null)panel.AutoScrollPosition=new Point(0,target);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);var t=Thumb(Range);if(t.IsEmpty)return;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var shape=Theme.Round(t,3))using(var b=new SolidBrush(Motion.Mix(Color.FromArgb(65,65,70),Color.FromArgb(173,173,182),Capture?1:hover.Value)))e.Graphics.FillPath(b,shape);}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover.To(1,110);}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover.To(0,180);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;var smooth=SmoothScroll.Find(owner);if(smooth!=null)smooth.CancelMotion();var i=Range;var t=Thumb(i);if(t.IsEmpty)return;if(e.Y>=t.Top&&e.Y<t.Bottom){grip=e.Y-t.Top;Capture=true;Invalidate();}else ScrollTo(i.pos+(e.Y<t.Top?-1:1)*Math.Max(1,i.page));}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!Capture)return;var i=Range;var t=Thumb(i);int travel=Height-8-t.Height;if(travel>0)ScrollTo(i.min+(int)Math.Round((e.Y-grip-4)*(double)(i.max-i.min+1-i.page)/travel));}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);var smooth=SmoothScroll.Find(owner);if(smooth!=null){smooth.Wheel(e.Delta);return;}int lines=SystemInformation.MouseWheelScrollLines;if(lines==0)return;var i=Range;int step=lines<0?i.page:lines*(owner is ScrollableControl&&!(owner is ListView)?18:1);wheel+=e.Delta/120.0*step;int move=(int)wheel;wheel-=move;ScrollTo(i.pos-move);}
    protected override void Dispose(bool disposing){if(disposing)hover.Dispose();base.Dispose(disposing);}
}
internal partial class Player {
    static void ScrollVisible(Control c,bool visible){DarkScrollHost.Frame(c).Visible=visible;c.Visible=visible;}
    void BuildDarkScrollbars(){
        homeScroll.AccessibleName="Home";discovery.AccessibleName="Search discovery";
        foreach(var control in new Control[]{homeScroll,discovery,libraryRows,queueView,lyricRows}){var frame=DarkScrollHost.Wrap(control);frame.Visible=true;control.Visible=true;}
        ScrollVisible(queueView,queue.Count>0);UpdateDiscovery();
    }
}
