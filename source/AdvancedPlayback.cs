using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;
internal partial class Player {
    string backgroundNextKey;DateTime? sleepDeadline;string sponsorKey,reconnectKey;long reconnectPosition;CancellationTokenSource sponsorRequest;
    readonly List<Tuple<long,long>> sponsorSegments=new List<Tuple<long,long>>();readonly HashSet<long> skippedSegments=new HashSet<long>();
    static readonly HttpClient segmentClient=new HttpClient{Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=1000000};
    void InitAdvancedPlayback(){
        timer.Tick+=delegate{AdvancedPlaybackTick();};
        NetworkAvailabilityChangedEventHandler network=delegate(object sender,NetworkAvailabilityEventArgs e){if(closing||!IsHandleCreated)return;try{BeginInvoke((Action)delegate{if(closing)return;if(!e.IsAvailable&&active&&audio.State==3&&string.IsNullOrEmpty(Current.File)){reconnectKey=Key(Current);reconnectPosition=audio.Time;}else if(e.IsAvailable&&reconnectKey!=null){string key=reconnectKey;reconnectKey=null;if(key==Key(Current)&&audio.State!=4)StartTrack(index,true,reconnectPosition);}});}catch(InvalidOperationException){}};
        NetworkChange.NetworkAvailabilityChanged+=network;FormClosing+=delegate{NetworkChange.NetworkAvailabilityChanged-=network;if(sponsorRequest!=null)sponsorRequest.Cancel();};
    }
    void AdvancedPlaybackTick(){
        if(sleepDeadline.HasValue&&DateTime.UtcNow>=sleepDeadline.Value){sleepDeadline=null;ResetTransition();if(active)PausePlayback(true);reconnectKey=null;status.Text="Sleep timer finished";}
        string key=featuresState.SponsorSkip&&Current.QobuzId==0&&string.IsNullOrEmpty(Current.File)?Current.Id:null;
        if(key!=sponsorKey){sponsorKey=key;sponsorSegments.Clear();skippedSegments.Clear();if(sponsorRequest!=null)sponsorRequest.Cancel();if(!string.IsNullOrEmpty(key))LoadSegments(key);}
        if(!active||audio.State!=3||!audio.Seekable||string.IsNullOrEmpty(key))return;
        long now=audio.Time;foreach(var segment in sponsorSegments)if(now>=segment.Item1&&now<segment.Item2&&!skippedSegments.Contains(segment.Item1)){skippedSegments.Add(segment.Item1);SeekTo(segment.Item2);break;}
    }
    async void LoadSegments(string id){var request=new CancellationTokenSource();sponsorRequest=request;try{
        var categories=new List<string>{"sponsor","selfpromo","interaction"};if(featuresState.SkipNonMusic)categories.Add("music_offtopic");if(featuresState.SkipFiller)categories.Add("filler");
        string url="https://sponsor.ajay.app/api/skipSegments?videoID="+Uri.EscapeDataString(id)+"&categories="+Uri.EscapeDataString(Catalog.Json().Serialize(categories));
        using(var response=await segmentClient.GetAsync(url,request.Token)){if(!response.IsSuccessStatusCode)return;var rows=Catalog.Json().DeserializeObject(await response.Content.ReadAsStringAsync()) as IList;if(closing||sponsorKey!=id||request.IsCancellationRequested||rows==null)return;
            foreach(var row in rows){var values=Catalog.Field(row,"segment") as IList;if(values==null||values.Count!=2)continue;double a=Convert.ToDouble(values[0]),b=Convert.ToDouble(values[1]);if(a>=0&&b>a&&b<86400)sponsorSegments.Add(Tuple.Create((long)(a*1000),(long)(b*1000)));}
        }
    }catch(Exception ex){System.Diagnostics.Debug.WriteLine("SponsorBlock: "+ex.GetType().Name);}finally{if(sponsorRequest==request)sponsorRequest=null;request.Dispose();}}
    void ShowAdvancedPlayback(){using(var d=DialogUI.Create("Playback tools","Timing, automatic skipping, and a quieter end to your night.",560,600)){
        DialogUI.Label(d,"Playback speed",24,112,250,25,12,true);var rate=new ChoiceButton{Font=Theme.Font(10)};rate.Items.AddRange(new object[]{"0.75×","1.0×","1.25×","1.5×","2.0×"});float[] speeds={.75f,1f,1.25f,1.5f,2f};rate.SelectedIndex=Math.Max(0,Array.IndexOf(speeds,featuresState.PlaybackRate));rate.SetBounds(368,108,168,36);d.Controls.Add(rate);rate.SelectedIndexChanged+=delegate{ResetTransition();float speed=speeds[rate.SelectedIndex];if(audio.SetRate(speed)){featuresState.PlaybackRate=speed;if(nextAudio!=null)nextAudio.SetRate(speed);if(outgoing!=null)outgoing.SetRate(speed);SaveFeatures();}else DialogUI.Show(d,"This stream does not support speed changes.","Playback speed");};
        DialogUI.Label(d,"Sleep timer",24,165,250,25,12,true);var sleep=new ChoiceButton{Font=Theme.Font(10)};sleep.Items.AddRange(new object[]{"Off","15 minutes","30 minutes","60 minutes","90 minutes"});sleep.SelectedIndex=0;sleep.SetBounds(368,160,168,36);d.Controls.Add(sleep);sleep.SelectedIndexChanged+=delegate{int[] minutes={0,15,30,60,90};sleepDeadline=sleep.SelectedIndex==0?(DateTime?)null:DateTime.UtcNow.AddMinutes(minutes[sleep.SelectedIndex]);};
        DialogUI.Label(d,sleepDeadline.HasValue?"Timer active until "+sleepDeadline.Value.ToLocalTime().ToShortTimeString():"Sleep pauses playback; it does not shut down your PC.",24,198,512,28,9);
        AddPlaybackToggle(d,"SponsorBlock","Skip sponsors, self-promotion and subscription reminders.",235,()=>featuresState.SponsorSkip,v=>{featuresState.SponsorSkip=v;sponsorKey=null;});
        AddPlaybackToggle(d,"Skip non-music sections","Optional: skip marked non-music intros and outros.",310,()=>featuresState.SkipNonMusic,v=>{featuresState.SkipNonMusic=v;sponsorKey=null;});
        AddPlaybackToggle(d,"Skip filler segments","Uses community-submitted SponsorBlock timings.",385,()=>featuresState.SkipFiller,v=>{featuresState.SkipFiller=v;sponsorKey=null;});
        AddPlaybackToggle(d,"Cache the next song","Downloads one upcoming YouTube track into offline storage.",460,()=>featuresState.CacheNext,v=>{featuresState.CacheNext=v;backgroundNextKey=null;});
        var close=new SoftButton("Done",delegate{d.Close();},true);close.SetBounds(416,545,120,36);d.Controls.Add(close);d.ShowDialog(this);
    }}
    void AddPlaybackToggle(Form d,string title,string note,int y,Func<bool> get,Action<bool> set){DialogUI.Label(d,title,24,y,390,26,12,true);DialogUI.Label(d,note,24,y+28,410,32,9);var toggle=new ToggleButton(title,get,v=>{set(v);SaveFeatures();});toggle.SetBounds(456,y+7,80,36);d.Controls.Add(toggle);}
}
