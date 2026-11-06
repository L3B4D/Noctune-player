using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class StreamMixProbe {
    readonly object gate=new object();readonly short[] head=new short[8000*90],tail=new short[8000*40];int used,tailAt,tailCount,downsample;long total;int running;DateTime last;
    internal MixAnalysis Result;
    internal void Push(float[] data,int frames){lock(gate){for(int i=0;i<frames;i++){if(++downsample<6)continue;downsample=0;float value=(data[i*2]+data[i*2+1])*.5f;if(float.IsNaN(value)||float.IsInfinity(value))value=0;short sample=(short)(Math.Max(-1,Math.Min(1,value))*32767);if(used<head.Length)head[used++]=sample;tail[tailAt]=sample;tailAt=(tailAt+1)%tail.Length;tailCount=Math.Min(tail.Length,tailCount+1);total++;}}}
    internal MixAnalysis Read(long length,bool ended){
        if(DateTime.UtcNow-last<TimeSpan.FromSeconds(5)||used<8000*5||Interlocked.CompareExchange(ref running,1,0)!=0)return Result;last=DateTime.UtcNow;
        Task.Run(delegate{try{short[] first,lastPart;long samples;lock(gate){first=new short[used];Array.Copy(head,first,used);lastPart=new short[tailCount];for(int i=0;i<tailCount;i++)lastPart[i]=tail[(tailAt-tailCount+i+tail.Length)%tail.Length];samples=total;}var analysis=SmartMix.Analyze(first);analysis.LengthMs=length>0?length:samples/8;analysis.OutroMs=analysis.LengthMs;if(ended&&Math.Abs(samples/8-analysis.LengthMs)<2000){var ending=SmartMix.Analyze(lastPart);analysis.OutroMs=Math.Max(analysis.LengthMs-30000,(samples-tailCount)/8+ending.OutroMs);}Result=analysis;}finally{Interlocked.Exchange(ref running,0);}});return Result;
    }
}
