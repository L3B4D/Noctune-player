using System;
internal sealed class PcmEqualizer {
    sealed class Filter {internal double B0,B1,B2,A1,A2;readonly double[] z1=new double[2],z2=new double[2];internal float Step(float sample,int channel){double value=sample*B0+z1[channel];z1[channel]=sample*B1-value*A1+z2[channel];z2[channel]=sample*B2-value*A2;return (float)value;}}
    sealed class Configuration {internal Filter[] Filters;internal float Gain;}
    Configuration active;
    internal void Configure(bool enabled,float[] bands){if(!enabled){active=null;return;}double[] frequencies={31.25,62.5,125,250,500,1000,2000,4000,8000,16000};var filters=new Filter[10];float peak=0;for(int i=0;i<10;i++){float gain=bands!=null&&bands.Length>i?Math.Max(-20,Math.Min(20,bands[i])):0;peak=Math.Max(peak,gain);double a=Math.Pow(10,gain/40),w=2*Math.PI*frequencies[i]/48000,alpha=Math.Sin(w)/(2*1.414),cos=Math.Cos(w),den=1+alpha/a;filters[i]=new Filter{B0=(1+alpha*a)/den,B1=-2*cos/den,B2=(1-alpha*a)/den,A1=-2*cos/den,A2=(1-alpha/a)/den};}active=new Configuration{Filters=filters,Gain=(float)Math.Pow(10,-peak/20)};}
    internal void Process(ref float left,ref float right){var settings=active;if(settings==null)return;foreach(var filter in settings.Filters){left=filter.Step(left,0);right=filter.Step(right,1);}left*=settings.Gain;right*=settings.Gain;}
}
