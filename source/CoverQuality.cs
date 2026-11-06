using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
internal static class CoverQuality {
    internal static string HighResolution(string url){
        Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https")return url;
        if(uri.Host.EndsWith(".googleusercontent.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".ggpht.com",StringComparison.OrdinalIgnoreCase)){
            return Regex.Replace(Regex.Replace(url,@"=w\d+-h\d+[^?]*","=w1600-h1600-l100-rj"),@"=s\d+[^?]*","=s1600-l100-rj");
        }
        return url;
    }
    internal static string ThumbnailUrl(string url){return HighResolution(url).Replace("=w1600-h1600-l100-rj","=w512-h512-l90-rj").Replace("=s1600-l100-rj","=s512-l90-rj");}
    static readonly string cacheRoot=Path.Combine(Catalog.Root,"data","artwork-cache-v1");
    static readonly object cacheLock=new object();static DateTime nextTrim;
    static string CachePath(string url){using(var hash=System.Security.Cryptography.SHA256.Create())return Path.Combine(cacheRoot,BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url))).Replace("-","")+".art");}
    static Bitmap Decode(byte[] bytes){using(var stream=new MemoryStream(bytes))using(var image=Image.FromStream(stream)){
        if(image.Width>4096||image.Height>4096)return null;return new Bitmap(image);
    }}
    static void SaveCache(string path,byte[] bytes){
        try{lock(cacheLock){Directory.CreateDirectory(cacheRoot);File.WriteAllBytes(path,bytes);
            if(DateTime.UtcNow<nextTrim)return;nextTrim=DateTime.UtcNow.AddMinutes(5);
            var files=new DirectoryInfo(cacheRoot).GetFiles("*.art");Array.Sort(files,(a,b)=>a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));long size=0;foreach(var f in files)size+=f.Length;
            foreach(var f in files){if(size<=160L*1024*1024)break;long length=f.Length;f.Delete();size-=length;}
        }}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    internal static Task<Bitmap> Load(string original,HttpClient client){return LoadSize(original,client,true);}
    internal static async Task<Bitmap> LoadSize(string original,HttpClient client,bool full){
        string best=full?HighResolution(original):ThumbnailUrl(original);
        foreach(string url in best==original?new[]{original}:new[]{best,original}){
            string path=CachePath(url);
            // File reads and decoding stay off the UI thread, including warm-cache loads.
            var cached=await Task.Run(delegate{
                try{if(File.Exists(path)){var bitmap=Decode(File.ReadAllBytes(path));if(bitmap!=null){try{File.SetLastWriteTimeUtc(path,DateTime.UtcNow);}catch(IOException){}catch(UnauthorizedAccessException){}return bitmap;}}}
                catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(OutOfMemoryException){}
                return null;
            }).ConfigureAwait(false);
            if(cached!=null)return cached;
            try{
                var point=System.Net.ServicePointManager.FindServicePoint(new Uri(url));if(point.ConnectionLimit<8)point.ConnectionLimit=8;
                var bytes=await client.GetByteArrayAsync(url).ConfigureAwait(false);
                return await Task.Run(delegate{var image=Decode(bytes);if(image!=null)SaveCache(path,bytes);return image;}).ConfigureAwait(false);
            }catch(IOException){}catch(HttpRequestException){}catch(TaskCanceledException){}catch(ArgumentException){}catch(OutOfMemoryException){}
        }
        return null;
    }

}

internal partial class Player {
    readonly System.Collections.Generic.Dictionary<string,long> artAccess=new System.Collections.Generic.Dictionary<string,long>();long artClock;
    void TrimArtwork(){
        long bytes=0;foreach(var image in images.Values)bytes+=(long)image.Width*image.Height*4;
        while(bytes>256L*1024*1024){
            string victim=null;long oldest=long.MaxValue;
            foreach(var pair in images){
                if(!qualityLoaded.Contains(pair.Key)||pair.Key==Key(Current)||pair.Value==previousCover||(displayedCollection!=null&&pair.Key==Key(displayedCollection)))continue;
                bool bundled=false;foreach(var pick in picks)if(Key(pick)==pair.Key){bundled=true;break;}if(bundled)continue;
                long accessed;if(!artAccess.TryGetValue(pair.Key,out accessed))accessed=0;if(accessed<oldest){oldest=accessed;victim=pair.Key;}
            }
            if(victim==null)break;var image=images[victim];bytes-=(long)image.Width*image.Height*4;images.Remove(victim);qualityLoaded.Remove(victim);fullArtwork.Remove(victim);artAccess.Remove(victim);
            for(var node=thumbnails.First;node!=null;){var next=node.Next;if(node.Value.Source==image){node.Value.Pixels.Dispose();thumbnails.Remove(node);}node=next;}
            for(var node=cardFrames.First;node!=null;){var next=node.Next;if(node.Value.Art==image){node.Value.Pixels.Dispose();cardFrames.Remove(node);}node=next;}
            if(layerArt==image){if(detailLayer!=null)detailLayer.Dispose();detailLayer=null;layerArt=null;}
            image.Dispose();
        }
    }
}
