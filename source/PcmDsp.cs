using System;
using System.Collections.Generic;

internal static class Pcm24 {
    internal static float Decode(byte a,byte b,byte c){int value=a|(b<<8)|(c<<16);if((value&0x800000)!=0)value|=unchecked((int)0xff000000);return value/8388608f;}
    internal static void Encode(float sample,byte[] target,int offset){int value=(int)Math.Round(Math.Max(-1,Math.Min(8388607.0/8388608,sample))*8388608);target[offset]=(byte)value;target[offset+1]=(byte)(value>>8);target[offset+2]=(byte)(value>>16);}
}

// Stereo BS.1770 K-weighting at 48 kHz, 400 ms blocks / 100 ms hop,
// -70 LUFS absolute gate and -10 LU relative gate. No true-peak certification claim.
internal sealed class R128Meter {
    sealed class Biquad {readonly double b0,b1,b2,a1,a2;double x1,x2,y1,y2;internal Biquad(double b0,double b1,double b2,double a1,double a2){this.b0=b0;this.b1=b1;this.b2=b2;this.a1=a1;this.a2=a2;}internal double Step(double x){double y=b0*x+b1*x1+b2*x2-a1*y1-a2*y2;x2=x1;x1=x;y2=y1;y1=y;return y;}}
    readonly Biquad[] shelf={new Biquad(1.53512485958697,-2.69169618940638,1.19839281085285,-1.69065929318241,.73248077421585),new Biquad(1.53512485958697,-2.69169618940638,1.19839281085285,-1.69065929318241,.73248077421585)};
    readonly Biquad[] high={new Biquad(1,-2,1,-1.99004745483398,.99007225036621),new Biquad(1,-2,1,-1.99004745483398,.99007225036621)};
    readonly double[] window=new double[19200];readonly List<double> blocks=new List<double>();int cursor,filled,hop;double sum;
    internal double Integrated=double.NaN;
    internal void Push(float left,float right){double a=high[0].Step(shelf[0].Step(left)),b=high[1].Step(shelf[1].Step(right));double power=a*a+b*b;sum+=power-window[cursor];window[cursor]=power;cursor=(cursor+1)%window.Length;filled=Math.Min(window.Length,filled+1);if(++hop<4800)return;hop=0;if(filled<window.Length)return;double block=Math.Max(0,sum/window.Length);if(block>Math.Pow(10,(-70+.691)/10)){if(blocks.Count>=36000)blocks.RemoveRange(0,18000);blocks.Add(block);}if(blocks.Count==0)return;double absolute=0;foreach(double e in blocks)absolute+=e;double gate=absolute/blocks.Count*.1,total=0;int count=0;foreach(double e in blocks)if(e>=gate){total+=e;count++;}if(count>0)Integrated=-.691+10*Math.Log10(total/count);}
}

// Two overlapping delay grains; pitch is independent of the decoder's tempo control.
internal sealed class PitchProcessor {
    readonly float[] left=new float[8192],right=new float[8192];int write;double phase;
    internal float Semitones;
    float Read(float[] samples,double delay){double at=write-delay;while(at<0)at+=samples.Length;int i=(int)at%samples.Length;return (float)(samples[i]*(1-(at-Math.Floor(at)))+samples[(i+1)%samples.Length]*(at-Math.Floor(at)));}
    internal void Process(ref float l,ref float r){left[write]=l;right[write]=r;if(Math.Abs(Semitones)>.001){double ratio=Math.Pow(2,Semitones/12.0);phase+=(1-ratio)/2048;phase-=Math.Floor(phase);double other=(phase+.5)%1,w=.5-.5*Math.Cos(2*Math.PI*phase);l=(float)(Read(left,64+phase*2048)*w+Read(left,64+other*2048)*(1-w));r=(float)(Read(right,64+phase*2048)*w+Read(right,64+other*2048)*(1-w));}write=(write+1)%left.Length;}
}
