using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
internal partial class Player {
    Audio nextAudio,outgoing;CancellationTokenSource transitionRequest;string readyKey,preparingKey;int transitionVersion;bool extending;string autoplaySeed;
    readonly System.Windows.Forms.Timer fadeTimer=new System.Windows.Forms.Timer{Interval=30};
    readonly System.Windows.Forms.Timer transitionTimer=new System.Windows.Forms.Timer{Interval=40};
    readonly Stopwatch fadeClock=new Stopwatch();long fadePrevious;double fadeElapsed;int fadeDuration;
    void InitTransitions(){
        fadeTimer.Tick+=delegate{if(outgoing==null){fadeTimer.Stop();return;}if(audio.Managed&&outgoing.Managed){if(audio.FadeComplete){outgoing.Dispose();outgoing=null;fadeTimer.Stop();}return;}long now=fadeClock.ElapsedMilliseconds;if(outgoing.State==6||outgoing.State==7){outgoing.Dispose();outgoing=null;fadeTimer.Stop();audio.Volume(volume.Value);audio.SetRate(featuresState.PlaybackRate);audio.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);return;}if(audio.State!=3){if(audio.State==4)outgoing.Pause(true);else if(outgoing.State==3)outgoing.Volume(volume.Value);fadePrevious=now;return;}if(outgoing.State==4)outgoing.Pause(false);fadeElapsed+=now-fadePrevious;fadePrevious=now;double mix=Math.Min(1,fadeElapsed/Math.Max(1,fadeDuration));audio.Volume((int)(volume.Value*CrossfadeGain(mix,true)));outgoing.Volume((int)(volume.Value*CrossfadeGain(mix,false)));TransitionBass(mix);if(mix>=1){outgoing.Dispose();outgoing=null;fadeTimer.Stop();audio.Volume(volume.Value);audio.SetRate(featuresState.PlaybackRate);audio.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);}};
        transitionTimer.Tick+=delegate{TransitionTick();};transitionTimer.Start();FormClosing+=delegate{transitionTimer.Dispose();ResetTransition();fadeTimer.Dispose();};
    }
    void ResetTransition(){Audio.CancelSchedule();if(analysisRequest!=null)analysisRequest.Cancel();analyzedPair=null;currentMix=nextMix=null;transitionVersion++;if(transitionRequest!=null){transitionRequest.Cancel();transitionRequest=null;}preparingKey=readyKey=null;if(nextAudio!=null){nextAudio.Dispose();nextAudio=null;}fadeTimer.Stop();if(outgoing!=null){outgoing.Dispose();outgoing=null;}if(!closing){audio.Volume(volume.Value);audio.SetRate(featuresState.PlaybackRate);audio.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);}}
    void TransitionTick(){
        if(!active||closing||shuffle||repeat||outgoing!=null)return;
        long remaining=audio.Length-audio.Time;if(audio.Length<=0||audio.State!=3)return;
        if(index+1>=queue.Count){if(featuresState.Autoplay&&remaining<20000&&!extending)ExtendQueue();return;}
        if(featuresState.Crossfade==0&&featuresState.Gapless==false)return;
        Track track=queue[index+1];string key=Key(track);AnalyzeMixPair(track);if(featuresState.SmartAutomix){currentMix=audio.Analysis??currentMix;if(nextAudio!=null)nextMix=nextAudio.Analysis??nextMix;}if(featuresState.SmartAutomix&&currentMix!=null&&Math.Abs(currentMix.LengthMs-audio.Length)<2000)remaining=Math.Min(remaining,currentMix.OutroMs-audio.Time);
        if(remaining<(Math.Max(featuresState.Crossfade*1000,PlannedFade)+14000)&&nextAudio==null&&preparingKey!=key)PrepareTransition(track);
        if(nextAudio!=null&&readyKey==key){if(audio.Managed&&nextAudio.Managed){audio.Schedule(nextAudio,audio.Time+remaining,PlannedFade,featuresState.BassSwap);if(nextAudio.HandoffStarted)AdvancePrepared();}else if(remaining<=Math.Max(70,PlannedFade))AdvancePrepared();}
    }
    async void PrepareTransition(Track track){
        int version=transitionVersion;var req=new CancellationTokenSource(TimeSpan.FromSeconds(30));transitionRequest=req;preparingKey=Key(track);Audio candidate=null;
        try{
            if(IsSpotifySong(track)){var original=track;track=await MatchSpotify(track,req.Token);req.Token.ThrowIfCancellationRequested();if(closing||version!=transitionVersion||index+1>=queue.Count||queue[index+1]!=original)return;queue[index+1]=track;RebuildQueue();preparingKey=Key(track);}
            string local=OfflinePath(track);if(track.QobuzId>0&&qobuz==null)return;
            string url=local!=null?new Uri(local).AbsoluteUri:track.QobuzId>0?await qobuz.Resolve(track.QobuzId,req.Token):await Catalog.Resolve(track,req.Token);
            if(closing||version!=transitionVersion)return;candidate=new Audio(featuresState.NormalizeVolume);candidate.Equalizer(featuresState.EqualizerEnabled,featuresState.Equalizer);candidate.Volume(candidate.Managed?volume.Value:0);if(candidate.Managed)candidate.Preload(url,featuresState.SmartAutomix&&nextMix!=null?nextMix.IntroMs:0);else candidate.Start(url);candidate.ExpectedLength(track.DurationSeconds*1000L);candidate.SetRate(featuresState.PlaybackRate*(featuresState.SmartAutomix?SmartMix.Tempo(currentMix,nextMix):1));candidate.SetPitch(featuresState.PitchSemitones);candidate.Volume(candidate.Managed?volume.Value:0);
            var watch=Stopwatch.StartNew();while(!candidate.PreparedReady&&watch.ElapsedMilliseconds<12000){req.Token.ThrowIfCancellationRequested();if(candidate.State==7)throw new InvalidOperationException("Next track could not open.");await Task.Delay(35,req.Token);}
            req.Token.ThrowIfCancellationRequested();if(!candidate.PreparedReady||closing||version!=transitionVersion)return;
            if(candidate.Managed)candidate.HoldPrepared();else{candidate.Pause(true);candidate.Seek(0);}nextAudio=candidate;candidate=null;readyKey=Key(track);
        }catch(OperationCanceledException){}catch(Exception ex){System.Diagnostics.Debug.WriteLine("Preload: "+ex.Message);}
        finally{if(candidate!=null)candidate.Dispose();if(transitionRequest==req)transitionRequest=null;req.Dispose();}
    }
    bool AdvancePrepared(){
        if(nextAudio==null||index+1>=queue.Count||readyKey!=Key(queue[index+1]))return false;
        int planned=PlannedFade;outgoing=audio;audio=nextAudio;nextAudio=null;readyKey=preparingKey=null;index++;playedAudio=false;lastPosition=resumeAt=0;triedIPv4=false;startTime=DateTime.UtcNow;active=true;
        if(!audio.Managed){audio.Seek(featuresState.SmartAutomix&&nextMix!=null?nextMix.IntroMs:0);audio.SetRate(featuresState.PlaybackRate*(featuresState.SmartAutomix?SmartMix.Tempo(currentMix,nextMix):1));}audio.Pause(false);now.Text=Current.Title;status.Text=Current.Artist;FetchArtwork(Current);RefreshSurfaces();queueView.Items[index].Selected=true;queueView.Items[index].EnsureVisible();
        fadeDuration=planned;fadeElapsed=0;fadePrevious=0;bassStep=-1;fadeClock.Restart();
        if(audio.Managed&&outgoing.Managed){audio.Volume(volume.Value);if(!audio.FadeComplete)fadeTimer.Start();else{outgoing.Dispose();outgoing=null;}}else if(fadeDuration>0&&outgoing.State==3){audio.Volume(0);fadeTimer.Start();}else{outgoing.Dispose();outgoing=null;audio.Volume(volume.Value);}return true;
    }
    async void ExtendQueue(){
        if(index<0||string.IsNullOrEmpty(Current.Id)||autoplaySeed==Key(Current))return;extending=true;string seed=Key(Current);autoplaySeed=seed;
        try{var tracks=await Catalog.Radio(Current,CancellationToken.None);if(closing||seed!=Key(Current))return;var existing=queue.Select(Key).ToArray();foreach(var track in tracks.Where(t=>!existing.Contains(Key(t))&&Recommendable(t)).OrderByDescending(Score).Take(12))Add(track,false);if(!active&&index+1<queue.Count)Play(index+1);}
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("Autoplay: "+ex.Message);}finally{extending=false;}
    }
}
