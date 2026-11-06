using System;
using System.Drawing;
using System.Windows.Forms;
internal sealed class LyricsList:ListBox {
    Font ownedFont;
    internal Action<Graphics,Rectangle> PaintBackdrop;
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    internal void Reflow(){if(!IsHandleCreated)return;for(int i=0;i<Items.Count;i++){int height=Math.Max(76,TextRenderer.MeasureText(Items[i].ToString(),Font,new Size(Math.Max(30,Width-SystemInformation.VerticalScrollBarWidth-48),2000),TextFormatFlags.WordBreak|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Height+28);SendMessage(Handle,0x1A0,new IntPtr(i),new IntPtr(height));}Invalidate();}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);float size=Width<500?22:28;if(Math.Abs(Font.Size-size)>.1f){var old=ownedFont;ownedFont=new Font("Yu Gothic UI",size,FontStyle.Bold);Font=ownedFont;if(old!=null)old.Dispose();}Reflow();}
    void PaintEmpty(Graphics g){if(PaintBackdrop==null)return;int bottom=Items.Count==0?0:GetItemRectangle(Items.Count-1).Bottom;if(bottom<ClientSize.Height)PaintBackdrop(g,new Rectangle(0,Math.Max(0,bottom),ClientSize.Width,ClientSize.Height-Math.Max(0,bottom)));}
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing&&ownedFont!=null){ownedFont.Dispose();ownedFont=null;}}
    protected override void WndProc(ref Message m){
        if(m.Msg==0x14&&PaintBackdrop!=null){using(var g=Graphics.FromHdc(m.WParam))PaintBackdrop(g,ClientRectangle);m.Result=new IntPtr(1);return;}
        base.WndProc(ref m);if(m.Msg==0x115||m.Msg==0x20a||m.Msg==0x197)Invalidate();
        if(m.Msg==0xf&&IsHandleCreated&&!IsDisposed){using(var g=CreateGraphics())PaintEmpty(g);}
        else if(m.Msg==0x318&&m.WParam!=IntPtr.Zero){using(var g=Graphics.FromHdc(m.WParam))PaintEmpty(g);}
    }
}
