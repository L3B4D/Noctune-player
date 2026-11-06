using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static partial class Catalog {
    static async Task<object> MusicRequest(string endpoint,object payload,CancellationToken token){
        using(var content=new StringContent(Json().Serialize(payload),Encoding.UTF8,"application/json"))
        using(var reply=await Http.PostAsync("https://music.youtube.com/youtubei/v1/"+endpoint,content,token).ConfigureAwait(false)){
            reply.EnsureSuccessStatusCode();return Json().DeserializeObject(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
    }
    static object MusicContext(){return new {client=new {clientName="WEB_REMIX",clientVersion="1.20260213.01.00",hl="en"}};}
    static IEnumerable<object> Nodes(object root,string name){
        var dict=root as IDictionary<string,object>;
        if(dict!=null){foreach(var pair in dict){if(pair.Key==name)yield return pair.Value;
            else foreach(var item in Nodes(pair.Value,name))yield return item;}}
        else if(root is IList)foreach(var child in (IList)root)foreach(var item in Nodes(child,name))yield return item;
    }
    static string Runs(object node){
        var runs=Field(node,"runs") as IList;
        return runs==null?"":string.Concat(runs.Cast<object>().Select(r=>Convert.ToString(Field(r,"text"))));
    }
    internal static async Task<List<string>> Completions(string text,CancellationToken token){
        var data=await MusicRequest("music/get_search_suggestions",new {context=MusicContext(),input=text},token).ConfigureAwait(false);
        return Nodes(data,"searchSuggestionRenderer").Select(x=>Runs(Field(x,"suggestion"))).Where(x=>x.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
    }
    internal static async Task<List<Track>> Radio(Track seed,CancellationToken token){
        if(!Valid(seed.Id))return new List<Track>();
        var data=await MusicRequest("next",new {context=MusicContext(),videoId=seed.Id,playlistId="RDAMVM"+seed.Id,
            isAudioOnly=true,enablePersistentPlaylistPanel=true},token).ConfigureAwait(false);
        var seen=new HashSet<string>{seed.Id};var tracks=new List<Track>();
        foreach(var row in Nodes(data,"playlistPanelVideoRenderer")){
            string id=Convert.ToString(Field(row,"videoId")),title=Runs(Field(row,"title"));
            if(!Valid(id)||!seen.Add(id)||title.Length==0)continue;
            var images=Field(Field(row,"thumbnail"),"thumbnails") as IList;
            tracks.Add(new Track{Id=id,Title=title,Artist=Runs(Field(row,"shortBylineText")),Artwork=BestThumbnail(images)});
            if(tracks.Count==20)break;
        }
        return tracks;
    }
}

internal partial class Player {
    sealed class LibraryState{public bool ReduceMotion{get;set;}public List<Track> Recent{get;set;}public List<Track> Liked{get;set;}}
    readonly string statePath=Path.Combine(Catalog.Root,"data","library.json");
    readonly ListBox suggestions=new ListBox();
    CancellationTokenSource completionRequest,radioRequest,stationRequest;
    Track[] recommended;
    Track[] filteredDefaults;static readonly Track[] emptyRecommendations={new Track{Title="No suggestions to show",Artist="Change recommendation filters in Settings",Kind="Empty"}};
    Track[] HomePicks{get{if(recommended!=null&&recommended.Length>0)return recommended;if(featuresState.Hidden==null||featuresState.BlockedArtists==null||featuresState.Hidden.Count+featuresState.BlockedArtists.Count==0)return picks;if(filteredDefaults==null)filteredDefaults=picks.Where(Recommendable).ToArray();return filteredDefaults.Length>0?filteredDefaults:emptyRecommendations;}}
    string recommendationSeed,recommendationArtist;
    ArtButton stationButton;
    Label homeTitle,homeReason;Action layoutHome;internal static bool reduceMotion;
    float cardReveal=1;
    readonly System.Windows.Forms.Timer revealTimer=new System.Windows.Forms.Timer{Interval=16};
    readonly System.Diagnostics.Stopwatch revealWatch=new System.Diagnostics.Stopwatch();
    void PaintPicks(){foreach(var card in features)if(card!=null&&card.Visible)card.Invalidate();}
    void AnimateCards(bool fade=true){
        foreach(var t in Enumerable.Range(0,Math.Min(4,HomePicks.Length)).Select(i=>HomePicks[(hero+i)%HomePicks.Length]))FetchArtwork(t);
        if(!fade||reduceMotion||!SystemInformation.IsMenuAnimationEnabled){revealTimer.Stop();cardReveal=1;PaintPicks();return;}
        cardReveal=0;revealWatch.Restart();revealTimer.Start();PaintPicks();
    }
    void InitDiscover(){
        LoadLibrary();
        revealTimer.Tick+=delegate{cardReveal=Math.Min(1,(float)revealWatch.Elapsed.TotalMilliseconds/165);PaintPicks();if(cardReveal>=1)revealTimer.Stop();};
        suggestions.Visible=false;suggestions.BackColor=Theme.Card;suggestions.ForeColor=Theme.Paper;suggestions.BorderStyle=BorderStyle.FixedSingle;
        suggestions.Font=Theme.Font(11);suggestions.IntegralHeight=false;suggestions.ItemHeight=26;suggestions.DrawMode=DrawMode.OwnerDrawFixed;suggestions.AccessibleName="Suggested searches";
        suggestions.DrawItem+=delegate(object s,DrawItemEventArgs e){if(e.Index<0||e.Index>=suggestions.Items.Count)return;
            bool selected=(e.State&DrawItemState.Selected)!=0;using(var b=new SolidBrush(selected?blue:Theme.Card))e.Graphics.FillRectangle(b,e.Bounds);
            Glyph(e.Graphics,"\uE721",new Rectangle(e.Bounds.X+6,e.Bounds.Y,26,e.Bounds.Height),Theme.Muted,10);
            TextAt(e.Graphics,(string)suggestions.Items[e.Index],new Rectangle(e.Bounds.X+38,e.Bounds.Y,e.Bounds.Width-42,e.Bounds.Height),10,Theme.Paper);
        };
        searchPage.Controls.Add(suggestions);suggestions.BringToFront();
        query.TextChanged+=async delegate{await Suggest();};
        query.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Down&&suggestions.Visible){suggestions.Focus();suggestions.SelectedIndex=0;e.SuppressKeyPress=true;}};
        suggestions.MouseClick+=delegate(object s,MouseEventArgs e){if(suggestions.IndexFromPoint(e.Location)>=0)ChooseSuggestion();};
        suggestions.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Enter){ChooseSuggestion();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){DismissSuggestions();query.Focus();e.SuppressKeyPress=true;}};
        searchPage.Resize+=delegate{LayoutSuggestions();};searchPage.VisibleChanged+=delegate{if(!searchPage.Visible)DismissSuggestions();};
        query.Leave+=delegate{if(!closing)BeginInvoke((Action)delegate{if(!suggestions.Focused&&!query.Focused)DismissSuggestions();});};
        InstallTrackMenu(results);InstallTrackMenu(libraryRows);
        Shown+=delegate{foreach(var t in picks)FetchArtwork(t);foreach(var t in recent.Concat(liked).Take(8))FetchArtwork(t);Track seed=recent.Concat(liked).FirstOrDefault(t=>!string.IsNullOrEmpty(t.Id));if(seed!=null)RefreshPersonalMix();};
        FormClosing+=delegate{DismissSuggestions();if(radioRequest!=null)radioRequest.Cancel();if(stationRequest!=null)stationRequest.Cancel();revealTimer.Dispose();};
    }
    void LoadLibrary(){
        if(!File.Exists(statePath))return;
        try{var state=Catalog.Json().Deserialize<LibraryState>(File.ReadAllText(statePath));if(state==null)return;reduceMotion=state.ReduceMotion;
            if(state.Recent!=null)recent.AddRange(state.Recent.Where(t=>t!=null&&Key(t)!=null).Take(50));
            if(state.Liked!=null)liked.AddRange(state.Liked.Where(t=>t!=null&&Key(t)!=null));
        }catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException))throw;}
    }
    void SaveLibrary(){
        try{Directory.CreateDirectory(Path.GetDirectoryName(statePath));string temp=statePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,Catalog.Json().Serialize(new LibraryState{Recent=recent,Liked=liked,ReduceMotion=reduceMotion}));if(File.Exists(statePath))File.Replace(temp,statePath,null);else File.Move(temp,statePath);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }catch(IOException){libraryTitle.Text="Library changes could not be saved.";}catch(UnauthorizedAccessException){libraryTitle.Text="Library folder is read-only.";}
    }
    void DismissSuggestions(){if(completionRequest!=null){completionRequest.Cancel();completionRequest=null;}suggestions.Hide();}
    void LayoutSuggestions(){Place(suggestions,searchField.Left,searchField.Bottom+3,searchField.Width,Math.Min(suggestions.Items.Count*26+3,Math.Max(28,searchPage.Height-searchField.Bottom-14)));}
    async Task Suggest(){
        DismissSuggestions();string term=query.Text.Trim();if(closing||page!="Search"||useQobuz||term.Length<2)return;
        var req=new CancellationTokenSource();completionRequest=req;
        try{await Task.Delay(260,req.Token);var phrases=await Catalog.Completions(term,req.Token);
            if(closing||req!=completionRequest||page!="Search"||useQobuz||!query.Focused)return;
            suggestions.BeginUpdate();try{suggestions.Items.Clear();foreach(var phrase in phrases)suggestions.Items.Add(phrase);LayoutSuggestions();}finally{suggestions.EndUpdate();}
            suggestions.Visible=phrases.Count>0;if(suggestions.Visible)suggestions.BringToFront();
        }catch(OperationCanceledException){}catch(HttpRequestException){}catch(ArgumentException){}catch(InvalidOperationException){}
        finally{if(completionRequest==req)completionRequest=null;req.Dispose();}
    }
    void ChooseSuggestion(){var text=suggestions.SelectedItem as string;if(text==null)return;query.Text=text;DismissSuggestions();query.Focus();RunSearch();}
    async void UpdatePicks(Track seed){
        if(string.IsNullOrEmpty(seed.Id)||seed.Id==recommendationSeed||closing)return;
        recommendationSeed=seed.Id;if(radioRequest!=null)radioRequest.Cancel();var req=new CancellationTokenSource();radioRequest=req;
        try{var tracks=await Catalog.Radio(seed,req.Token);if(closing||req!=radioRequest||tracks.Count==0)return;
            recommended=DiverseRecommendations(tracks).Take(12).ToArray();if(recommended.Length==0)recommended=null;recommendationArtist=seed.Artist;hero=0;
            homeTitle.Text="Quick picks";homeReason.Text="Because you listened to "+seed.Title+" · "+recommendationArtist;help.SetToolTip(homeTitle,homeReason.Text);homeReason.Visible=true;layoutHome();
            foreach(var t in HomePicks.Take(4))FetchArtwork(t);AnimateCards();
        }catch(OperationCanceledException){}catch(HttpRequestException){}catch(ArgumentException){}catch(InvalidOperationException){}
        finally{if(req==radioRequest)radioRequest=null;req.Dispose();}
    }
    void InstallTrackMenu(ListView list){
        list.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Right)return;var item=list.GetItemAt(e.X,e.Y);if(item==null)return;item.Selected=true;ShowTrackMenu((Track)item.Tag,list,e.Location);};
    }
    void ShowTrackMenu(Track t,Control owner,Point point){
        if(!string.IsNullOrEmpty(t.Kind)){OpenCollection(t);return;}
        var m=new DarkMenu{BackColor=Theme.Card,ForeColor=Theme.Paper};
        m.Items.Add("Play",null,delegate{Add(t,true);});AddTrackFeatures(m,t);
        m.Items.Add("Play next",null,delegate{ResetTransition();int at=index<0?queue.Count:index+1;queue.Insert(at,t);queueView.Items.Insert(at,Row(t));queueEmpty.Visible=false;ScrollVisible(queueView,true);queueTitle.Text="Queue  ·  "+queue.Count;});
        m.Items.Add("Add to queue",null,delegate{Add(t,false);});if(!string.IsNullOrEmpty(t.Id))m.Items.Add("Song radio",null,delegate{StartStationFor(t);});
        bool isLiked=liked.Exists(x=>Key(x)==Key(t));m.Items.Add(isLiked?"Remove from liked songs":"Like song",null,delegate{if(isLiked)liked.RemoveAll(x=>Key(x)==Key(t));else{MarkLibraryAdded(t);liked.Add(t);UpdatePicks(t);}SaveLibrary();if(page=="Library")FillLibrary();});
        m.Closed+=delegate{if(!closing)BeginInvoke((Action)m.Dispose);else m.Dispose();};TrackGpuOptions(m,owner);m.Show(owner,point);
    }
    void StartStation(){StartStationFor(Current);}
    async void StartStationFor(Track seed){if(string.IsNullOrEmpty(seed.Id)||stationRequest!=null)return;
        var req=new CancellationTokenSource();stationRequest=req;stationButton.Enabled=false;stationButton.Text="Loading…";stationButton.Invalidate();
        try{var tracks=await Catalog.Radio(seed,req.Token);if(closing||req.IsCancellationRequested)return;
            var known=new HashSet<string>(queue.Select(Key));int first=queue.Count;
            foreach(var t in tracks.Where(t=>known.Add(Key(t))).Take(12))Add(t,false);
            if(queue.Count==first){DialogUI.Show(this,"No new radio tracks were available for this song.","Song radio");return;}
            if(!active&&starting==null)Play(first);ShowPage("Queue");
        }catch(OperationCanceledException){}catch(Exception){if(!closing)DialogUI.Show(this,"Song radio could not load. Try again.","Song radio");}
        finally{stationRequest=null;req.Dispose();if(!closing){stationButton.Enabled=true;stationButton.Text="Song radio";stationButton.Invalidate();}}
    }
}
