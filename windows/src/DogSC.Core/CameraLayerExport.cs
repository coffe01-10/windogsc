namespace DogSC.Core;

public static class CameraLayerExport
{
    /// <summary>Resample the complete camera texture into a fixed canvas. Never stretch an already-cropped bubble.</summary>
    public static string Filter(CameraSceneTrack track, double start, double end, double entryStart, double entryEnd, int fps, double blurPadding = 0)
    {
        var first = (int)Math.Floor(start * fps);
        var last = (int)Math.Ceiling(end * fps);
        var samples = new List<(double Time, CameraScene Scene)>();
        for (var frame = first; frame <= last; frame++)
        {
            var time = frame / (double)fps;
            samples.Add((time, track.At(time, entryStart, entryEnd)));
        }
        string Expression(Func<CameraScene, double> field) => MotionExport.Piecewise(samples.Select(s => (s.Time, field(s.Scene))).ToArray(), "0", "T");
        var x = Expression(s => s.Frame.X); var y = Expression(s => s.Frame.Y);
        var w = Expression(s => s.Frame.W); var h = Expression(s => s.Frame.H);
        var radius = Expression(s => s.Radius); var opacity = Expression(s => s.Frame.Opacity);
        var u = Expression(s => s.Content.X); var v = Expression(s => s.Content.Y);
        var uw = Expression(s => s.Content.Width); var vh = Expression(s => s.Content.Height);
        var geometry = "st(0," + x + ");st(1," + y + ");st(2," + w + ");st(3," + h + ");";
        var mirrored = samples[0].Scene.Mirrored;
        var content = "st(6,(" + (mirrored ? "-" : "") + "(" + uw + "))*W/ld(2));st(7,(" + vh + ")*H/ld(3));"
            + "st(4,((" + u + ")" + (mirrored ? "+(" + uw + ")" : "") + ")*W+(.5-ld(0))*ld(6)-.5);"
            + "st(5,(" + v + ")*H+(.5-ld(1))*ld(7)-.5);";
        var sourceX = "X*ld(6)+ld(4)";
        var sourceY = "Y*ld(7)+ld(5)";
        var padding = blurPadding.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
        var inside = "between(X+.5,ld(0)-" + padding + ",ld(0)+ld(2)+" + padding
            + ")*between(Y+.5,ld(1)-" + padding + ",ld(1)+ld(3)+" + padding + ")";
        // geq owns one persistent expression context per plane and worker slice.
        // Resolve the track once per frame there, rather than at every image pixel.
        string Cached(string values) => "if(eq(ld(8),0)+not(eq(ld(9),N))," + values + "st(8,1);st(9,N),0);";
        string Color(string channel) => "'" + Cached(geometry + content) + "if(" + inside + "," + channel + "(" + sourceX + "," + sourceY + "),0)'";
        var alpha = Cached(geometry + "st(4," + radius + ");st(5," + opacity + ");")
            + "st(6,abs(X+.5-ld(0)-ld(2)/2)-(ld(2)/2-ld(4)));"
            + "st(7,abs(Y+.5-ld(1)-ld(3)/2)-(ld(3)/2-ld(4)));"
            + "255*clip(.5-(hypot(max(ld(6),0),max(ld(7),0))+min(max(ld(6),ld(7)),0)-ld(4)),0,1)*ld(5)";
        return "format=gbrap,geq=r=" + Color("r") + ":g=" + Color("g") + ":b=" + Color("b")
            + ":a='" + alpha + "':interpolation=bilinear,setsar=1";
    }
}
