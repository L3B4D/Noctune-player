using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal partial class Player {
    readonly Surface lyricsPage=new Surface();
    readonly LyricsList lyricRows=new LyricsList();
    readonly List<long> lineStarts=new List<long>();
    string lyricKey;int lyricVersion,highlighted=-1;CancellationTokenSource lyricRequest;
    Label lyricSource;DateTime lyricManualUntil;
    void BuildLyrics(){
        int previousLine=-1;var highlightMotion=new Motion(delegate{if(lyricRows.Visible)lyricRows.Invalidate();});lyricRows.Disposed+=delegate{highlightMotion.Dispose();};
        lyricsPage.BackColor=Theme.Ink;
        var bar=Make(lyricsPage,"Back to player",delegate{ShowPage("Player");},delegate(Graphics g,Rectangle r){PaintGlass(g,new Rectangle(1,1,r.Width-3,r.Height-3),24,ArtworkTone(Cover(Current)));Glyph(g,"\uE70D",new Rectangle(6,0,37,r.Height),Theme.Paper,12);Artwork(g,Cover(Current),new Rectangle(48,8,40,40),20);TextAt(g,Current.Title,new Rectangle(103,5,r.Width-119,27),12,Theme.Paper,true);TextAt(g,Current.Artist,new Rectangle(103,31,r.Width-119,20),9,Theme.Muted);});
        lyricSource=LabelOn(lyricsPage,"",9);lyricSource.ForeColor=Theme.Muted;lyricSource.AutoEllipsis=true;
        var retry=Pill(lyricsPage,"Retry lyrics",delegate{lyricKey=null;LoadLyrics(true);});
        var follow=lyricFollow=Pill(lyricsPage,"Follow song",delegate{lyricManualUntil=DateTime.MinValue;lastFollow=-1;if(highlighted>=0)lyricRows.TopIndex=Math.Max(0,highlighted-2);});
        ArtButton options=null;options=Pill(lyricsPage,"•••",delegate{LyricOptions(options);});lyricRows.PaintBackdrop=delegate(Graphics g,Rectangle area){var saved=g.Save();g.SetClip(area);var origin=lyricsPage.PointToClient(lyricRows.PointToScreen(Point.Empty));g.TranslateTransform(-origin.X,-origin.Y);PaintImmersiveBackdrop(g,lyricsPage.ClientRectangle);g.Restore(saved);};lyricRows.DrawMode=DrawMode.OwnerDrawVariable;lyricRows.IntegralHeight=false;lyricRows.BorderStyle=BorderStyle.None;lyricRows.BackColor=Theme.Ink;lyricRows.AccessibleName="Lyrics — double-click a synced line to seek";lyricsPage.Controls.Add(lyricRows);
        lyricRows.MeasureItem+=delegate(object s,MeasureItemEventArgs e){if(e.Index>=0&&e.Index<lyricRows.Items.Count)e.ItemHeight=Math.Max(86,TextRenderer.MeasureText(lyricRows.Items[e.Index].ToString(),lyricRows.Font,new Size(Math.Max(30,lyricRows.Width-SystemInformation.VerticalScrollBarWidth-48),2000),TextFormatFlags.WordBreak|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Height+28);};
        lyricRows.DrawItem+=delegate(object s,DrawItemEventArgs e){if(e.Index<0||e.Index>=lyricRows.Items.Count)return;var state=e.Graphics.Save();e.Graphics.SetClip(e.Bounds);var origin=lyricsPage.PointToClient(lyricRows.PointToScreen(Point.Empty));e.Graphics.TranslateTransform(-origin.X,-origin.Y);PaintImmersiveBackdrop(e.Graphics,lyricsPage.ClientRectangle);e.Graphics.Restore(state);var r=e.Bounds;r.Width-=SystemInformation.VerticalScrollBarWidth;r.Inflate(-20,-12);int distance=Math.Abs(e.Index-highlighted);Color muted=distance<=1?Color.FromArgb(188,195,206):Color.FromArgb(139,151,165);Color color=e.Index==highlighted?Motion.Mix(muted,Color.White,highlightMotion.Value):e.Index==previousLine?Motion.Mix(Color.White,muted,highlightMotion.Value):muted;DrawLyricText(e.Graphics,r,e.Index,color);};
        lyricRows.MouseWheel+=delegate{lyricManualUntil=DateTime.UtcNow.AddSeconds(10);lastFollow=-1;};lyricRows.MouseDown+=delegate{lyricManualUntil=DateTime.UtcNow.AddSeconds(10);lastFollow=-1;};
        lyricRows.DoubleClick+=delegate{int i=lyricRows.SelectedIndex;if(i>=0&&i<lineStarts.Count&&lineStarts[i]>=0&&audio.Seekable){SeekTo(lineStarts[i]);lyricManualUntil=DateTime.MinValue;}};
        lyricsPage.Resize+=delegate{int w=lyricsPage.Width,h=lyricsPage.Height,width=Math.Min(780,w-36),x=(w-width)/2;Place(bar,x,18,width,58);Place(lyricSource,x+8,83,Math.Max(50,width-280),28);Place(retry,x+width-154,82,102,30);Place(follow,x+width-266,82,106,30);Place(options,x+width-44,82,44,30);Place(lyricRows,x,124,width,Math.Max(80,h-142));};
        timer.Tick+=delegate{if(page!="Lyrics")return;if(lyricKey!=Key(Current)){LoadLyrics();return;}int line=LyricsSource.ActiveLine(lineStarts,audio.Time,LyricOffset);if(line>=0&&DateTime.UtcNow>=lyricManualUntil&&lastFollow!=line){lyricRows.TopIndex=Math.Max(0,line-2);lastFollow=line;lyricRows.Invalidate();}int word=(int)((audio.Time+LyricOffset)/80);if(word!=lyricWord&&line>=0){lyricWord=word;lyricRows.Invalidate(lyricRows.GetItemRectangle(line));}if(line!=highlighted){previousLine=highlighted;highlighted=line;highlightMotion.To(1,220,true);if(line>=0&&DateTime.UtcNow>=lyricManualUntil)lyricRows.TopIndex=Math.Max(0,line-2);lyricRows.Invalidate();}};
        FormClosing+=delegate{if(translationRequest!=null)translationRequest.Cancel();if(lyricRequest!=null)lyricRequest.Cancel();karaokeLayout.Dispose();};
    }
    async void LoadLyrics(bool refresh=false){
        var t=Current;string key=Key(t);if(lyricKey==key)return;if(translationRequest!=null)translationRequest.Cancel();lyricKey=key;int version=++lyricVersion;if(lyricRequest!=null)lyricRequest.Cancel();var request=new CancellationTokenSource();lyricRequest=request;
        highlighted=-1;lastFollow=-1;lyricManualUntil=DateTime.MinValue;richLyrics.Clear();lyricRows.Items.Clear();lineStarts.Clear();lyricRows.Items.Add("Finding the words…");lyricSource.Text="Checking lyric sources";
        try{int duration=t.DurationSeconds>0?t.DurationSeconds:(int)(Math.Max(0,audio.Length)/1000);var result=SavedLyricChoice(t)??await LyricsSource.Load(t,duration,request.Token,refresh);if(closing||version!=lyricVersion||request.IsCancellationRequested)return;lyricRows.BeginUpdate();try{lyricRows.Items.Clear();lineStarts.Clear();if(result!=null){foreach(var line in LyricsSource.Parse(result.Text)){richLyrics.Add(line);lyricRows.Items.Add(line.Text+(string.IsNullOrWhiteSpace(line.Pronunciation)?"":"\n"+line.Pronunciation)+(string.IsNullOrWhiteSpace(line.Translation)?"":"\n"+line.Translation));lineStarts.Add(line.Time);}lyricSource.Text=result.Provider+(lineStarts.Any(ms=>ms>=0)?" · Synced lyrics":" · Untimed — no sync data");}if(lyricRows.Items.Count==0){lyricRows.Items.Add("No lyrics found for this recording.");lyricSource.Text="Try again with Retry lyrics";}}finally{lyricRows.EndUpdate();lyricRows.Reflow();lyricFollow.Enabled=lineStarts.Any(ms=>ms>=0);}}
        catch(OperationCanceledException){}catch(Exception ex){System.Diagnostics.Debug.WriteLine("Lyrics: "+ex);if(!closing&&version==lyricVersion){lyricRows.Items.Clear();lyricRows.Items.Add("Lyrics couldn't load. Try again.");lyricSource.Text="Check your connection, then retry";}}
        finally{if(lyricRequest==request){lyricRequest=null;}request.Dispose();}
    }
}