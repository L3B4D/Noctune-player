using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class ResultsList : ListView {
    internal event Action ScrollChanged;
    internal ResultsList(){DoubleBuffered=true;}
    [StructLayout(LayoutKind.Sequential)]internal struct ScrollInfo {
        internal int size,mask,min,max,page,pos,track;
    }
    [DllImport("user32.dll")]static extern bool GetScrollInfo(IntPtr hwnd,int bar,ref ScrollInfo info);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    internal ScrollInfo Range {
        get {var info=new ScrollInfo{size=Marshal.SizeOf(typeof(ScrollInfo)),mask=7};
            if(IsHandleCreated)GetScrollInfo(Handle,1,ref info);return info;}
    }
    internal void ScrollTo(int target){
        if(Items.Count==0)return;
        var info=Range;int dest=Math.Max(info.min,Math.Min(target,info.max-info.page+1));
        if(dest!=info.pos)SendMessage(Handle,0x1014,IntPtr.Zero,new IntPtr((dest-info.pos)*Items[0].Bounds.Height));
    }
    protected override void WndProc(ref Message m){
        base.WndProc(ref m);
        switch(m.Msg){
            case 0x115:case 0x20a:case 0x100:case 0x1014:case 0x1013:case 5:case 0xf:
                if(ScrollChanged!=null)ScrollChanged();break;
        }
    }
}
internal sealed class ResultScrollbar : Control {
    readonly ResultsList list;
    int grip;readonly Motion hover;
    internal ResultScrollbar(ResultsList owner){
        list=owner;hover=new Motion(Invalidate);TabStop=false;AccessibleName="Search results scrollbar";AccessibleRole=AccessibleRole.ScrollBar;
        BackColor=Theme.Ink;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        owner.ScrollChanged+=Invalidate;
    }
    ResultsList.ScrollInfo DisplayRange{get{var smooth=SmoothScroll.Find(list);if(smooth==null)return list.Range;var r=smooth.Range;return new ResultsList.ScrollInfo{min=0,max=r.Total-1,page=r.Page,pos=r.Position};}}
    void ScrollTo(int target){var smooth=SmoothScroll.Find(list);if(smooth!=null)smooth.Jump(target);else list.ScrollTo(target);}
    Rectangle Thumb(ResultsList.ScrollInfo info){
        int track=Height-6,total=info.max-info.min+1;
        if(track<=0||info.page<=0||total<=info.page)return Rectangle.Empty;
        int length=Math.Min(track,Math.Max(25,(int)((long)track*info.page/total)));
        int y=3+(int)((long)(track-length)*(info.pos-info.min)/(total-info.page));
        return new Rectangle((Width-6)/2,y,6,length);
    }
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(Theme.Ink);var thumb=Thumb(DisplayRange);if(thumb.IsEmpty)return;
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var shape=Theme.Round(thumb,3))using(var brush=new SolidBrush(Motion.Mix(Color.FromArgb(65,65,70),Color.FromArgb(173,173,182),Capture?1:hover.Value)))e.Graphics.FillPath(brush,shape);
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hover.To(1,110);}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover.To(0,180);}
    protected override void Dispose(bool disposing){if(disposing){list.ScrollChanged-=Invalidate;hover.Dispose();}base.Dispose(disposing);}
    protected override void OnMouseDown(MouseEventArgs e){
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        var smooth=SmoothScroll.Find(list);if(smooth!=null)smooth.CancelMotion();var info=DisplayRange;var t=Thumb(info);if(t.IsEmpty)return;
        if(e.Y>=t.Top&&e.Y<t.Bottom){grip=e.Y-t.Top;Capture=true;Invalidate();}
        else ScrollTo(info.pos+(e.Y<t.Top?-1:1)*Math.Max(1,info.page));
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);if(!Capture)return;
        var i=DisplayRange;var t=Thumb(i);int travel=Height-6-t.Height;
        if(travel>0)ScrollTo(i.min+(int)Math.Round((e.Y-grip-3)*(double)(i.max-i.min+1-i.page)/travel));
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;Invalidate();}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);var smooth=SmoothScroll.Find(list);if(smooth!=null)smooth.Wheel(e.Delta);else ScrollTo(DisplayRange.pos-Math.Sign(e.Delta)*3);}
}

