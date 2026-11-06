using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
internal partial class Player {
    public sealed class BackupDocument {public int Version{get;set;}public string Features{get;set;}public string Library{get;set;}public Dictionary<string,string> LyricChoices{get;set;}}
    internal static BackupDocument ReadBackup(string file){
        if(new FileInfo(file).Length>16000000)throw new IOException("Backup is too large.");var data=Catalog.Json().Deserialize<BackupDocument>(File.ReadAllText(file));
        if(data==null||data.Version!=1||string.IsNullOrEmpty(data.Features)||string.IsNullOrEmpty(data.Library))throw new IOException("This is not a supported Noctune backup.");
        var features=Catalog.Json().Deserialize<FeatureState>(data.Features);var library=Catalog.Json().Deserialize<LibraryState>(data.Library);
        if(features==null||features.Playlists==null||features.Playlists.Count>10000||features.Playlists.Any(p=>p==null||p.Songs==null||p.Songs.Any(t=>t==null))||library==null||library.Liked==null||library.Recent==null||library.Liked.Any(t=>t==null)||library.Recent.Any(t=>t==null))throw new IOException("The backup contains an invalid library.");
        if(features.Scores!=null&&features.Scores.Values.Any(x=>x==null)||features.SavedAlbums!=null&&features.SavedAlbums.Any(x=>x==null)||features.FollowedArtists!=null&&features.FollowedArtists.Any(x=>x==null)||features.Queue!=null&&features.Queue.Any(x=>x==null))throw new IOException("The backup contains invalid track entries.");
        features.Crossfade=Math.Max(0,Math.Min(8,features.Crossfade));features.Volume=Math.Max(0,Math.Min(100,features.Volume));features.Position=Math.Max(0,features.Position);if(float.IsNaN(features.PlaybackRate)||features.PlaybackRate<.5||features.PlaybackRate>2)features.PlaybackRate=1;if(float.IsNaN(features.PitchSemitones)||Math.Abs(features.PitchSemitones)>12)features.PitchSemitones=0;if(features.DownloadLimitMb<100||features.DownloadLimitMb>102400)features.DownloadLimitMb=2048;
        data.Features=Catalog.Json().Serialize(features);data.Library=Catalog.Json().Serialize(library);
        if(data.LyricChoices==null)data.LyricChoices=new Dictionary<string,string>();if(data.LyricChoices.Count>10000)throw new IOException("Too many lyric choices.");
        foreach(var pair in data.LyricChoices){if(!Regex.IsMatch(pair.Key,@"\A[A-F0-9]{64}\.json\z")||pair.Value.Length>2100000)throw new IOException("Invalid lyric backup entry.");var lyric=Catalog.Json().Deserialize<LyricsSource.Result>(pair.Value);if(lyric==null||LyricsSource.Parse(lyric.Text).Count==0)throw new IOException("Invalid lyric choice.");}
        return data;
    }
    static void WriteAtomic(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    internal static void ApplyBackup(BackupDocument data,string root){
        string recovery=Path.Combine(root,"restore-backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(recovery);
        var writes=new Dictionary<string,string>{{Path.Combine(root,"features.json"),data.Features},{Path.Combine(root,"library.json"),data.Library}};
        foreach(var item in data.LyricChoices)writes.Add(Path.Combine(root,"lyric-choices",item.Key),item.Value);
        var previous=new Dictionary<string,string>();foreach(string path in writes.Keys){previous[path]=File.Exists(path)?File.ReadAllText(path):null;if(previous[path]!=null)WriteAtomic(Path.Combine(recovery,Path.GetFileName(path)),previous[path]);}
        try{foreach(var item in writes)WriteAtomic(item.Key,item.Value);}catch{foreach(var item in previous){if(item.Value!=null)WriteAtomic(item.Key,item.Value);else if(File.Exists(item.Key))File.Delete(item.Key);}throw;}
    }
    void ShowBackup(){using(var d=DialogUI.Create("Backup & restore","Keep your music library safe when moving or reinstalling Noctune.",600,350)){
        DialogUI.Label(d,"Includes likes, history, playlists, settings and saved lyric choices.\nAudio files and account sign-in credentials are not included.",24,120,552,66,11);
        var export=new SoftButton("Export backup",delegate{using(var picker=new SaveFileDialog{Filter="Noctune backup|*.yuma-backup.json",FileName="Noctune-library-"+DateTime.Now.ToString("yyyy-MM-dd")+".yuma-backup.json"}){if(picker.ShowDialog(d)!=DialogResult.OK)return;try{SaveFeatures();SaveLibrary();var choices=new Dictionary<string,string>();if(Directory.Exists(LyricChoiceRoot))foreach(string path in Directory.GetFiles(LyricChoiceRoot,"*.json")){if(Regex.IsMatch(Path.GetFileName(path),@"\A[A-F0-9]{64}\.json\z"))choices[Path.GetFileName(path)]=File.ReadAllText(path);}var data=new BackupDocument{Version=1,Features=File.ReadAllText(featurePath),Library=File.ReadAllText(statePath),LyricChoices=choices};WriteAtomic(picker.FileName,Catalog.Json().Serialize(data));ReadBackup(picker.FileName);DialogUI.Show(d,"Backup exported and checked.","Backup saved");}catch(Exception ex){DialogUI.Show(d,ex.Message,"Backup failed");}}},true);export.SetBounds(24,229,170,40);d.Controls.Add(export);
        var restore=new SoftButton("Restore backup",delegate{using(var picker=new OpenFileDialog{Filter="Noctune backup|*.yuma-backup.json|JSON|*.json"}){if(picker.ShowDialog(d)!=DialogResult.OK)return;try{var data=ReadBackup(picker.FileName);if(DialogUI.Show(d,"Replace the saved library and settings with this backup, then restart Noctune? A recovery copy of current data will be kept.","Restore backup",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;d.Close();FormClosed+=delegate{try{ApplyBackup(data,Path.Combine(Catalog.Root,"data"));Process.Start(Application.ExecutablePath);}catch(Exception ex){DialogUI.Show(null,ex.Message,"Restore failed");}};Close();}catch(Exception ex){DialogUI.Show(d,ex.Message,"Invalid backup");}}});restore.SetBounds(210,229,170,40);d.Controls.Add(restore);d.ShowDialog(this);
    }}
}
