using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

internal sealed class PcmDeck {
    readonly object gate=new object();readonly float[] ring=new float[48000*2*6];int read,write,count;float[] transfer=new float[8192];
    internal volatile bool Stopped=true,Paused,Hold,Ended,Activated,Retired,FadeComplete=true;
    internal float Volume=1,Rate=1,Pitch;internal bool Normalize;
    internal readonly PcmEqualizer Equalizer=new PcmEqualizer();internal R128Meter Meter=new R128Meter();readonly PitchProcessor shifter=new PitchProcessor();double mediaFrames,gain=1,lowL,lowR;
    internal StreamMixProbe Probe=new StreamMixProbe();internal int Generation;internal long Underruns;internal double Bass=1;
    internal int Buffered {get{lock(gate)return count/2;}}
    internal long Time {get{lock(gate)return (long)(mediaFrames/48);}}
    internal void Reset(long position,bool held){lock(gate){Generation++;Probe=new StreamMixProbe();Meter=new R128Meter();gain=1;lowL=lowR=0;read=write=count=0;mediaFrames=position*48.0;Stopped=false;Paused=false;Hold=held;Ended=Activated=Retired=false;FadeComplete=true;Monitor.PulseAll(gate);}}
    internal void Flush(){lock(gate){read=write=count=0;Monitor.PulseAll(gate);}}
    internal void Stop(){Stopped=true;Flush();}
    internal void Seek(long position){lock(gate){read=write=count=0;mediaFrames=position*48.0;Ended=Retired=false;Monitor.PulseAll(gate);}}
    internal void Push(IntPtr samples,int frames){
        if(Stopped||frames<=0||frames>480000)return;int length=frames*2;if(transfer.Length<length)transfer=new float[length];Marshal.Copy(samples,transfer,0,length);PushManaged(transfer,frames,Generation);
    }
    internal void PushManaged(float[] samples,int frames,int generation){var probe=Probe;if(generation!=Generation||Stopped)return;probe.Push(samples,frames);int length=frames*2,at=0;
        lock(gate){while(at<length&&!Stopped&&generation==Generation){while(count==ring.Length&&!Stopped&&generation==Generation)Monitor.Wait(gate,20);if(Stopped||generation!=Generation)return;int n=Math.Min(length-at,Math.Min(ring.Length-write,ring.Length-count));Array.Copy(samples,at,ring,write,n);write=(write+n)%ring.Length;count+=n;at+=n;}}
    }
    internal bool Pull(out float l,out float r){
        l=r=0;if(Stopped||Paused||Hold||Retired)return false;
        lock(gate){if(count<2){if(!Ended)Underruns++;return false;}l=ring[read];r=ring[(read+1)%ring.Length];read=(read+2)%ring.Length;count-=2;mediaFrames+=Rate;if(count<ring.Length/2)Monitor.PulseAll(gate);}
        if(float.IsNaN(l)||float.IsInfinity(l))l=0;if(float.IsNaN(r)||float.IsInfinity(r))r=0;
        Equalizer.Process(ref l,ref r);Meter.Push(l,r);double target=Normalize&&!double.IsNaN(Meter.Integrated)?Math.Max(.125,Math.Min(2,Math.Pow(10,(-23-Meter.Integrated)/20))):1;gain+=(target-gain)/48000;
        shifter.Semitones=Pitch;shifter.Process(ref l,ref r);lowL+=.0156*(l-lowL);lowR+=.0156*(r-lowR);l=(float)((l+(Bass-1)*lowL)*gain*Volume);r=(float)((r+(Bass-1)*lowR)*gain*Volume);return true;
    }
}

