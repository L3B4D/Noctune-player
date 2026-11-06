using System;
using System.Windows.Forms;
internal partial class Player {
    void ShowPrecisionAudio(){using(var dialog=DialogUI.Create("Precision audio","One output clock, independent pitch, and measured loudness.",560,390)){
        DialogUI.Label(dialog,"Pitch",24,116,300,28,13,true);DialogUI.Label(dialog,"Semitones; tempo is controlled separately in Playback tools.",24,150,350,50,10);
        var pitch=new NumericUpDown{Minimum=-12,Maximum=12,DecimalPlaces=1,Increment=.5m,Value=(decimal)Math.Max(-12,Math.Min(12,featuresState.PitchSemitones)),BackColor=Theme.Card,ForeColor=Theme.Paper,BorderStyle=BorderStyle.None,Font=Theme.Font(13)};pitch.SetBounds(400,120,130,32);dialog.Controls.Add(pitch);pitch.Enabled=audio.Managed;
        pitch.ValueChanged+=delegate{ResetTransition();featuresState.PitchSemitones=(float)pitch.Value;audio.SetPitch(featuresState.PitchSemitones);if(nextAudio!=null)nextAudio.SetPitch(featuresState.PitchSemitones);if(outgoing!=null)outgoing.SetPitch(featuresState.PitchSemitones);SaveFeatures();};
        var engine=DialogUI.Label(dialog,"",24,221,512,60,11);using(var refresh=new System.Windows.Forms.Timer{Interval=300}){refresh.Tick+=delegate{engine.Text=(audio.Managed?"Shared PCM · 48 kHz stereo · 32-bit float":"Compatibility playback")+"\n"+(double.IsNaN(audio.Loudness)?"Loudness meter is waiting for audio.":"Integrated loudness: "+audio.Loudness.ToString("0.0")+" LUFS");};refresh.Start();var done=new SoftButton("Done",delegate{dialog.Close();},true);done.SetBounds(416,325,120,38);dialog.Controls.Add(done);dialog.ShowDialog(this);}
    }}
}
