using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
internal static partial class Catalog {
    internal static Task<string> DownloadTrack(Track t,string root,string name,CancellationToken token){return DownloadWithProgress(t,root,name,token,null,0);}
    internal static async Task<string> DownloadWithProgress(Track t,string root,string name,CancellationToken token,Action<string> progress,long maxBytes){
        if(!Valid(t.Id))throw new IOException("Only playable YouTube Music tracks can be downloaded here.");
        var args=new List<string>{"--newline","--progress","--progress-template","YUMA_PROGRESS:%(progress._percent_str)s","--no-warnings","-f","bestaudio[ext=m4a]/bestaudio","--output",Path.Combine(root,name+".%(ext)s")};if(maxBytes>0){args.Add("--max-filesize");args.Add(maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));}args.Add("https://www.youtube.com/watch?v="+t.Id);await ToolProgress(token,progress??delegate{},args.ToArray()).ConfigureAwait(false);
        string file=Directory.GetFiles(root,name+".*").FirstOrDefault(p=>!p.EndsWith(".part")&&!p.EndsWith(".ytdl"));
        if(file==null||new FileInfo(file).Length==0)throw new IOException("No completed download was produced.");return file;
    }
}
internal partial class Player {
    public sealed class OfflineEntry{public string Key{get;set;}public Track Track{get;set;}public string Path{get;set;}}
    readonly string offlineRoot=Path.Combine(Catalog.Root,"data","offline");
    List<OfflineEntry> offline=new List<OfflineEntry>();
    readonly Dictionary<string,string> downloadNames=new Dictionary<string,string>();
    readonly Dictionary<string,CancellationTokenSource> downloads=new Dictionary<string,CancellationTokenSource>();
    void InitOffline(){try{string file=Path.Combine(offlineRoot,"index.json");if(File.Exists(file))offline=Catalog.Json().Deserialize<List<OfflineEntry>>(File.ReadAllText(file))??new List<OfflineEntry>();}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}offline.RemoveAll(x=>x==null||x.Track==null||!OwnedOffline(x.Path));FormClosing+=delegate{foreach(var c in downloads.Values)c.Cancel();};}
    bool OwnedOffline(string path){try{return !string.IsNullOrEmpty(path)&&Path.GetFullPath(path).StartsWith(Path.GetFullPath(offlineRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}}
    string OfflinePath(Track t){var found=offline.Find(x=>x.Key==Key(t)&&File.Exists(x.Path));return found==null?null:found.Path;}
    IEnumerable<Track> OfflineTracks(){return offline.Where(x=>File.Exists(x.Path)).Select(x=>{var t=Catalog.Json().Deserialize<Track>(Catalog.Json().Serialize(x.Track));t.File=x.Path;return t;});}
    void SaveOffline(){Directory.CreateDirectory(offlineRoot);string file=Path.Combine(offlineRoot,"index.json"),tmp=file+".tmp";File.WriteAllText(tmp,Catalog.Json().Serialize(offline));if(File.Exists(file))File.Replace(tmp,file,null);else File.Move(tmp,file);}
    void AddOfflineMenu(ContextMenuStrip menu,Track track){
        string key=Key(track);if(downloads.ContainsKey(key)){menu.Items.Add("Cancel download",null,delegate{CancellationTokenSource c;if(downloads.TryGetValue(key,out c))c.Cancel();});return;}
        if(OfflinePath(track)!=null||OwnedOffline(track.File)){menu.Items.Add("Available offline").Enabled=false;return;}
        if(track.QobuzId>0&&qobuz==null){menu.Items.Add("Connect Qobuz to download FLAC").Enabled=false;return;}
        menu.Items.Add("Save offline",null,delegate{DownloadTrack(track);});
    }
    async void DownloadTrack(Track track,bool background=false){
        string key=Key(track);if(key==null||downloads.ContainsKey(key)||OfflinePath(track)!=null)return;
        if(downloads.Count>=2){if(!background)DialogUI.Show(this,"Two downloads are already running. Wait for one to finish.","Downloads");return;}long room=featuresState.DownloadLimitMb*1048576L-OfflineBytes();if(room<1048576){if(!background)DialogUI.Show(this,"The download storage limit is reached. Open Downloads & storage to free space or raise the limit.","Storage full");return;}failedDownloads.Remove(key);var cancel=new CancellationTokenSource();downloads.Add(key,cancel);downloadNames[key]=track.Title;string name=Guid.NewGuid().ToString("N");Directory.CreateDirectory(offlineRoot);downloadPrefixes[key]=name;var quota=new System.Windows.Forms.Timer{Interval=500};quota.Tick+=delegate{if(OfflineBytes()>featuresState.DownloadLimitMb*1048576L)cancel.Cancel();};quota.Start();if(!background)status.Text="Downloading “"+track.Title+"”…";
        try{
            string path;if(!string.IsNullOrEmpty(track.File)){if(new FileInfo(track.File).Length>room)throw new IOException("This file exceeds the remaining download storage limit.");path=Path.Combine(offlineRoot,name+Path.GetExtension(track.File));await Task.Run(()=>File.Copy(track.File,path),cancel.Token);}else if(track.QobuzId>0){if(qobuz==null)throw new IOException("Connect Qobuz first.");path=await qobuz.Download(track.QobuzId,Path.Combine(offlineRoot,name+".flac"),cancel.Token);}else path=await Catalog.DownloadWithProgress(track,offlineRoot,name,cancel.Token,line=>RecordDownloadProgress(key,line),room);
            cancel.Token.ThrowIfCancellationRequested();if(closing)return;
            offline.Add(new OfflineEntry{Key=key,Track=track,Path=path});SaveOffline();if(!background)status.Text="Saved offline: "+track.Title;
            if(page=="Library"&&listMode=="Offline")FillLibrary();if(tray!=null&&!background)tray.ShowBalloonTip(2500,"Available offline",track.Title,ToolTipIcon.Info);
        }catch(OperationCanceledException){if(!closing&&!background)status.Text="Download cancelled.";foreach(var path in Directory.GetFiles(offlineRoot,name+".*"))if(OwnedOffline(path))try{File.Delete(path);}catch(IOException){}}
        catch(Exception ex){if(failedDownloads.Count>=30)failedDownloads.Clear();failedDownloads[key]=track;if(!closing){if(!background)status.Text="Download failed: "+ex.Message;if(!background)DialogUI.Show(this,ex.Message,"Download failed");}}
        finally{quota.Dispose();downloadProgress.Remove(key);downloadPrefixes.Remove(key);downloads.Remove(key);downloadNames.Remove(key);cancel.Dispose();foreach(var path in Directory.GetFiles(offlineRoot,name+".*"))if(OwnedOffline(path)&&!offline.Exists(x=>x.Path==path))try{File.Delete(path);}catch(IOException){}}
    }
    void ShowOfflineStorage(){ShowDownloads();}
}
