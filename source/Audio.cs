using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class Audio : IDisposable {
    internal static bool CompatibilityMode;PcmDeck pcm;FfmpegAudio ffmpeg;
    internal bool Buffering {get{return pcm!=null&&!pcm.Paused&&!pcm.Ended&&pcm.Buffered==0;}}
    internal MixAnalysis Analysis {get{return pcm==null?null:pcm.Probe.Read(Length,pcm.Ended);}}
    internal bool Managed {get{return pcm!=null;}}
    internal bool PreparedReady {get{return pcm==null?State==3:pcm.Buffered>=4800||pcm.Ended&&pcm.Buffered>0;}}
    internal bool HandoffStarted {get{return pcm!=null&&pcm.Activated;}}
    internal bool FadeComplete {get{return pcm==null||pcm.FadeComplete;}}
    internal double Loudness {get{return pcm==null?double.NaN:pcm.Meter.Integrated;}}
    internal void SetPitch(float semitones){if(ffmpeg!=null)ffmpeg.SetPitch(semitones);else if(pcm!=null)pcm.Pitch=Math.Max(-12,Math.Min(12,semitones));}
    internal void Schedule(Audio next,long end,int fade,bool bass){if(pcm!=null&&next.pcm!=null)PcmOutput.Shared.Schedule(pcm,next.pcm,end,fade,bass);}
    internal static void CancelSchedule(){PcmOutput.Shared.Cancel();}
    internal void ExpectedLength(long ms){if(ffmpeg!=null)ffmpeg.ExpectedLength(ms);}
    internal void HoldPrepared(){if(ffmpeg==null)libvlc_media_player_set_pause(player,1);}
    internal void Preload(string url,long cue){Begin(url,true,cue);}

    const string Lib="libvlc.dll";
    const CallingConvention C=CallingConvention.Cdecl;
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetDllDirectory(string path);
    [DllImport(Lib,CallingConvention=C)] static extern IntPtr libvlc_new(int argc,IntPtr argv);
    [DllImport(Lib,CallingConvention=C)] static extern IntPtr libvlc_media_player_new(IntPtr vlc);
    [DllImport(Lib,CallingConvention=C)] static extern IntPtr libvlc_media_new_location(IntPtr vlc,byte[] location);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_media_add_option(IntPtr m,byte[] option);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_media_release(IntPtr media);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_media_player_set_media(IntPtr player,IntPtr media);
    [DllImport(Lib,CallingConvention=C)] static extern int libvlc_media_player_play(IntPtr player);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_media_player_set_pause(IntPtr player,int pause);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_media_player_stop(IntPtr player);
    [DllImport(Lib,CallingConvention=C)] static extern long libvlc_media_player_get_time(IntPtr player);
    [DllImport(Lib,CallingConvention=C)] static extern long libvlc_media_player_get_length(IntPtr player);
    [DllImport(Lib,CallingConvention=C)] static extern int libvlc_media_player_get_state(IntPtr player);
    [DllImport(Lib,CallingConvention=C)] static extern int libvlc_media_player_is_seekable(IntPtr player);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_media_player_set_time(IntPtr player,long time);
    [DllImport(Lib,CallingConvention=C)] static extern int libvlc_audio_set_volume(IntPtr player,int volume);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_media_player_release(IntPtr player);
    [DllImport(Lib,CallingConvention=C)] static extern void libvlc_release(IntPtr vlc);
    [DllImport(Lib,CallingConvention=C)]static extern IntPtr libvlc_audio_equalizer_new();
    [DllImport(Lib,CallingConvention=C)]static extern void libvlc_audio_equalizer_release(IntPtr eq);
    [DllImport(Lib,CallingConvention=C)]static extern int libvlc_audio_equalizer_set_amp_at_index(IntPtr eq,float gain,uint band);
    [DllImport(Lib,CallingConvention=C)]static extern int libvlc_audio_equalizer_set_preamp(IntPtr eq,float gain);
    [DllImport(Lib,CallingConvention=C)]static extern int libvlc_media_player_set_equalizer(IntPtr player,IntPtr eq);
    [DllImport(Lib,CallingConvention=C)]static extern int libvlc_media_player_set_rate(IntPtr player,float rate);internal bool SetRate(float rate){rate=Math.Max(.5f,Math.Min(2,rate));if(ffmpeg!=null){ffmpeg.SetRate(rate);return true;}bool accepted=libvlc_media_player_set_rate(player,rate)==0;if(accepted&&pcm!=null)pcm.Rate=rate;return accepted;} internal readonly bool Normalized;
    internal void Equalizer(bool enabled,float[] bands){
        if(pcm!=null){pcm.Equalizer.Configure(enabled,bands);return;}
        if(!enabled){libvlc_media_player_set_equalizer(player,IntPtr.Zero);return;}
        var eq=libvlc_audio_equalizer_new();if(eq==IntPtr.Zero)throw new IOException("Equalizer could not initialize.");
        try{float peak=0;for(uint i=0;i<10;i++){float gain=bands!=null&&i<bands.Length?Math.Max(-20,Math.Min(20,bands[i])):0;peak=Math.Max(peak,gain);libvlc_audio_equalizer_set_amp_at_index(eq,gain,i);}libvlc_audio_equalizer_set_preamp(eq,-peak);if(libvlc_media_player_set_equalizer(player,eq)!=0)throw new IOException("Equalizer is unavailable.");}finally{libvlc_audio_equalizer_release(eq);}
    }
    IntPtr instance, player;
    RangeAudio.Reader reader;
    static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s+"\0"); }
    internal Audio(bool normalize=false) {
        Normalized=normalize;
        var path=Path.Combine(Catalog.Root,"tools","vlc");
        if (!SetDllDirectory(path)) throw new IOException("Could not load audio components.");
        Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH",Path.Combine(path,"plugins"));
        if(!normalize||!CompatibilityMode&&FfmpegAudio.Available)instance=libvlc_new(0,IntPtr.Zero);
        else{var args=new[]{"--audio-filter=normvol","--norm-max-level=2.0","--norm-buff-size=20"};var ptrs=new IntPtr[args.Length];var argv=Marshal.AllocHGlobal(IntPtr.Size*args.Length);try{for(int i=0;i<args.Length;i++){ptrs[i]=Marshal.StringToHGlobalAnsi(args[i]);Marshal.WriteIntPtr(argv,i*IntPtr.Size,ptrs[i]);}instance=libvlc_new(args.Length,argv);}finally{foreach(var ptr in ptrs)if(ptr!=IntPtr.Zero)Marshal.FreeHGlobal(ptr);Marshal.FreeHGlobal(argv);}}
        if(instance!=IntPtr.Zero) player=libvlc_media_player_new(instance);
        if(player==IntPtr.Zero) { Dispose(); throw new IOException("Could not initialize VLC. Extract the entire ZIP before opening the player."); }
        if(!CompatibilityMode&&FfmpegAudio.Available){pcm=new PcmDeck{Normalize=normalize};ffmpeg=new FfmpegAudio(pcm);PcmOutput.Shared.Add(pcm);}

    }
    internal void Start(string url) { Begin(url,false,0); }
    void Begin(string url,bool held,long cue) {
        Stop();if(ffmpeg!=null){ffmpeg.Start(url,cue,held);return;}var range=RangeAudio.Find(url);
        if(range!=null)reader=new RangeAudio.Reader(range);
        var media=reader==null?libvlc_media_new_location(instance,Utf8(url)):reader.Media(instance);
        if(media==IntPtr.Zero) throw new IOException("Could not open this track.");
        try {
            libvlc_media_add_option(media,Utf8(":no-video"));libvlc_media_add_option(media,Utf8(Normalized&&pcm==null?":audio-filter=scaletempo:normvol":":audio-filter=scaletempo"));
            libvlc_media_add_option(media,Utf8(":network-caching=1500"));if(cue>0)libvlc_media_add_option(media,Utf8(":start-time="+(cue/1000.0).ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)));
            libvlc_media_player_set_media(player,media);
        } finally { libvlc_media_release(media); }
        if(libvlc_media_player_play(player)<0) throw new IOException("Playback could not start.");
    }
    internal void Pause(bool value) { if(pcm!=null){pcm.Paused=value;if(!value)pcm.Hold=false;if(ffmpeg!=null)return;}libvlc_media_player_set_pause(player,value?1:0); }
    internal void Stop() {
        if(ffmpeg!=null)ffmpeg.Stop();if(pcm!=null)pcm.Stop();if(reader!=null)reader.Cancel();
        libvlc_media_player_stop(player);
        if(reader!=null){libvlc_media_player_set_media(player,IntPtr.Zero);reader.Release();reader=null;}
    }
    internal bool ReaderFailed { get { return ffmpeg!=null?ffmpeg.Failed:reader!=null&&reader.Failed; } }
    internal int State { get { int state=ffmpeg!=null?(ffmpeg.Failed?7:!ffmpeg.Active?0:pcm.Ended&&pcm.Buffered==0?6:3):libvlc_media_player_get_state(player);if(pcm==null)return state;if(PcmOutput.Shared.Failure!=null)return 5;if(pcm.Retired)return 6;if(pcm.Paused)return 4;if(pcm.Ended||state==6)return pcm.Buffered>0&&!pcm.Hold?3:pcm.Buffered==0?6:state;return state;} }
    internal long Time { get { return pcm==null?libvlc_media_player_get_time(player):Math.Max(0,pcm.Time-30); } }
    internal long Length { get { return ffmpeg!=null?ffmpeg.Length:libvlc_media_player_get_length(player); } }
    internal bool Seekable { get { return ffmpeg!=null?ffmpeg.Length>0:libvlc_media_player_is_seekable(player)!=0; } }
    internal void Seek(long ms) { if(Seekable){if(ffmpeg!=null){ffmpeg.Seek(ms);return;}if(pcm!=null)pcm.Seek(ms);libvlc_media_player_set_time(player,ms);} }
    internal void Volume(int v) { if(pcm!=null)pcm.Volume=Math.Max(0,Math.Min(100,v))/100f;else libvlc_audio_set_volume(player,v); }
    public void Dispose() {
        if(player!=IntPtr.Zero) { Stop(); libvlc_media_player_release(player); player=IntPtr.Zero; }
        if(instance!=IntPtr.Zero) { libvlc_release(instance); instance=IntPtr.Zero; }if(ffmpeg!=null){ffmpeg.Dispose();ffmpeg=null;}if(pcm!=null){PcmOutput.Shared.Remove(pcm);pcm=null;}
    }
}
