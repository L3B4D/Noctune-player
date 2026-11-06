using System;
internal partial class Player {
    int bassStep=-1;
    internal static double CrossfadeGain(double mix,bool incoming){mix=Math.Max(0,Math.Min(1,mix));return incoming?Math.Sin(mix*Math.PI/2):Math.Cos(mix*Math.PI/2);}
    void TransitionBass(double mix){if(!featuresState.BassSwap||outgoing==null)return;double swap=Math.Max(0,Math.Min(1,(mix-.45)/.10));int step=(int)Math.Round(swap*10);if(step==bassStep)return;bassStep=step;var incoming=new float[10];var leaving=new float[10];for(int i=0;i<10;i++){float value=featuresState.EqualizerEnabled?featuresState.Equalizer[i]:0;incoming[i]=leaving[i]=value;if(i<3){incoming[i]=Math.Max(-20,value-(float)((1-swap)*20));leaving[i]=Math.Max(-20,value-(float)(swap*20));}}audio.Equalizer(true,incoming);outgoing.Equalizer(true,leaving);}
}
