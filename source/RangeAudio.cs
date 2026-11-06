using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// The direct CDN requires bounded byte ranges. VLC can read and seek the
// resulting file through media callbacks without running a local HTTP server.
internal sealed class RangeAudio {
    const int Chunk=256*1024;
    static readonly HttpClient http=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};
    static readonly Dictionary<string,RangeAudio> known=new Dictionary<string,RangeAudio>();
    readonly Uri url;
    string videoId;int format;volatile Uri fullUrl;
    readonly byte[] first;
    readonly long length;
    readonly DateTime created=DateTime.UtcNow;
    RangeAudio(Uri uri,long size,byte[] bytes){url=uri;length=size;first=bytes;}
    internal static RangeAudio Find(string uri){
        lock(known){RangeAudio result;return known.TryGetValue(uri,out result)?result:null;}
    }
    static async Task<RangeAudio> ReadRange(Uri uri,long start,long end,long expected,CancellationToken token){
        using(var request=new HttpRequestMessage(HttpMethod.Get,uri)){
            request.Headers.Range=new RangeHeaderValue(start,end);
            using(var reply=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)){
                var range=reply.Content.Headers.ContentRange;
                if(reply.StatusCode!=HttpStatusCode.PartialContent||range==null||range.From!=start||!range.To.HasValue||
                   !range.Length.HasValue||range.Length<=0||range.To!=Math.Min(end,range.Length.Value-1)||
                   (expected>0&&range.Length!=expected))throw new IOException("Invalid audio range ("+(int)reply.StatusCode+", requested "+start+"-"+end+", received "+range+").");
                int count=checked((int)(range.To.Value-start+1));var data=new byte[count];
                using(var stream=await reply.Content.ReadAsStreamAsync().ConfigureAwait(false)){
                    for(int pos=0;pos<count;){
                        int n=await stream.ReadAsync(data,pos,count-pos,token).ConfigureAwait(false);
                        if(n<=0)throw new EndOfStreamException();pos+=n;
                    }
                }
                return new RangeAudio(uri,range.Length.Value,data);
            }
        }
    }
    internal static async Task Register(Uri uri,string id,int itag,CancellationToken token){
        var source=await ReadRange(uri,0,Chunk-1,0,token).ConfigureAwait(false);
        source.videoId=id;source.format=itag;
        lock(known){
            if(known.Count>=96){
                string oldest=null;var time=DateTime.MaxValue;
                foreach(var e in known)if(e.Value.created<time){oldest=e.Key;time=e.Value.created;}
                if(oldest!=null)known.Remove(oldest);
            }
            known[uri.AbsoluteUri]=source;
        }
    }
    async Task<Uri> FullStream(CancellationToken token){
        if(fullUrl!=null)return fullUrl;
        // Some direct links allow the opening bytes but reject later ranges.
        // Resolve the rest while those bytes are already playing.
        if(length>first.Length){
            try{await ReadRange(url,length-1,length-1,length,token).ConfigureAwait(false);fullUrl=url;return url;}
            catch(IOException){}catch(HttpRequestException){}
        }else{return url;}
        Uri replacement=await Catalog.FullFormat(videoId,format,token).ConfigureAwait(false);
        var prefix=await ReadRange(replacement,0,first.Length-1,length,token).ConfigureAwait(false);
        for(int i=0;i<first.Length;i++)if(first[i]!=prefix.first[i])throw new IOException("Audio representations did not match.");
        fullUrl=replacement;return replacement;
    }
    internal async Task CopyEncoded(Stream output,CancellationToken token){
        await output.WriteAsync(first,0,first.Length,token).ConfigureAwait(false);long at=first.Length;
        Task<Uri> continuation=FullStream(token);try{while(at<length){var target=fullUrl??url;RangeAudio block=null;try{block=await ReadRange(target,at,Math.Min(length-1,at+Chunk-1),length,token).ConfigureAwait(false);}catch(IOException){}catch(HttpRequestException){}if(block==null){block=await ReadRange(await continuation.ConfigureAwait(false),at,Math.Min(length-1,at+Chunk-1),length,token).ConfigureAwait(false);}await output.WriteAsync(block.first,0,block.first.Length,token).ConfigureAwait(false);at+=block.first.Length;}}finally{FfmpegAudio.Observe(continuation);}
    }
    internal sealed class Reader {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int OpenFn(IntPtr opaque,out IntPtr data,out ulong size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr ReadFn(IntPtr data,IntPtr buffer,UIntPtr len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SeekFn(IntPtr data,ulong offset);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void CloseFn(IntPtr data);
        [DllImport("libvlc.dll",CallingConvention=CallingConvention.Cdecl)]
        static extern IntPtr libvlc_media_new_callbacks(IntPtr instance,OpenFn open,ReadFn read,SeekFn seek,CloseFn close,IntPtr opaque);
        readonly OpenFn open;readonly ReadFn read;readonly SeekFn seek;readonly CloseFn close;
        readonly RangeAudio source;readonly CancellationTokenSource stop=new CancellationTokenSource();
        long at,blockStart;byte[] block;readonly Task<Uri> continuation;
        internal volatile bool Failed;
        internal Reader(RangeAudio source){this.source=source;open=Open;read=Read;seek=Seek;close=delegate{};continuation=source.FullStream(stop.Token);continuation.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        internal IntPtr Media(IntPtr vlc){return libvlc_media_new_callbacks(vlc,open,read,seek,close,IntPtr.Zero);}
        internal void Cancel(){stop.Cancel();}
        internal void Release(){stop.Dispose();}
        int Open(IntPtr unused,out IntPtr data,out ulong size){
            data=IntPtr.Zero;size=(ulong)source.length;at=blockStart=0;block=source.first;return 0;
        }
        int Seek(IntPtr unused,ulong offset){if(offset>(ulong)source.length)return -1;at=(long)offset;return 0;}
        byte[] GetBlock(Uri target,long start,long end){
            using(var budget=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){
                budget.CancelAfter(TimeSpan.FromSeconds(10));
                return ReadRange(target,start,end,source.length,budget.Token).GetAwaiter().GetResult().first;
            }
        }
        IntPtr Read(IntPtr unused,IntPtr buffer,UIntPtr len){
            try{
                if(stop.IsCancellationRequested)return new IntPtr(-1);
                if(at>=source.length||len.ToUInt64()==0)return IntPtr.Zero;
                if(at<blockStart||at>=blockStart+block.Length){
                    if(at<source.first.Length){block=source.first;blockStart=0;}
                    else{
                        long end=Math.Min(at+Chunk-1,source.length-1);
                        var target=source.fullUrl;
                        try{block=GetBlock(target??source.url,at,end);}
                        catch(IOException){
                            if(target!=null)throw;
                            block=GetBlock(continuation.GetAwaiter().GetResult(),at,end);
                        }
                        blockStart=at;
                    }
                }
                int offset=(int)(at-blockStart),n=(int)Math.Min((ulong)(block.Length-offset),len.ToUInt64());
                Marshal.Copy(block,offset,buffer,n);at+=n;return new IntPtr(n);
            }catch(Exception){if(!stop.IsCancellationRequested)Failed=true;return new IntPtr(-1);}
        }
    }
}
