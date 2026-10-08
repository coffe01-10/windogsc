using System.Globalization;
using System.Text;

namespace DogSC.Core;

public sealed class ExportOptions
{
    public bool AudioOnly { get; init; }
    public string Resolution { get; init; } = "source";
    public int Fps { get; init; } = 30;
}

public sealed class ResolvedMedia
{
    public required IReadOnlyDictionary<string, string> Videos { get; init; }
    public required IReadOnlyDictionary<string, string> Microphones { get; init; }
    public required IReadOnlyDictionary<string, string> Systems { get; init; }
    public required IReadOnlyDictionary<string, string> Cameras { get; init; }
}

public sealed class ExportPlan
{
    public required List<string> Inputs { get; init; }
    public required string FilterScript { get; init; }
    public string? VideoLabel { get; init; }
    public string? AudioLabel { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Duration { get; init; }
    public int Fps { get; init; }
    public FramePlacement Placement { get; init; }
    public int FrameMaskIndex { get; init; } = -1;
    public int CameraMaskIndex { get; init; } = -1;
    public BubblePlacement Bubble { get; init; }
}

public static class ExportGraph
{
    public static ExportPlan Build(ProjectDocument project, ExportOptions options, ResolvedMedia media)
    {
        ProjectFile.Normalize(project);
        var placed = TimelineMath.Place(project);
        if (placed.Count == 0)
        {
            throw new InvalidDataException("时间线上没有片段。");
        }

        var slices = BuildPieces(project, placed, media);
        var duration = placed[^1].End;
        var fps = options.Fps is 60 ? 60 : 30;
        var (canvasWidth, canvasHeight) = CanvasLayout.OutputSize(project.Width, project.Height, project.Canvas.Aspect, options.Resolution);
        var placement = CanvasLayout.Place(canvasWidth, canvasHeight, project.Width, project.Height, project.Canvas);
        var bubble = CanvasLayout.Bubble(canvasWidth, canvasHeight, project.Camera);
        var inputs = new List<string>();
        var videoIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var micIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var systemIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cameraIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int IndexFor(Dictionary<string, int> map, string path)
        {
            if (map.TryGetValue(path, out var index))
            {
                return index;
            }

            index = inputs.Count;
            inputs.Add(path);
            map[path] = index;
            return index;
        }

        foreach (var slice in slices)
        {
            IndexFor(videoIndex, slice.VideoPath);
            if (slice.MicrophonePath is not null)
            {
                IndexFor(micIndex, slice.MicrophonePath);
            }

            if (slice.SystemPath is not null)
            {
                IndexFor(systemIndex, slice.SystemPath);
            }

            if (slice.CameraPath is not null)
            {
                IndexFor(cameraIndex, slice.CameraPath);
            }
        }

        var includeCamera = !options.AudioOnly && slices.All(slice => slice.CameraPath is not null);
        var script = new StringBuilder();
        var videoLabels = new List<string>();
        var cameraLabels = new List<string>();
        if (!options.AudioOnly)
        {
            for (var index = 0; index < slices.Count; index++)
            {
                var slice = slices[index];
                var label = "v" + index;
                script.Append('[').Append(videoIndex[slice.VideoPath]).Append(":v]")
                    .Append(VideoChain(slice, project.Width, project.Height, fps))
                    .Append('[').Append(label).AppendLine("]");
                videoLabels.Add(label);
                if (includeCamera)
                {
                    var cameraLabel = "c" + index;
                    script.Append('[').Append(cameraIndex[slice.CameraPath!]).Append(":v]")
                        .Append(CameraChain(slice, bubble.Size, fps))
                        .Append('[').Append(cameraLabel).AppendLine("]");
                    cameraLabels.Add(cameraLabel);
                }
            }
        }

        string? videoMap = null;
        var frameMaskIndex = -1;
        var cameraMaskIndex = -1;
        if (!options.AudioOnly)
        {
            var concatenated = Join(script, videoLabels, "vcat", video: true);
            var needsMask = placement.Radius > 0 || placement.ShadowBlur > 0;
            if (needsMask)
            {
                frameMaskIndex = inputs.Count;
                inputs.Add("");
            }

            script.Append(concatenated).Append("scale=")
                .Append(placement.Width).Append(':').Append(placement.Height)
                .Append(":flags=lanczos,format=rgba[scaled]");
            script.AppendLine();
            if (needsMask)
            {
                var split = placement.ShadowBlur > 0 ? ",split[framea][shadowsrc]" : "[framea]";
                script.Append('[').Append(frameMaskIndex).Append(":v]format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                    .Append(F(Math.Max(duration, 0.1)))
                    .Append(",setpts=PTS-STARTPTS,alphaextract").Append(split)
                    .AppendLine();
                script.Append("[scaled][framea]alphamerge[rounded]").AppendLine();
            }
            else
            {
                script.Append("[scaled]format=rgba[rounded]").AppendLine();
            }

            var background = ProjectFile.NormalizeColor(project.Canvas.Background)[1..];
            script.Append("color=c=0x").Append(background)
                .Append(":s=").Append(canvasWidth).Append('x').Append(canvasHeight)
                .Append(":r=").Append(fps)
                .Append(":d=").Append(F(Math.Max(duration, 0.1)))
                .AppendLine(",format=rgba[bg]");
            var under = "bg";
            if (placement.ShadowBlur > 0)
            {
                script.Append("[shadowsrc]boxblur=").Append(Math.Max(1, placement.ShadowBlur)).Append(":2[shadowa]").AppendLine();
                script.Append("color=c=black:s=").Append(placement.Width).Append('x').Append(placement.Height)
                    .Append(":r=").Append(fps)
                    .Append(":d=").Append(F(Math.Max(duration, 0.1)))
                    .AppendLine(",format=rgba[shadowc]");
                script.Append("[shadowc][shadowa]alphamerge,colorchannelmixer=aa=")
                    .Append(F(placement.ShadowAlpha))
                    .AppendLine("[shadow]");
                script.Append("[bg][shadow]overlay=")
                    .Append(placement.X + placement.ShadowOffset / 3).Append(':')
                    .Append(placement.Y + placement.ShadowOffset)
                    .AppendLine(":format=auto:shortest=1[bgsh]");
                under = "bgsh";
            }

            script.Append('[').Append(under).Append("][rounded]overlay=")
                .Append(placement.X).Append(':').Append(placement.Y)
                .AppendLine(":format=auto:shortest=1[composed]");
            videoMap = "composed";
            if (includeCamera)
            {
                cameraMaskIndex = inputs.Count;
                inputs.Add("");
                var cameraCat = Join(script, cameraLabels, "ccat", video: true);
                script.Append(cameraCat).AppendLine("format=rgba[camf]");
                script.Append('[').Append(cameraMaskIndex).Append(":v]format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                    .Append(F(Math.Max(duration, 0.1)))
                    .AppendLine(",setpts=PTS-STARTPTS,alphaextract[cama]");
                script.AppendLine("[camf][cama]alphamerge[camr]");
                script.Append("[composed][camr]overlay=").Append(bubble.X).Append(':').Append(bubble.Y)
                    .AppendLine(":format=auto:shortest=1[vfinal]");
                videoMap = "vfinal";
            }

            script.Append('[').Append(videoMap).Append("]format=yuv420p[vout]").AppendLine();
            videoMap = "vout";
        }

        var audioLabels = new List<string>();
        var wantMic = project.MicrophoneGain > 0.001;
        var wantSystem = project.SystemGain > 0.001;
        for (var index = 0; index < slices.Count; index++)
        {
            var slice = slices[index];
            string? mic = null;
            string? system = null;
            if (wantMic && slice.MicrophonePath is not null)
            {
                mic = "m" + index;
                script.Append('[').Append(micIndex[slice.MicrophonePath]).Append(":a]")
                    .Append(AudioChain(slice, project.MicrophoneGain))
                    .Append('[').Append(mic).AppendLine("]");
            }

            if (wantSystem && slice.SystemPath is not null)
            {
                system = "s" + index;
                script.Append('[').Append(systemIndex[slice.SystemPath]).Append(":a]")
                    .Append(AudioChain(slice, project.SystemGain))
                    .Append('[').Append(system).AppendLine("]");
            }

            if (mic is null && system is null)
            {
                continue;
            }

            if (mic is not null && system is not null)
            {
                script.Append('[').Append(mic).Append("][").Append(system)
                    .Append("]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[a")
                    .Append(index).AppendLine("]");
                audioLabels.Add("a" + index);
            }
            else
            {
                audioLabels.Add(mic ?? system!);
            }
        }

        string? audioMap = null;
        if (audioLabels.Count == slices.Count && audioLabels.Count > 0)
        {
            audioMap = Join(script, audioLabels, "aout", video: false).Trim().Trim('[', ']');
        }

        return new ExportPlan
        {
            Inputs = inputs,
            FilterScript = script.ToString().Trim(),
            VideoLabel = videoMap,
            AudioLabel = audioMap,
            Width = canvasWidth,
            Height = canvasHeight,
            Duration = duration,
            Fps = fps,
            Placement = placement,
            FrameMaskIndex = frameMaskIndex,
            CameraMaskIndex = cameraMaskIndex,
            Bubble = bubble,
        };
    }

    private static string Join(StringBuilder script, List<string> labels, string output, bool video)
    {
        if (labels.Count == 1)
        {
            return "[" + labels[0] + "]";
        }

        foreach (var label in labels)
        {
            script.Append('[').Append(label).Append(']');
        }

        script.Append("concat=n=").Append(labels.Count)
            .Append(video ? ":v=1:a=0" : ":v=0:a=1")
            .Append('[').Append(output).AppendLine("]");
        return "[" + output + "]";
    }

    private static string VideoChain(Piece slice, int width, int height, int fps, bool zoom = true)
    {
        var chain = new StringBuilder();
        chain.Append("trim=start=").Append(F(slice.SourceStart))
            .Append(":end=").Append(F(slice.SourceEnd))
            .Append(",setpts=(PTS-STARTPTS)/").Append(F(slice.Speed));
        if (zoom && slice.Zoom is { Scale: > 1.01 })
        {
            chain.Append(",crop=w='trunc(iw/").Append(F(slice.Zoom.Scale))
                .Append("/2)*2':h='trunc(ih/").Append(F(slice.Zoom.Scale))
                .Append("/2)*2':x='trunc(((iw-ow)*").Append(F(slice.Zoom.FocusX))
                .Append(")/2)*2':y='trunc(((ih-oh)*").Append(F(slice.Zoom.FocusY)).Append(")/2)*2'");
        }

        chain.Append(",scale=").Append(width).Append(':').Append(height)
            .Append(":flags=lanczos,setsar=1,fps=").Append(fps).Append(",format=yuv420p");
        return chain.ToString();
    }

    private static string CameraChain(Piece slice, int size, int fps)
    {
        var chain = new StringBuilder();
        chain.Append("trim=start=").Append(F(slice.SourceStart))
            .Append(":end=").Append(F(slice.SourceEnd))
            .Append(",setpts=(PTS-STARTPTS)/").Append(F(slice.Speed))
            .Append(",scale=").Append(size).Append(':').Append(size)
            .Append(":force_original_aspect_ratio=increase,crop=")
            .Append(size).Append(':').Append(size)
            .Append(",setsar=1,fps=").Append(fps).Append(",format=yuv420p");
        return chain.ToString();
    }

    private static string AudioChain(Piece slice, double gain)
    {
        var chain = new StringBuilder();
        chain.Append("aformat=channel_layouts=stereo,aresample=48000,atrim=start=")
            .Append(F(slice.SourceStart)).Append(":end=").Append(F(slice.SourceEnd))
            .Append(",asetpts=PTS-STARTPTS");
        if (Math.Abs(slice.Speed - 1) > 0.001)
        {
            chain.Append(",atempo=").Append(F(slice.Speed));
        }

        if (Math.Abs(gain - 1) > 0.001)
        {
            chain.Append(",volume=").Append(F(gain));
        }

        chain.Append(",apad=pad_dur=2,atrim=0:").Append(F(slice.TimelineDuration));
        return chain.ToString();
    }

    private static List<Piece> BuildPieces(ProjectDocument project, IReadOnlyList<PlacedClip> placed, ResolvedMedia media)
    {
        var zooms = project.Zooms.OrderBy(item => item.Start).ToList();
        var slices = new List<Piece>();
        foreach (var clip in placed)
        {
            if (!media.Videos.TryGetValue(clip.Asset.Id, out var video))
            {
                throw new InvalidDataException("找不到片段对应的画面文件。");
            }

            media.Microphones.TryGetValue(clip.Asset.Id, out var microphone);
            media.Systems.TryGetValue(clip.Asset.Id, out var system);
            media.Cameras.TryGetValue(clip.Asset.Id, out var camera);
            var cuts = new List<double> { clip.Start, clip.End };
            foreach (var zoom in zooms)
            {
                var start = Math.Max(zoom.Start, clip.Start);
                var end = Math.Min(zoom.Start + zoom.Duration, clip.End);
                if (end - start > 0.04)
                {
                    cuts.Add(start);
                    cuts.Add(end);
                }
            }

            cuts.Sort();
            for (var index = 0; index < cuts.Count - 1; index++)
            {
                var start = cuts[index];
                var end = cuts[index + 1];
                if (end - start < 0.04)
                {
                    continue;
                }

                if (index > 0 && Math.Abs(cuts[index] - cuts[index - 1]) < 0.0001)
                {
                    continue;
                }

                var midpoint = (start + end) / 2;
                var zoom = zooms.FirstOrDefault(item => midpoint >= item.Start && midpoint < item.Start + item.Duration);
                var sourceStart = clip.Clip.SourceStart + (start - clip.Start) * clip.Clip.Speed;
                var sourceEnd = clip.Clip.SourceStart + (end - clip.Start) * clip.Clip.Speed;
                slices.Add(new Piece(video, microphone, system, camera, sourceStart, sourceEnd, clip.Clip.Speed, start, end - start, zoom));
            }
        }

        if (slices.Count == 0)
        {
            throw new InvalidDataException("片段短得无法导出。");
        }

        return slices;
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record Piece(
        string VideoPath,
        string? MicrophonePath,
        string? SystemPath,
        string? CameraPath,
        double SourceStart,
        double SourceEnd,
        double Speed,
        double TimelineStart,
        double TimelineDuration,
        ZoomSpan? Zoom);
}
