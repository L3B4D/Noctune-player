using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
internal partial class Player {
    bool wasPlayingBeforeSleep;string sleepTrack;long sleepPosition;
    void InitReliability(){
        Audio.CompatibilityMode=featuresState.CompatibilityAudio;if(featuresState.CompatibilityAudio&&audio.Managed){audio.Dispose();audio=new Audio(featuresState.NormalizeVolume);}
        var menu=new DarkMenu();menu.Items.Add("Retry this track",null,delegate{RetryPlayback();});menu.Items.Add("Show in-app player",null,delegate{ShowMiniPlayer();});menu.Items.Add("Playback options",null,delegate{ShowReliability();});mini.ContextMenuStrip=menu;
        PowerModeChangedEventHandler power=delegate(object sender,PowerModeChangedEventArgs e){if(closing||!IsHandleCreated)return;try{BeginInvoke((Action)delegate{if(closing)return;if(e.Mode==PowerModes.Suspend){wasPlayingBeforeSleep=active&&audio.State==3;sleepTrack=Key(Current);sleepPosition=audio.Time;PausePlayback(true);}else if(e.Mode==PowerModes.Resume&&wasPlayingBeforeSleep){wasPlayingBeforeSleep=false;if(sleepTrack==Key(Current))StartTrack(index,false,sleepPosition);}});}catch(InvalidOperationException){}};
        InitInAppPlayer();SystemEvents.PowerModeChanged+=power;FormClosed+=delegate{SystemEvents.PowerModeChanged-=power;menu.Dispose();};
    }
    void RetryPlayback(){if(index>=0)StartTrack(index,false,Math.Max(lastPosition,restoredPosition));}
    string PlaybackDescription(){if(starting!=null)return "Finding audio…";if(!active)return status.Text;if(audio.State==4)return "Paused";if(audio.State==5&&audio.Managed)return "Audio device unavailable · Reconnecting…";if(audio.State==7)return "Playback failed · Retry available";if(audio.Managed&&audio.Buffering)return "Buffering…";return "Playing";}
    void ShowReliability(){using(var d=DialogUI.Create("Playback & reliability","Choose the engine that works best on your computer.",620,480)){
        DialogUI.Label(d,PlaybackDescription(),24,115,572,28,12,true);
        var retry=new SoftButton("Retry track",delegate{RetryPlayback();d.Close();},true);retry.SetBounds(24,155,130,36);d.Controls.Add(retry);
        AddPlaybackToggle(d,"Compatibility audio engine","VLC fallback. R128 normalization and independent pitch require the standard engine.",213,()=>featuresState.CompatibilityAudio,v=>{long at=audio.Time;bool resume=active&&audio.State==3;ResetTransition();featuresState.CompatibilityAudio=v;Audio.CompatibilityMode=v;audio.Dispose();audio=new Audio(featuresState.NormalizeVolume);active=false;SaveFeatures();if(resume)StartTrack(index,false,at);else restoredPosition=at;});
        DialogUI.Label(d,"Shortcuts: Space play/pause · Alt+←/→ previous/next\nCtrl+←/→ seek 5 seconds · Ctrl+↑/↓ volume\nCtrl+M floating player · Ctrl+F search · Ctrl+R retry",24,316,572,84,10);
        var done=new SoftButton("Done",()=>d.Close(),true);done.SetBounds(466,422,130,36);d.Controls.Add(done);d.ShowDialog(this);
    }}
    protected override bool ProcessCmdKey(ref Message msg,Keys key){
        Control focus=ActiveControl;while(focus is ContainerControl&&((ContainerControl)focus).ActiveControl!=null)focus=((ContainerControl)focus).ActiveControl;
        bool typing=focus is TextBoxBase||focus is NumericUpDown||focus is ComboBox;
        if(key==(Keys.Control|Keys.M)){ShowMiniPlayer();return true;}if(key==(Keys.Control|Keys.R)&&!typing){RetryPlayback();return true;}
        if(!typing){if(key==Keys.Space&&!(focus is ButtonBase)){Toggle();return true;}if(key==(Keys.Alt|Keys.Right)||key==(Keys.Alt|Keys.Left)){Step(key==(Keys.Alt|Keys.Right)?1:-1);return true;}if(key==(Keys.Control|Keys.Right)||key==(Keys.Control|Keys.Left)){if(audio.Seekable)SeekTo(Math.Max(0,Math.Min(audio.Length,audio.Time+(key==(Keys.Control|Keys.Right)?5000:-5000))));return true;}if(key==(Keys.Control|Keys.Up)||key==(Keys.Control|Keys.Down)){volume.Value=Math.Max(0,Math.Min(100,volume.Value+(key==(Keys.Control|Keys.Up)?5:-5)));return true;}}
        return base.ProcessCmdKey(ref msg,key);
    }
    bool Recommendable(Track t){return !featuresState.Hidden.Contains(Key(t))&&!featuresState.BlockedArtists.Contains(MatchName(t.Artist))&&(string.IsNullOrEmpty(t.ArtistId)||!featuresState.BlockedArtists.Contains("id:"+t.ArtistId));}
    IEnumerable<Track> DiverseRecommendations(IEnumerable<Track> tracks){var all=tracks.Where(Recommendable).GroupBy(Key).Select(g=>g.First()).OrderByDescending(Score).ToList();var count=new Dictionary<string,int>();foreach(var track in all){string artist=MatchName(track.Artist);int n;count.TryGetValue(artist,out n);if(n>=3)continue;count[artist]=n+1;yield return track;}}
    void BlockArtist(Track track){filteredDefaults=null;string name=MatchName(track.Artist);if(name.Length==0)return;if(!featuresState.BlockedArtists.Contains(name))featuresState.BlockedArtists.Add(name);if(!string.IsNullOrEmpty(track.ArtistId)&&!featuresState.BlockedArtists.Contains("id:"+track.ArtistId))featuresState.BlockedArtists.Add("id:"+track.ArtistId);if(recommended!=null)recommended=recommended.Where(Recommendable).ToArray();hero=0;SaveFeatures();PaintPicks();homeReason.Text="Recommendations respect your hidden artists";}
}
