using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Windows.Forms;
internal sealed partial class Meter {
    readonly Timer motionTimer=new Timer{Interval=33};
    readonly Stopwatch progressClock=Stopwatch.StartNew();
    double sampleTime,sampleLength;long sampleAt;bool playing,playbackMeter;
    internal double DisplayFraction{get{
        if(!playbackMeter||Capture||!playing||sampleLength<=0||Player.reduceMotion)return Value/(double)Math.Max(1,Maximum);
        double elapsed=Math.Min(500,Math.Max(0,progressClock.ElapsedMilliseconds-sampleAt));
        return Math.Max(0,Math.Min(1,(sampleTime+elapsed)/sampleLength));
    }}
    void InitProgress(){
        motionTimer.Tick+=delegate{if(!playing||!Visible||Player.reduceMotion){motionTimer.Stop();return;}Invalidate();};
        VisibleChanged+=delegate{SyncMotion();};
        Disposed+=delegate{motionTimer.Dispose();};
    }
    internal void PlaybackSample(long time,long length,bool isPlaying){
        playbackMeter=true;
        // VLC may repeat a timestamp while buffering. Never extrapolate indefinitely.
        if(time!=sampleTime||isPlaying!=playing||length!=sampleLength){sampleTime=time;sampleLength=length;sampleAt=progressClock.ElapsedMilliseconds;}
        playing=isPlaying;SyncMotion();Invalidate();
    }
    void SyncMotion(){if(playing&&Visible&&!Player.reduceMotion)motionTimer.Start();else motionTimer.Stop();}
    void DrawPlayed(Graphics g,int x,int y){
        using(var pen=new Pen(Theme.Peach,playbackMeter?2.5f:4f)){
            pen.StartCap=pen.EndCap=LineCap.Round;
            if(!playbackMeter||!playing||Player.reduceMotion||Capture||x<15){g.DrawLine(pen,7,y,x,y);return;}
            int count=x-7+1;var points=new PointF[count];double phase=progressClock.Elapsed.TotalSeconds*5;
            for(int i=0;i<count;i++){float envelope=Math.Min(1,Math.Min(i/10f,(count-1-i)/10f));points[i]=new PointF(7+i,y+(float)Math.Sin(i*.25-phase)*3*envelope);}
            g.DrawLines(pen,points);
        }
    }
}
internal partial class Player {
    void UpdateProgressMotion(){
        bool playing=active&&audio.State==3;
        long time=active?audio.Time:0,length=active?audio.Length:0;
        ((Meter)position).PlaybackSample(time,length,playing);
        barPosition.PlaybackSample(time,length,playing);
    }
}
