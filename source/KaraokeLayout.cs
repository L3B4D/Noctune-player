using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

// Cache the actual shaped word regions so wrapping, repeated words and CJK stay aligned.
internal sealed class KaraokeLayout:IDisposable {
    readonly List<Region> regions=new List<Region>();readonly List<LyricsSource.Word> words=new List<LyricsSource.Word>();
    List<LyricsSource.Word> source;string text;float width,fontSize;string family;
    internal void Draw(Graphics g,string value,Font font,Rectangle bounds,Color color,Color accent,List<LyricsSource.Word> timed,long time){
        using(var format=new StringFormat(StringFormat.GenericDefault)){format.Trimming=StringTrimming.None;format.FormatFlags=StringFormatFlags.LineLimit;
            var measured=g.MeasureString(value,font,new SizeF(bounds.Width,10000),format);float y=bounds.Y+Math.Max(0,(bounds.Height-measured.Height)/2);
            var box=new RectangleF(0,0,bounds.Width,Math.Max(measured.Height+4,bounds.Height));
            if(time>=0&&(text!=value||width!=bounds.Width||fontSize!=font.Size||family!=font.FontFamily.Name||source!=timed)){
                Clear();source=timed;text=value;width=bounds.Width;fontSize=font.Size;family=font.FontFamily.Name;int next=0;
                if(timed!=null)foreach(var word in timed){if(string.IsNullOrEmpty(word.Text))continue;int at=value.IndexOf(word.Text,next,StringComparison.Ordinal);if(at<0)continue;next=at+word.Text.Length;format.SetMeasurableCharacterRanges(new[]{new CharacterRange(at,word.Text.Length)});regions.Add(g.MeasureCharacterRanges(value,font,box,format)[0]);words.Add(word);}
            }
            var saved=g.Save();g.SetClip(bounds);g.TranslateTransform(bounds.X,y);
            using(var brush=new SolidBrush(color))g.DrawString(value,font,brush,box,format);
            for(int i=0;i<words.Count;i++)if(time>=words[i].Start&&time<words[i].End){var state=g.Save();g.SetClip(regions[i],System.Drawing.Drawing2D.CombineMode.Intersect);using(var brush=new SolidBrush(accent))g.DrawString(value,font,brush,box,format);g.Restore(state);}
            g.Restore(saved);
        }
    }
    void Clear(){foreach(var region in regions)region.Dispose();regions.Clear();words.Clear();}
    public void Dispose(){Clear();text=null;}
}
