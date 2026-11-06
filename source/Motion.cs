using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// All short transitions use one UI timer, which sleeps when nothing is moving.
internal sealed class Motion : IDisposable {
    static readonly List<Motion> active=new List<Motion>();
    static readonly Timer ticker=new Timer{Interval=15};
    static readonly Stopwatch clock=Stopwatch.StartNew();
    static Motion(){ticker.Tick+=delegate{foreach(var animation in active.ToArray())animation.Tick();};}
    [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint param,out int value,uint flags);
    internal static bool Allowed {get{int enabled;return !Player.reduceMotion&&SystemInformation.IsMenuAnimationEnabled&&
        (!SystemParametersInfo(0x1042,0,out enabled,0)||enabled!=0);}}
    readonly Action changed;readonly Action finished;
    float start,target;long since;int duration;
    internal float Value {get;private set;}
    internal Motion(Action repaint,Action complete=null){changed=repaint;finished=complete;}
    internal void To(float value,int milliseconds,bool restart=false){
        if(restart){Stop();Value=0;}
        if(!restart&&active.Contains(this)&&target==value)return;
        target=value;
        if(!Allowed||Math.Abs(Value-value)<.001f){Complete();return;}
        start=Value;duration=milliseconds;since=clock.ElapsedMilliseconds;
        if(!active.Contains(this))active.Add(this);ticker.Start();changed();
    }
    void Tick(){
        if(!active.Contains(this))return;
        double t=Math.Min(1,(clock.ElapsedMilliseconds-since)/(double)duration);
        if(t>=1||Player.reduceMotion){Complete();return;}
        Value=start+(target-start)*(float)(1-Math.Pow(1-t,3));changed();
    }
    void Complete(){Stop();Value=target;changed();if(finished!=null)finished();}
    internal void Stop(){active.Remove(this);if(active.Count==0)ticker.Stop();}
    internal static void FinishAll(){foreach(var m in active.ToArray())if(active.Contains(m))m.Complete();}
    public void Dispose(){Stop();}
    internal static Color Mix(Color a,Color b,float n){
        return Color.FromArgb((int)(a.R+(b.R-a.R)*n),(int)(a.G+(b.G-a.G)*n),(int)(a.B+(b.B-a.B)*n));
    }
}

// Move the existing native window inside a fixed clip. A transition never
// captures the desktop or rerenders album art into full-window snapshots.
internal sealed class PageMotion : IMessageFilter,IDisposable {
    readonly Control page;readonly Motion entrance;
    int distance;
    internal bool Running{get;private set;}
    internal PageMotion(Control content){
        page=content;entrance=new Motion(Apply,End);
    }
    internal void Start(bool player){
        End();if(!Motion.Allowed||!page.Visible)return;
        distance=player?16:10;Running=true;Application.AddMessageFilter(this);
        entrance.To(1,player?200:160,true);
    }
    void Apply(){if(Running)page.Top=(int)Math.Round(distance*(1-entrance.Value));}
    internal void End(){
        entrance.Stop();if(!Running)return;
        Running=false;Application.RemoveMessageFilter(this);
        if(!page.IsDisposed)page.Top=0;
    }
    public bool PreFilterMessage(ref Message msg){
        if((msg.Msg>=0x100&&msg.Msg<=0x109)||(msg.Msg>=0x201&&msg.Msg<=0x20e))End();
        return false;
    }
    public void Dispose(){End();entrance.Dispose();}
}