// Both decks are summed in this one render callback before one Windows output device.
// Scheduling and crossfade gains advance per sample, independently of the UI timers.
internal sealed class PcmOutput {
    [StructLayout(LayoutKind.Sequential,Pack=2)]struct Format {internal ushort tag,channels;internal uint rate,bytes;internal ushort align,bits,extra;}
    [StructLayout(LayoutKind.Sequential)]struct Header {internal IntPtr data;internal uint length,recorded;internal IntPtr user;internal uint flags,loops;internal IntPtr next,reserved;}
    [DllImport("winmm.dll")]static extern int waveOutOpen(out IntPtr output,int device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")]static extern int waveOutPrepareHeader(IntPtr output,IntPtr header,int size);
    [DllImport("winmm.dll")]static extern int waveOutWrite(IntPtr output,IntPtr header,int size);
    [DllImport("winmm.dll")]static extern int waveOutReset(IntPtr output);
    [DllImport("winmm.dll")]static extern int waveOutUnprepareHeader(IntPtr output,IntPtr header,int size);
    [DllImport("winmm.dll")]static extern int waveOutClose(IntPtr output);
    sealed class Buffer {internal float[] Samples=new float[960];internal GCHandle Pin;internal IntPtr Header;}
    sealed class Link {internal PcmDeck From,To;internal long EndMs;internal int FadeFrames,Frame;internal bool Bass,Started;}
    internal static readonly PcmOutput Shared=new PcmOutput();
    readonly object gate=new object();readonly List<PcmDeck> decks=new List<PcmDeck>();readonly AutoResetEvent wake=new AutoResetEvent(false);readonly List<Buffer> buffers=new List<Buffer>();IntPtr device;Link link;double limiter=1;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Caps {internal ushort maker,product;internal uint version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]internal string name;internal uint formats;internal ushort channels,reserved;internal uint support;}
    [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern int waveOutGetDevCapsW(IntPtr id,out Caps caps,uint size);
    static string DefaultDevice(){Caps c;return waveOutGetDevCapsW(new IntPtr(-1),out c,(uint)Marshal.SizeOf(typeof(Caps)))==0?c.name:null;}
    volatile bool reopenRequested;string outputName;int checkedAt;
    internal void RequestReopen(){reopenRequested=true;wake.Set();}
    bool Reopen(){if(device!=IntPtr.Zero){waveOutReset(device);foreach(var b in buffers)waveOutUnprepareHeader(device,b.Header,Marshal.SizeOf(typeof(Header)));waveOutClose(device);device=IntPtr.Zero;}var format=new Format{tag=3,channels=2,rate=48000,bytes=384000,align=8,bits=32};if(waveOutOpen(out device,-1,ref format,wake.SafeWaitHandle.DangerousGetHandle(),IntPtr.Zero,0x50000)!=0){device=IntPtr.Zero;return false;}foreach(var b in buffers){Marshal.StructureToPtr(new Header{data=b.Pin.AddrOfPinnedObject(),length=3840},b.Header,false);if(waveOutPrepareHeader(device,b.Header,Marshal.SizeOf(typeof(Header)))!=0){waveOutReset(device);foreach(var item in buffers)waveOutUnprepareHeader(device,item.Header,Marshal.SizeOf(typeof(Header)));waveOutClose(device);device=IntPtr.Zero;return false;}}outputName=DefaultDevice();Failure=null;return true;}
    internal bool Available {get{return device!=IntPtr.Zero;}}internal long RenderedFrames;internal Exception Failure;
    internal PcmOutput(bool testing=false){
        if(testing)return;
        var format=new Format{tag=3,channels=2,rate=48000,bytes=384000,align=8,bits=32};
        if(waveOutOpen(out device,-1,ref format,wake.SafeWaitHandle.DangerousGetHandle(),IntPtr.Zero,0x50000)!=0){device=IntPtr.Zero;return;}
        try{for(int i=0;i<4;i++){var b=new Buffer();b.Pin=GCHandle.Alloc(b.Samples,GCHandleType.Pinned);b.Header=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Header)));Marshal.StructureToPtr(new Header{data=b.Pin.AddrOfPinnedObject(),length=3840,flags=1},b.Header,false);buffers.Add(b);if(waveOutPrepareHeader(device,b.Header,Marshal.SizeOf(typeof(Header)))!=0)throw new InvalidOperationException("Audio buffers could not initialize.");}
            outputName=DefaultDevice();checkedAt=Environment.TickCount;var thread=new Thread(Render){IsBackground=true,Name="Noctune PCM render",Priority=ThreadPriority.AboveNormal};thread.Start();
        }catch(Exception ex){Failure=ex;waveOutReset(device);foreach(var b in buffers){waveOutUnprepareHeader(device,b.Header,Marshal.SizeOf(typeof(Header)));Marshal.FreeHGlobal(b.Header);b.Pin.Free();}waveOutClose(device);device=IntPtr.Zero;}
    }
    internal void Add(PcmDeck deck){lock(gate)decks.Add(deck);}
    internal void Remove(PcmDeck deck){deck.Stop();lock(gate){decks.Remove(deck);if(link!=null&&(link.From==deck||link.To==deck)){if(link.To!=deck){link.To.Bass=1;link.To.FadeComplete=true;}link=null;}}}
    internal void Cancel(){lock(gate){if(link!=null){link.From.Bass=link.To.Bass=1;link.To.FadeComplete=true;}link=null;}}
    internal void Schedule(PcmDeck from,PcmDeck to,long endMs,int fadeMs,bool bass){lock(gate){if(link!=null&&link.From==from&&link.To==to){if(!link.Started){link.EndMs=endMs;link.FadeFrames=Math.Max(0,fadeMs*48);}return;}if(to.Activated)return;link=new Link{From=from,To=to,EndMs=endMs,FadeFrames=Math.Max(0,fadeMs*48),Bass=bass};to.FadeComplete=false;}}
    internal void Mix(float[] output){lock(gate){
        for(int frame=0;frame<output.Length/2;frame++){
            if(link!=null&&!link.Started&&!link.From.Paused&&link.To.Buffered>0&&(link.From.Time>=link.EndMs-link.FadeFrames/48||link.From.Ended&&link.From.Buffered==0)){
                link.Started=true;link.To.Hold=false;link.To.Activated=true;link.To.Paused=false;
            }
            float l=0,r=0;bool incomingReady=link!=null&&link.Started&&link.To.Buffered>0;double blend=link!=null&&link.Started?(link.FadeFrames==0?1:Math.Min(1,link.Frame/(double)link.FadeFrames)):0;
            if(link!=null&&link.Started&&link.From.Buffered==0&&incomingReady)blend=1;
            foreach(var deck in decks){double mix=1;if(link!=null){if(deck==link.To)mix=link.Started?Math.Sin(blend*Math.PI/2):0;else if(deck==link.From&&link.Started)mix=incomingReady?Math.Cos(blend*Math.PI/2):1;if(link.Bass&&link.Started){double swap=Math.Max(0,Math.Min(1,(blend-.45)/.10));if(deck==link.From)deck.Bass=incomingReady?1-swap:1;if(deck==link.To)deck.Bass=swap;}}
                float a,b;if(deck.Pull(out a,out b)){l+=(float)(a*mix);r+=(float)(b*mix);}}
            if(link!=null&&link.Started&&!link.To.Paused){if(incomingReady)link.Frame++;if(blend>=1){link.From.Retired=true;link.To.FadeComplete=true;link.From.Bass=link.To.Bass=1;link=null;}}
            double peak=Math.Max(Math.Abs(l),Math.Abs(r));double target=peak>.98?.98/peak:1;limiter=target<limiter?target:limiter+(target-limiter)/2400;output[frame*2]=(float)(l*limiter);output[frame*2+1]=(float)(r*limiter);RenderedFrames++;
        }
    }}
    void Render(){while(true){try{int now=Environment.TickCount;if(reopenRequested||unchecked(now-checkedAt)>=1000){checkedAt=now;string current=DefaultDevice();if(reopenRequested||device==IntPtr.Zero||current!=outputName){reopenRequested=false;if(!Reopen()){Failure=new InvalidOperationException("Audio device unavailable. Waiting to reconnect.");wake.WaitOne(1000);continue;}}}if(device==IntPtr.Zero){wake.WaitOne(100);continue;}foreach(var b in buffers){var header=(Header)Marshal.PtrToStructure(b.Header,typeof(Header));if((header.flags&16)!=0)continue;Mix(b.Samples);if(waveOutWrite(device,b.Header,Marshal.SizeOf(typeof(Header)))!=0){reopenRequested=true;break;}}wake.WaitOne(10);}catch(Exception ex){Failure=ex;reopenRequested=true;wake.WaitOne(1000);}}}
}
