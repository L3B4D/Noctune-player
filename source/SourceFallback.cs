using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
internal partial class Player {
    static string MatchName(string value){return Regex.Replace((value??"").Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"[^\p{L}\p{N}]","");}
    internal static bool SameRecording(Track wanted,Track candidate){var title=MatchName(wanted.Title);var other=MatchName(candidate.Title);var artist=MatchName(wanted.Artist);var theirs=MatchName(candidate.Artist);return title.Length>0&&title==other&&artist.Length>0&&(artist==theirs||theirs.StartsWith(artist+"topic"))&&(wanted.DurationSeconds<=0||candidate.DurationSeconds<=0||Math.Abs(wanted.DurationSeconds-candidate.DurationSeconds)<=5);}
    async Task<Tuple<string,Track>> ResolveQobuzPlayback(Track track,CancellationToken token){
        try{return Tuple.Create(await qobuz.Resolve(track.QobuzId,token),track);}catch(OperationCanceledException){throw;}catch(IOException){if(!featuresState.LosslessFallback)throw;}
        status.Text="Finding the same recording on YouTube Music…";var matches=await Catalog.Search(track.Title+" "+track.Artist,token);token.ThrowIfCancellationRequested();var match=matches.FirstOrDefault(t=>SameRecording(track,t));if(match==null)throw new IOException("Lossless unavailable; no matching YouTube recording was found.");return Tuple.Create(await Catalog.Resolve(match,token),match);
    }
}
