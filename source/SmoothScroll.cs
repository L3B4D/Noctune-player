using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Animate scroll offsets, not page snapshots. Native controls still own selection and input.
internal sealed class SmoothScroll : NativeWindow,IDisposable {
    static readonly Dictionary<Control,SmoothScroll> owners=new Dictionary<Control,SmoothScroll>();
    static readonly WheelRouter router=new WheelRouter();static bool installed;
    readonly Control content;readonly Action changed;readonly Timer ticker=new Timer{Interval=15};readonly Stopwatch clock=Stopwatch.StartNew();
    double position,target,startPosition,startTangent;long animationStart;int duration,lastDirection,lastApplied=-1;bool applying,syncing,measuring;int count=-1,metricWidth=-1;int[] offsets=new int[0];int rowHeight=54,lastNative;
    internal struct Metrics{internal int Position,Total,Page;internal int Maximum{get{return Math.Max(0,Total-Page);}}}
    internal static SmoothScroll Find(Control control){SmoothScroll scroll;return owners.TryGetValue(control,out scroll)?scroll:null;}
    internal static SmoothScroll Attach(Control control,Action update){var old=Find(control);if(old!=null)return old;var scroll=new SmoothScroll(control,update);owners.Add(control,scroll);if(!installed){Application.AddMessageFilter(router);installed=true;}return scroll;}
    SmoothScroll(Control control,Action update){content=control;changed=update;content.HandleCreated+=Created;content.HandleDestroyed+=Destroyed;content.Disposed+=Disposed;content.VisibleChanged+=Visibility;content.SizeChanged+=Resized;ticker.Tick+=Tick;if(content.IsHandleCreated)AssignHandle(content.Handle);}
    void Created(object s,EventArgs e){AssignHandle(content.Handle);}void Destroyed(object s,EventArgs e){ticker.Stop();ReleaseHandle();}
    void Disposed(object s,EventArgs e){Dispose();}void Visibility(object s,EventArgs e){if(!content.Visible)Stop();}void Resized(object s,EventArgs e){if(applying||content.Disposing||content.IsDisposed)return;metricWidth=-1;lastApplied=-1;Stop();SyncNative();}
    int ViewHeight{get{return content.Parent==null?content.ClientSize.Height:content.Parent.ClientSize.Height;}}
    bool IsRows{get{return content is ListView||content is ListBox;}}
    int Count{get{var list=content as ListView;if(list!=null)return list.Items.Count;var box=content as ListBox;return box==null?0:box.Items.Count;}}
    void Measure(){
        if(!IsRows||measuring)return;int n=Count;if(count==n&&metricWidth==content.Width)return;count=n;metricWidth=content.Width;
        var list=content as ListView;if(list!=null){
            // Managed rows can outlive native items during BeginUpdate and handle teardown.
            // Keep the last valid height until the native row can be measured again.
            if(n>0&&content.IsHandleCreated&&!content.Disposing){try{int height=list.Items[0].Bounds.Height;if(height>0)rowHeight=height;else metricWidth=-1;}catch(ArgumentException){metricWidth=-1;}}
            offsets=new int[0];return;
        }
        var box=(ListBox)content;offsets=new int[n+1];rowHeight=1;bool retry=false;measuring=true;
        try{for(int i=0;i<n;i++){int h=Math.Max(1,box.Font.Height+16);try{h=Math.Max(1,box.GetItemHeight(i));}catch(System.ComponentModel.Win32Exception){retry=true;}offsets[i+1]=offsets[i]+h;rowHeight=Math.Max(rowHeight,h);}}finally{measuring=false;if(retry)metricWidth=-1;}
    }
    int NativePixel(){
        if(!content.IsHandleCreated)return 0;var list=content as ListView;if(list!=null)return list.TopItem==null?0:list.TopItem.Index*rowHeight;
        var box=content as ListBox;if(box!=null){int i=Math.Max(0,Math.Min(box.TopIndex,offsets.Length-1));return offsets.Length==0?0:offsets[i];}
        var panel=content as ScrollableControl;return panel==null?0:-panel.AutoScrollPosition.Y;
    }
    internal Metrics Range{get{
        Measure();int page=Math.Max(1,ViewHeight),total=page;
        if(content is ListView)total=Math.Max(page,Count*rowHeight);else if(content is ListBox)total=Math.Max(page,offsets.Length>0?offsets[offsets.Length-1]:0);else {var panel=content as ScrollableControl;if(panel!=null)total=Math.Max(page,panel.DisplayRectangle.Height);}
        return new Metrics{Position=(int)Math.Round(Math.Max(0,Math.Min(total-page,position))),Total=total,Page=page};
    }}
    internal void Layout(){
        if(!IsRows||content.Parent==null||applying)return;lastApplied=-1;Measure();applying=true;try{content.Height=Math.Max(1,ViewHeight+2*rowHeight);}finally{applying=false;}Jump(position);
    }
    internal void Jump(double pixel){Stop();var r=Range;target=position=Math.Max(0,Math.Min(r.Maximum,pixel));Apply();}
    internal void Wheel(int delta){
        if(delta==0||content.IsDisposed||!content.Visible)return;SyncNative();var r=Range;int lines=SystemInformation.MouseWheelScrollLines;if(lines==0||r.Maximum==0)return;
        double step=lines<0?r.Page:lines*(IsRows?Math.Min(rowHeight,80):32);
        double movement=-delta/120.0*step;int direction=Math.Sign(movement);
        // High-resolution devices already deliver a continuous stream of small deltas.
        // Adding a second inertia curve makes that input feel late.
        if(Math.Abs(delta)<120||!Motion.Allowed){Stop();target=position=Math.Max(0,Math.Min(r.Maximum,position+movement));Apply();return;}
        if(ticker.Enabled&&lastDirection!=direction){ticker.Stop();target=position;}
        lastDirection=direction;
        target=Math.Max(0,Math.Min(r.Maximum,target+movement));
        double distance=target-position;
        // Acknowledge a detent in this message, rather than waiting for a WM_TIMER tick.
        position+=Math.Sign(distance)*Math.Min(Math.Abs(distance),Math.Min(12,Math.Abs(movement)*.12));Apply();
        startPosition=position;duration=140;animationStart=clock.ElapsedMilliseconds;
        startTangent=(target-startPosition)*1.25;
        if(Math.Abs(target-position)<.5){position=target;Apply();ticker.Stop();}else ticker.Start();
    }
    void Tick(object s,EventArgs e){
        if(content.IsDisposed||!content.Visible){Stop();return;}var r=Range;target=Math.Max(0,Math.Min(r.Maximum,target));
        double t=Math.Min(1,(clock.ElapsedMilliseconds-animationStart)/(double)duration);
        if(Player.reduceMotion||t>=1){position=target;ticker.Stop();}
        else{
            // Finite monotonic Hermite curve: no delayed ramp-up, bounce, or slow tail.
            double t2=t*t,t3=t2*t;
            position=(2*t3-3*t2+1)*startPosition+(t3-2*t2+t)*startTangent+(-2*t3+3*t2)*target;
            position=Math.Max(Math.Min(startPosition,target),Math.Min(Math.Max(startPosition,target),position));
        }
        position=Math.Max(0,Math.Min(r.Maximum,position));Apply();
    }
    internal void CancelMotion(){Stop();}
    void Stop(){ticker.Stop();target=position;startTangent=0;}
    void Apply(){
        if(!content.IsHandleCreated||applying||content.IsDisposed)return;applying=true;
        try{
            int pixel=Range.Position;if(pixel==lastApplied&&NativePixel()==lastNative)return;var list=content as ListView;var box=content as ListBox;
            if(list!=null){if(list.Items.Count>0){int index=Math.Min(list.Items.Count-1,pixel/rowHeight);var native=list as ResultsList;if(native!=null&&index*rowHeight!=lastNative)native.ScrollTo(index);content.Top=-(pixel-NativePixel());}else content.Top=0;}
            else if(box!=null){if(box.Items.Count>0){int index=Array.BinarySearch(offsets,pixel);if(index<0)index=~index-1;int row=Math.Max(0,Math.Min(box.Items.Count-1,index));if(box.TopIndex!=row)box.TopIndex=row;content.Top=-(pixel-NativePixel());}else content.Top=0;}
            else{var panel=content as ScrollableControl;if(panel!=null)panel.AutoScrollPosition=new Point(0,pixel);}
            lastNative=NativePixel();lastApplied=pixel;changed();
        }finally{applying=false;}
    }
    void SyncNative(){
        if(applying||syncing||measuring||!content.IsHandleCreated||content.Disposing||content.IsDisposed)return;syncing=true;
        try{int previousCount=count,previousHeight=rowHeight;Measure();if(IsRows&&(previousHeight!=rowHeight||content.Height!=ViewHeight+2*rowHeight))Layout();int native=NativePixel();if(native!=lastNative||previousCount!=count){Stop();position=target=native;lastNative=native;lastApplied=-1;if(IsRows)content.Top=0;changed();}}finally{syncing=false;}
    }
    protected override void WndProc(ref Message m){
        if(m.Msg==0x20a&&!applying){Wheel((short)((m.WParam.ToInt64()>>16)&0xffff));m.Result=IntPtr.Zero;return;}
        if(!applying&&(m.Msg==0x100||m.Msg==0x201||m.Msg==0x115))Stop();base.WndProc(ref m);
        if(!applying&&!syncing&&(m.Msg==0x100||m.Msg==0x115||m.Msg==0x197||m.Msg==0x1013||m.Msg==0xf))SyncNative();
        if(!applying&&!syncing&&(m.Msg==0x100||m.Msg==0x1013))EnsureSelection(m.Msg==0x1013?m.WParam.ToInt32():-1);
    }
    void EnsureSelection(int requested){
        Rectangle bounds=Rectangle.Empty;var list=content as ListView;var box=content as ListBox;
        if(list!=null){int index=requested>=0?requested:list.SelectedIndices.Count>0?list.SelectedIndices[0]:-1;if(index>=0&&index<list.Items.Count)bounds=list.Items[index].Bounds;}
        else if(box!=null&&box.SelectedIndex>=0)bounds=box.GetItemRectangle(box.SelectedIndex);
        if(bounds.IsEmpty)return;int top=bounds.Top+content.Top,bottom=bounds.Bottom+content.Top;if(bottom>ViewHeight)Jump(position+bottom-ViewHeight);else if(top<0)Jump(position+top);
    }
    public void Dispose(){ticker.Stop();ticker.Dispose();content.HandleCreated-=Created;content.HandleDestroyed-=Destroyed;content.Disposed-=Disposed;content.VisibleChanged-=Visibility;content.SizeChanged-=Resized;owners.Remove(content);ReleaseHandle();if(owners.Count==0&&installed){Application.RemoveMessageFilter(router);installed=false;}}
    sealed class WheelRouter:IMessageFilter {
        static Control PointerTarget(Message message){
            var addressed=Control.FromChildHandle(message.HWnd);if(addressed==null)return null;
            var root=addressed.TopLevelControl??addressed;
            long packed=message.LParam.ToInt64();var screen=new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff));
            if(!root.RectangleToScreen(root.ClientRectangle).Contains(screen))return addressed;
            Control hit=root;
            for(int depth=0;depth<30;depth++){var child=hit.GetChildAtPoint(hit.PointToClient(screen),GetChildAtPointSkip.Invisible);if(child==null||child==hit)break;hit=child;}
            return hit;
        }
        public bool PreFilterMessage(ref Message m){
            bool wheel=m.Msg==0x20a;if(!wheel&&m.Msg!=0x201&&m.Msg!=0x204&&m.Msg!=0x100)return false;
            Control hit=wheel?PointerTarget(m):Control.FromChildHandle(m.HWnd);
            for(Control c=hit;c!=null;c=c.Parent){
                if(wheel&&c is ArtButton&&c.Text=="Quick pick"&&(Control.ModifierKeys&Keys.Shift)!=0)return false;
                var host=c as DarkScrollHost;var scroll=Find(c)??(host==null?null:Find(host.Content));
                if(scroll==null)continue;if(!wheel){scroll.Stop();return false;}
                scroll.Wheel((short)((m.WParam.ToInt64()>>16)&0xffff));return true;
            }
            return false;
        }
    }
}
