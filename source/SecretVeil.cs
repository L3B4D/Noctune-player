using System;
using System.Drawing;
using System.Windows.Forms;
// The underlying editor remains password-masked; the visual veil never draws its text.
internal sealed class SecretVeil:Control {
    readonly TextBox editor;readonly Timer timer=new Timer{Interval=50};float phase;
    internal SecretVeil(TextBox input){editor=input;Bounds=input.Bounds;BackColor=Theme.Card;Cursor=Cursors.IBeam;AccessibleName="Protected field. Click to edit securely.";SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);TabStop=false;input.Parent.Controls.Add(this);BringToFront();Visible=input.TextLength>0;input.GotFocus+=delegate{Visible=false;};input.LostFocus+=delegate{Visible=input.TextLength>0;BringToFront();};MouseEnter+=delegate{if(Motion.Allowed)timer.Start();};MouseLeave+=delegate{timer.Stop();};timer.Tick+=delegate{if(!Visible||!Motion.Allowed){timer.Stop();return;}phase+=.12f;Invalidate();};}
    protected override void OnMouseDown(MouseEventArgs e){Visible=false;editor.Focus();base.OnMouseDown(e);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Theme.Card);int width=Math.Max(40,Width-105);using(var dot=new SolidBrush(Color.FromArgb(140,Theme.Muted))){for(int i=0;i<70;i++){float x=(i*43%width)+(float)Math.Sin(phase+i)*2,y=4+i*17%Math.Max(1,Height-8);e.Graphics.FillEllipse(dot,x,y,2,2);}}using(var font=Theme.Font(9))TextRenderer.DrawText(e.Graphics,"Protected",font,new Rectangle(Width-98,0,96,Height),Theme.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.Right);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