internal partial class Player {
    readonly Surface searchField=new Surface();
    readonly Panel resultClip=new Panel();
    ResultScrollbar resultScroll;
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wp,string text);
    void BuildSearchField(){
        var focusRing=new Motion(searchField.Invalidate);searchField.Disposed+=delegate{focusRing.Dispose();};
        searchPage.Controls.Add(searchField);searchField.BackColor=Theme.Ink;
        searchField.PaintSurface=delegate(Graphics g,Rectangle r){
            if(r.Width<4||r.Height<4)return;
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var shape=Theme.Round(new Rectangle(1,1,r.Width-3,r.Height-3),16))
            using(var fill=new SolidBrush(Theme.Card))
            using(var pen=new Pen(Motion.Mix(Theme.Edge,Theme.Peach,focusRing.Value),1)){g.FillPath(fill,shape);g.DrawPath(pen,shape);}

        };
        var icon=Make(searchField,"Focus search",delegate{query.Focus();},delegate(Graphics g,Rectangle r){Glyph(g,"\uE721",r,query.Focused?Theme.Peach:Theme.Muted,14);});icon.TabStop=false;
        searchField.Controls.Add(query);query.BorderStyle=BorderStyle.None;query.BackColor=Theme.Card;query.ForeColor=Theme.Paper;
        query.Font=Theme.Font(12);query.AccessibleName="Search songs or artists";
        query.HandleCreated+=delegate{SendMessage(query.Handle,0x1501,new IntPtr(1),"Search songs or artists");};
        query.GotFocus+=delegate{focusRing.To(1,135);};query.LostFocus+=delegate{focusRing.To(0,150);};
        searchField.MouseDown+=delegate{query.Focus();};
        var clear=Make(searchField,"Clear search",delegate{query.Clear();query.Focus();},delegate(Graphics g,Rectangle r){Glyph(g,"\uE711",r,Theme.Muted,10);});
        clear.Visible=false;query.TextChanged+=delegate{clear.Visible=query.TextLength>0;};
        var go=Pill(searchField,"Search",RunSearch,true);
        searchField.Resize+=delegate{int w=searchField.Width,h=searchField.Height;Place(icon,9,6,30,h-12);Place(go,w-80,6,73,h-12);Place(clear,w-113,9,29,h-18);
            Place(query,45,(h-query.PreferredHeight)/2,w-160,query.PreferredHeight);};
    }
    void BuildResultViewport(){
        resultClip.BackColor=Theme.Ink;searchPage.Controls.Add(resultClip);
        SetList(results,resultClip,"Search results");resultScroll=new ResultScrollbar((ResultsList)results);searchPage.Controls.Add(resultScroll);
        var smooth=SmoothScroll.Attach(results,resultScroll.Invalidate);resultClip.Resize+=delegate{Place(results,0,0,resultClip.ClientSize.Width+SystemInformation.VerticalScrollBarWidth+4,resultClip.ClientSize.Height);smooth.Layout();};
    }
    void ResultRowsChanged(){resultScroll.Invalidate();}
    void ArtworkArrived(string key){
        if(closing||IsDisposed)return;
        if(collectionGallery!=null&&collectionGallery.Visible)foreach(var card in collectionCards)if(card.Tag is Track&&Key((Track)card.Tag)==key)card.Invalidate();if(collectionsHub.Visible)collectionsHub.Invalidate(true);if(discoverPage.Visible&&!discoverBody.IsDisposed)discoverBody.Invalidate(true);
        if(discovery.Visible)discovery.Invalidate(true);if(mixHero!=null&&mixHero.Visible)mixHero.Invalidate();foreach(var c in nextTracks)if(c!=null&&c.Visible)c.Invalidate();
        if(collectionCover!=null&&displayedCollection!=null&&Key(displayedCollection)==key){collectionCover.Invalidate();collectionPage.Invalidate(true);}
        foreach(var list in new[]{results,queueView,libraryRows,collectionRows}){
            if(!list.Visible||!list.IsHandleCreated)continue;
            foreach(ListViewItem row in list.Items)if(row.Tag is Track&&Key((Track)row.Tag)==key&&row.Bounds.IntersectsWith(list.ClientRectangle))
                list.Invalidate(row.Bounds);
        }
        if(home.Visible)for(int i=0;i<features.Length;i++)if(features[i]!=null&&features[i].Visible&&Key(HomePicks[(hero+i)%HomePicks.Length])==key)features[i].Invalidate();
        if(Key(Current)==key){mini.Invalidate(true);if(detail.Visible)detail.Invalidate(true);if(queuePage.Visible)queuePage.Invalidate(true);}
        if(home.Visible)for(int i=0;i<shelf.Length;i++)if(shelf[i]!=null&&shelf[i].Visible&&Key(ShelfTrack(i))==key)shelf[i].Invalidate();
    }
}
