using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Search filter values and browse structure follow the bundled GPLv3 Yuma source.
internal static partial class Catalog {
    static string BrowseId(object endpoint){return Convert.ToString(Field(Field(endpoint,"browseEndpoint"),"browseId"));}
    internal static void Metadata(Track track,object row){
        foreach(var runs in Nodes(Field(row,"fixedColumns"),"runs").OfType<IList>()){
            string text=string.Concat(runs.Cast<object>().Select(x=>Convert.ToString(Field(x,"text"))));
            var parts=text.Split(':');int value=0,n;bool valid=parts.Length==2||parts.Length==3;
            foreach(var part in parts){if(!int.TryParse(part,out n)||n<0){valid=false;break;}value=value*60+n;}
            if(valid)track.DurationSeconds=value;
        }
        if(track.DurationSeconds==0){foreach(var runs in Nodes(Field(row,"flexColumns"),"runs").OfType<IList>()){
            string text=string.Concat(runs.Cast<object>().Select(x=>Convert.ToString(Field(x,"text"))));
            var time=System.Text.RegularExpressions.Regex.Match(text,@"(?:^|[·•]\s*)(\d{1,2}:\d{2}(?::\d{2})?)$");
            if(time.Success){int seconds=0;foreach(var part in time.Groups[1].Value.Split(':'))seconds=seconds*60+int.Parse(part);track.DurationSeconds=seconds;}
        }}
        var year=System.Text.RegularExpressions.Regex.Match(Runs(Field(row,"subtitle")),@"\b(19|20)\d{2}\b");if(year.Success)track.Year=int.Parse(year.Value);

        foreach(var run in Nodes(Field(row,"flexColumns"),"runs").OfType<IList>().SelectMany(x=>x.Cast<object>())){
            string id=BrowseId(Field(run,"navigationEndpoint"));
            if(id.StartsWith("UC")){if(string.IsNullOrEmpty(track.ArtistId))track.ArtistId=id;}
            if(id.StartsWith("MPRE")){track.AlbumId=id;track.Album=Convert.ToString(Field(run,"text"));}
        }
    }
    static Track Entity(object row){
        string title=Column(row,0);if(title.Length==0)title=Runs(Field(row,"title"));
        string id=Convert.ToString(Field(Field(row,"playlistItemData"),"videoId"));
        if(!Valid(id))id=Convert.ToString(Field(Field(Field(row,"navigationEndpoint"),"watchEndpoint"),"videoId"));
        string browse=BrowseId(Field(row,"navigationEndpoint"));
        if(browse.Length==0){var cols=Field(row,"flexColumns") as IList;if(cols!=null&&cols.Count>0){var runs=Field(Field(Field(cols[0],"musicResponsiveListItemFlexColumnRenderer"),"text"),"runs") as IList;if(runs!=null&&runs.Count>0)browse=BrowseId(Field(runs[0],"navigationEndpoint"));}}
        var thumbs=Nodes(row,"thumbnails").OfType<IList>().FirstOrDefault();
        if(title.Length==0||(!Valid(id)&&browse.Length==0))return null;
        var t=new Track{Title=title,Artist=Column(row,1),Id=Valid(id)?id:null,Artwork=BestThumbnail(thumbs)};
        if(t.Artist.Length==0)t.Artist=Runs(Field(row,"subtitle"));
        if(!Valid(id)){t.BrowseId=browse;t.Kind=browse.StartsWith("UC")?"Artist":browse.StartsWith("MPRE")?"Album":"Playlist";}
        Metadata(t,row);return t;
    }
    static List<Track> Entities(object data){
        var found=new List<Track>();var seen=new HashSet<string>();
        foreach(string renderer in new[]{"musicResponsiveListItemRenderer","musicTwoRowItemRenderer"})foreach(var row in Nodes(data,renderer)){
            var t=Entity(row);if(t!=null&&seen.Add(t.Id??t.BrowseId))found.Add(t);
        }
        return found;
    }
    internal static async Task<List<Track>> SearchEntities(string query,string kind,CancellationToken token){
        string filter=kind=="Artists"?"EgWKAQIgAWoKEAkQChAFEAMQBA%3D%3D":kind=="Albums"?"EgWKAQIYAWoKEAkQChAFEAMQBA%3D%3D":"EgeKAQQoAEABagoQAxAEEAoQCRAF";
        return Entities(await MusicRequest("search",new{context=MusicContext(),query=query,@params=filter},token).ConfigureAwait(false));
    }
    internal static async Task<List<Track>> Album(Track album,CancellationToken token){
        var data=await MusicRequest("browse",new{context=MusicContext(),browseId=album.BrowseId},token).ConfigureAwait(false);
        var header=Nodes(data,"musicResponsiveHeaderRenderer").FirstOrDefault()??Nodes(data,"musicDetailHeaderRenderer").FirstOrDefault();
        string artwork=BestThumbnail(Nodes(Field(header,"thumbnail"),"thumbnails").OfType<IList>().FirstOrDefault());if(!string.IsNullOrEmpty(artwork))album.Artwork=artwork;
        var artistRuns=Field(Field(header,"straplineTextOne"),"runs") as IList;
        if(artistRuns==null)artistRuns=Field(Field(header,"subtitle"),"runs") as IList;
        if(artistRuns!=null)foreach(var run in artistRuns){string id=BrowseId(Field(run,"navigationEndpoint"));if(id.StartsWith("UC")){album.Artist=Convert.ToString(Field(run,"text"));album.ArtistId=id;break;}}
        var year=System.Text.RegularExpressions.Regex.Match(Runs(Field(header,"subtitle")),@"\b(19|20)\d{2}\b");if(year.Success)album.Year=int.Parse(year.Value);
        var songs=new List<Track>();var seen=new HashSet<string>();
        foreach(var shelf in Nodes(data,"musicShelfRenderer").Concat(Nodes(data,"musicPlaylistShelfRenderer")))foreach(var track in Entities(Field(shelf,"contents"))){
            if(!string.IsNullOrEmpty(track.Kind)||!Valid(track.Id)||!seen.Add(track.Id))continue;
            if(string.IsNullOrEmpty(track.Artwork))track.Artwork=album.Artwork;if(string.IsNullOrEmpty(track.Artist))track.Artist=album.Artist;
            if(string.IsNullOrEmpty(track.ArtistId))track.ArtistId=album.ArtistId;track.AlbumId=album.BrowseId;track.Album=album.Title;songs.Add(track);
        }
        return songs;
    }
    internal sealed class ArtistDetails {internal string Description,Artwork;internal List<Track> Tracks=new List<Track>();}
    internal static async Task<ArtistDetails> Artist(string id,CancellationToken token){
        var data=await MusicRequest("browse",new{context=MusicContext(),browseId=id},token).ConfigureAwait(false);
        var result=new ArtistDetails();var header=Field(data,"header");
        result.Artwork=BestThumbnail(Nodes(header,"thumbnails").OfType<IList>().FirstOrDefault());
        result.Description=Nodes(header,"description").Select(Runs).FirstOrDefault(x=>x.Length>0)??"";
        var seen=new HashSet<string>();
        foreach(var shelf in Nodes(Field(data,"contents"),"musicShelfRenderer")){
            string heading=Runs(Field(shelf,"title"));
            foreach(var track in Entities(Field(shelf,"contents"))){track.Section=heading;if(seen.Add(track.Id??track.BrowseId))result.Tracks.Add(track);}
        }
        foreach(var shelf in Nodes(Field(data,"contents"),"musicCarouselShelfRenderer")){
            string heading=Nodes(Field(shelf,"header"),"title").Select(Runs).FirstOrDefault()??"";
            foreach(var track in Entities(Field(shelf,"contents"))){track.Section=heading;if(seen.Add(track.Id??track.BrowseId))result.Tracks.Add(track);}
        }
        foreach(var track in Entities(Field(data,"contents")))if(seen.Add(track.Id??track.BrowseId))result.Tracks.Add(track);
        return result;
    }
    internal static async Task<List<Track>> MoodCategories(CancellationToken token){
        var data=await MusicRequest("browse",new{context=MusicContext(),browseId="FEmusic_moods_and_genres"},token).ConfigureAwait(false);
        var result=new List<Track>();
        foreach(var row in Nodes(data,"musicNavigationButtonRenderer")){
            var endpoint=Field(Field(row,"clickCommand"),"browseEndpoint");
            string id=Convert.ToString(Field(endpoint,"browseId")),title=Runs(Field(row,"buttonText"));
            if(id.Length>0&&title.Length>0)result.Add(new Track{Title=title,Artist="YouTube Music",Kind="Mood",BrowseId=id,BrowseParams=Convert.ToString(Field(endpoint,"params"))});
        }
        return result;
    }
    internal static async Task ResolveMoodArtwork(Track mood,CancellationToken token){
        var data=await MusicRequest("browse",new{context=MusicContext(),browseId=mood.BrowseId,@params=mood.BrowseParams},token).ConfigureAwait(false);
        // Same source order as Yuma's BrowseResult: category header, then category contents.
        var header=Field(data,"header");
        foreach(var name in new[]{"musicImmersiveHeaderRenderer","musicVisualHeaderRenderer","musicDetailHeaderRenderer","musicResponsiveHeaderRenderer","musicHeaderRenderer"}){
            var renderer=Nodes(header,name).FirstOrDefault();
            var thumbnails=Nodes(renderer,"thumbnails").OfType<IList>().FirstOrDefault();
            var url=BestThumbnail(thumbnails);if(!string.IsNullOrEmpty(url)){mood.Artwork=url;return;}
        }
        mood.Artwork=Entities(Field(data,"contents")).Select(x=>x.Artwork).FirstOrDefault(x=>!string.IsNullOrEmpty(x));
    }
    internal static async Task<List<Track>> Browse(string id,CancellationToken token,string parameters=null){
        var data=await MusicRequest("browse",new{context=MusicContext(),browseId=id,@params=parameters},token).ConfigureAwait(false);
        var rows=Entities(data);var tokens=new HashSet<string>();
        for(int page=0;page<10;page++){
            string continuation=Nodes(data,"nextContinuationData").Select(x=>Convert.ToString(Field(x,"continuation"))).FirstOrDefault();
            if(string.IsNullOrEmpty(continuation))continuation=Nodes(data,"continuationCommand").Select(x=>Convert.ToString(Field(x,"token"))).FirstOrDefault();
            if(string.IsNullOrEmpty(continuation)||!tokens.Add(continuation))break;
            data=await MusicRequest("browse",new{context=MusicContext(),continuation=continuation},token).ConfigureAwait(false);rows.AddRange(Entities(data));
        }
        return rows;
    }
}
