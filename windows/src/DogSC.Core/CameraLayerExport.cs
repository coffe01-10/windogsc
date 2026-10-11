namespace DogSC.Core;

public static class CameraLayerExport
{
    /// <summary>Resample the complete camera texture into a fixed canvas. Never stretch an already-cropped bubble.</summary>
    public static string Filter(CameraSceneTrack track, double start, double end, double entryStart, double entryEnd, int fps, double blurPadding = 0, bool applyOpacity = true)
    {
        var samples = Samples(track, start, end, entryStart, entryEnd, fps);
        string Expression(Func<CameraScene, double> field) => MotionExport.Piecewise(samples.Select(s => (s.Time, field(s.Scene))).ToArray(), "0", "T");
        var x = Expression(s => s.Frame.X); var y = Expression(s => s.Frame.Y);
        var w = Expression(s => s.Frame.W); var h = Expression(s => s.Frame.H);
        var radius = Expression(s => s.Radius); var opacity = applyOpacity ? Expression(s => s.Frame.Opacity) : "1";
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
        string Color(string channel) => "'" + Cached(geometry + content) + "if(" + inside + "," + channel + "(" + sourceX + "," + sourceY + "),0)'";
        var alpha = Cached(geometry + "st(4," + radius + ");st(5," + opacity + ");")
            + "st(6,abs(X+.5-ld(0)-ld(2)/2)-(ld(2)/2-ld(4)));"
            + "st(7,abs(Y+.5-ld(1)-ld(3)/2)-(ld(3)/2-ld(4)));"
            + "255*clip(.5-(hypot(max(ld(6),0),max(ld(7),0))+min(max(ld(6),ld(7)),0)-ld(4)),0,1)*ld(5)";
        return "format=gbrap,geq=r=" + Color("r") + ":g=" + Color("g") + ":b=" + Color("b")
            + ":a='" + alpha + "':interpolation=bilinear,setsar=1";
    }

    /// <summary>White outer ring or black, padded Gaussian shadow, before the shared layer opacity.</summary>
    public static string DecorationFilter(CameraSceneTrack track, double start, double end, double entryStart, double entryEnd, int fps, bool shadow)
    {
        var samples = Samples(track, start, end, entryStart, entryEnd, fps);
        string Expression(Func<CameraScene, double> field) => MotionExport.Piecewise(samples.Select(s => (s.Time, field(s.Scene))).ToArray(), "0", "T");
        var style = samples[0].Scene;
        // Preserve off-canvas mask pixels that contribute to the visible shadow.
        var padding = shadow ? (int)Math.Ceiling(style.ShadowRadius * 3 + Math.Abs(style.ShadowOffsetY) + samples.Max(s => s.Scene.BorderWidth)) : 0;
        var geometry = "st(0," + Expression(s => s.Frame.X + padding) + ");st(1,"
            + Expression(s => s.Frame.Y + padding + (shadow ? s.ShadowOffsetY : 0)) + ");st(2,"
            + Expression(s => s.Frame.W) + ");st(3," + Expression(s => s.Frame.H) + ");st(4,"
            + Expression(s => s.Radius) + ");st(5," + Expression(s => s.BorderWidth) + ");";
        var distance = "st(6,abs(X+.5-ld(0)-ld(2)/2)-(ld(2)/2-ld(4)));"
            + "st(7,abs(Y+.5-ld(1)-ld(3)/2)-(ld(3)/2-ld(4)));"
            + "st(6,hypot(max(ld(6),0),max(ld(7),0))+min(max(ld(6),ld(7)),0)-ld(4));";
        var outer = "clip(.5-ld(6)+ld(5),0,1)";
        var alpha = Cached(geometry) + distance + "255*" + (shadow ? outer + "*" + F(style.ShadowStrength)
            : "max(0," + outer + "-clip(.5-ld(6),0,1))");
        var color = shadow ? "0" : "255";
        var filter = "format=gbrap,";
        if (shadow) filter += "pad=iw+" + padding * 2 + ":ih+" + padding * 2 + ":" + padding + ":" + padding + ":color=black@0,";
        filter += "geq=r='" + color + "':g='" + color + "':b='" + color + "':a='" + alpha + "'";
        if (shadow) filter += ",gblur=sigma=" + F(style.ShadowRadius) + ":planes=8,crop=iw-" + padding * 2 + ":ih-" + padding * 2 + ":" + padding + ":" + padding;
        return filter;
    }

    public static string OpacityFilter(CameraSceneTrack track, double start, double end, double entryStart, double entryEnd, int fps)
    {
        var samples = Samples(track, start, end, entryStart, entryEnd, fps);
        var opacity = MotionExport.Piecewise(samples.Select(s => (s.Time, s.Scene.Frame.Opacity)).ToArray(), "0", "T");
        return "format=gbrap,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='" + Cached("st(0," + opacity + ");") + "alpha(X,Y)*ld(0)'";
    }

    static List<(double Time, CameraScene Scene)> Samples(CameraSceneTrack track, double start, double end, double entryStart, double entryEnd, int fps)
        => Enumerable.Range((int)Math.Floor(start * fps), (int)Math.Ceiling(end * fps) - (int)Math.Floor(start * fps) + 1)
            .Select(frame => (Time: frame / (double)fps, Scene: track.At(frame / (double)fps, entryStart, entryEnd))).ToList();

    static string Cached(string values) => "if(eq(ld(8),0)+not(eq(ld(9),N))," + values + "st(8,1);st(9,N),0);";
    static string F(double value) => value.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
}
