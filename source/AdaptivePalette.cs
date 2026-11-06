using System;
using System.Drawing;

internal sealed class AdaptivePalette {
    internal Color Accent,Tone;
    static double Channel(byte v){double s=v/255.0;return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4);}
    internal static double Luminance(Color c){return .2126*Channel(c.R)+.7152*Channel(c.G)+.0722*Channel(c.B);}
    internal static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    internal static Color Readable(Color color,Color background,double minimum){for(int i=0;i<32&&Contrast(color,background)<minimum;i++)color=Color.FromArgb(Math.Min(255,color.R+8),Math.Min(255,color.G+8),Math.Min(255,color.B+8));return color;}
    internal static AdaptivePalette From(Color art){var tone=Color.FromArgb(12+art.R/9,14+art.G/9,17+art.B/9);var accent=Readable(Color.FromArgb(80+art.R*2/3,80+art.G*2/3,80+art.B*2/3),Color.FromArgb(55,65,75),4.5);return new AdaptivePalette{Accent=accent,Tone=tone};}
    internal static string Hex(Color color){return "#"+color.R.ToString("x2")+color.G.ToString("x2")+color.B.ToString("x2");}
}
