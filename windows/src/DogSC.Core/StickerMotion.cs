namespace DogSC.Core;

/// <summary>
/// Mac ElementMotionEvaluator + stickerAnimationTransform + StickerTransitionTrack
/// 的逐行移植：进出场进度曲线、画外位移公式、相邻贴图的交接与背景通道合成。
/// </summary>
public static class StickerMotion
{
    public static readonly string[] Animations =
    [
        "none", "fade", "pop", "slideLeft", "slideRight", "slideUp", "slideDown",
        "slideTopLeft", "slideTopRight", "slideBottomLeft", "slideBottomRight",
    ];

    public static readonly string[] Curves = ["smooth", "swift", "gentle"];

    /// <summary>ElementMotionEvaluator.progress：smooth=五次平滑，swift=1-(1-x)^4，gentle=1-(1-x)^3。</summary>
    public static double CurveProgress(double linear, string? curve)
    {
        var x = Math.Clamp(double.IsFinite(linear) ? linear : 0, 0, 1);
        return curve switch
        {
            "smooth" => x * x * x * (x * (x * 6 - 15) + 10),
            "gentle" => 1 - Math.Pow(1 - x, 3),
            _ => 1 - Math.Pow(1 - x, 4), // swift 与未知值
        };
    }

    /// <summary>同 StickerAnimationPreset.automaticExit：退出方向与进入互补。</summary>
    public static string AutomaticExit(string? animation) => animation switch
    {
        "slideLeft" => "slideRight",
        "slideRight" => "slideLeft",
        "slideUp" => "slideDown",
        "slideDown" => "slideUp",
        "slideTopLeft" => "slideBottomRight",
        "slideTopRight" => "slideBottomLeft",
        "slideBottomLeft" => "slideTopRight",
        "slideBottomRight" => "slideTopLeft",
        _ => animation ?? "none",
    };

    public static string ExitPreset(StickerImage sticker) =>
        string.IsNullOrEmpty(sticker.ExitAnimation) ? AutomaticExit(sticker.Animation) : sticker.ExitAnimation!;

    /// <summary>
    /// stickerAnimationTransform：progress 已缓动；位移从画布外进入，
    /// outsideLeading/Trailing 取决于贴纸半宽，pop 走 0.72→1 的缩放。
    /// </summary>
    public static (double Scale, double OffsetX, double OffsetY) Transform(
        string? preset, double progress, double posX, double posY, double width)
    {
        var p = Math.Clamp(progress, 0, 1);
        var travel = 1 - p;
        var halfExtent = Math.Max(width, 0.02) / 2;
        var outsideLeading = Math.Min(-0.85, -halfExtent - 0.06);
        var outsideTrailing = Math.Max(1.85, 1 + halfExtent + 0.06);
        double left = outsideLeading - posX, right = outsideTrailing - posX;
        double top = outsideLeading - posY, bottom = outsideTrailing - posY;
        double ox = 0, oy = 0, scale = 1;
        switch (preset)
        {
            case "pop":
                scale = 0.72 + p * 0.28;
                break;
            case "slideLeft": ox = left * travel; break;
            case "slideRight": ox = right * travel; break;
            case "slideUp": oy = top * travel; break;
            case "slideDown": oy = bottom * travel; break;
            case "slideTopLeft": ox = left * travel; oy = top * travel; break;
            case "slideTopRight": ox = right * travel; oy = top * travel; break;
            case "slideBottomLeft": ox = left * travel; oy = bottom * travel; break;
            case "slideBottomRight": ox = right * travel; oy = bottom * travel; break;
        }
        return (scale, ox, oy);
    }

    /// <summary>FrameStickerBackdropScene：压屏幕/压摄像头/屏幕虚化/摄像头虚化，max 合并、线性插值。</summary>
    public struct Backdrop
    {
        public double ScreenSuppression;
        public double CameraSuppression;
        public double ScreenBlur;
        public double CameraBlur;

        public static Backdrop Of(StickerImage clip, double visibility) => new()
        {
            ScreenSuppression = clip.HidesScreen ? visibility : 0,
            CameraSuppression = clip.HidesCamera ? visibility : 0,
            ScreenBlur = clip.HidesScreen ? 0 : Math.Max(clip.BackdropBlur, 0) * visibility,
            CameraBlur = clip.BackdropBlurIncludesCamera ? Math.Max(clip.BackdropBlur, 0) * visibility : 0,
        };

        public void Merge(Backdrop other)
        {
            ScreenSuppression = Math.Max(ScreenSuppression, other.ScreenSuppression);
            CameraSuppression = Math.Max(CameraSuppression, other.CameraSuppression);
            ScreenBlur = Math.Max(ScreenBlur, other.ScreenBlur);
            CameraBlur = Math.Max(CameraBlur, other.CameraBlur);
        }

        public Backdrop Interpolated(Backdrop other, double progress) => new()
        {
            ScreenSuppression = ScreenSuppression + (other.ScreenSuppression - ScreenSuppression) * progress,
            CameraSuppression = CameraSuppression + (other.CameraSuppression - CameraSuppression) * progress,
            ScreenBlur = ScreenBlur + (other.ScreenBlur - ScreenBlur) * progress,
            CameraBlur = CameraBlur + (other.CameraBlur - CameraBlur) * progress,
        };

        public bool IsZero =>
            ScreenSuppression == 0 && CameraSuppression == 0 && ScreenBlur == 0 && CameraBlur == 0;
    }

    /// <summary>单张贴图的采样结果。</summary>
    public readonly record struct Sample(StickerImage Clip, double Visibility, string Preset, double Progress);

    sealed class Entry
    {
        public StickerImage Clip;
        public double EnterDuration;
        public double ExitDuration;
        public int? EntryJoin;
        public int? ExitJoin;
        public double JoinEnterDuration => Clip.Animation == "none" ? 0 : EnterDuration;
        public double JoinExitDuration => ExitPreset(Clip) == "none" ? 0 : ExitDuration;

        public Entry(StickerImage clip)
        {
            Clip = clip;
            var enter = Math.Max(clip.EnterDuration, 0);
            var exit = Math.Max(clip.ExitDuration, 0);
            var total = enter + exit;
            var scale = total > clip.Timing.Duration && total > 0 ? clip.Timing.Duration / total : 1;
            EnterDuration = enter * scale;
            ExitDuration = exit * scale;
        }
    }

    sealed record Join(double Start, double End, Backdrop Outgoing, Backdrop Incoming, string? Curve)
    {
        public bool Contains(double time) => time >= Start && time < End;
        public double ProgressAt(double time) =>
            End > Start ? Math.Clamp((time - Start) / (End - Start), 0, 1) : (time >= End ? 1 : 0);
    }

    sealed record BackdropBridge(double Start, double End, Backdrop Treatment)
    {
        public bool Contains(double time) => time >= Start && time < End;
    }

    /// <summary>
    /// 同 StickerTransitionTrack：端点容差 1e-7 的邻接交接、重叠贴图的背景桥、
    /// 进入/退出时长按比例压回片段长度。
    /// </summary>
    public sealed class Track
    {
        const double EndpointTolerance = 0.000_000_1;

        readonly List<Entry> _entries;
        readonly List<Join> _joins = [];
        readonly List<BackdropBridge> _bridges = [];

        public Track(IEnumerable<StickerImage> clips)
        {
            _entries = clips
                .Where(c => c.Timing.Duration > 0 && double.IsFinite(c.Timing.Start) && double.IsFinite(c.Timing.Duration))
                .Select(c => new Entry(c))
                .ToList();
            var endpoints = _entries.SelectMany((e, i) => new[]
                {
                    (Time: e.Clip.Timing.Start, Index: i, Start: true),
                    (Time: e.Clip.Timing.Start + e.Clip.Timing.Duration, Index: i, Start: false),
                })
                .OrderBy(e => e.Time).ToList();
            var cursor = 0;
            while (cursor < endpoints.Count)
            {
                var boundary = endpoints[cursor].Time;
                var outgoing = new List<int>();
                var incoming = new List<int>();
                do
                {
                    var e = endpoints[cursor];
                    (e.Start ? incoming : outgoing).Add(e.Index);
                    cursor++;
                } while (cursor < endpoints.Count && endpoints[cursor].Time - boundary <= EndpointTolerance);
                var shared = outgoing.Intersect(incoming).ToHashSet();
                outgoing.RemoveAll(shared.Contains);
                incoming.RemoveAll(shared.Contains);
                if (outgoing.Count == 0 || incoming.Count == 0) continue;
                var before = outgoing.Min(i => _entries[i].JoinExitDuration);
                var after = incoming.Min(i => _entries[i].JoinEnterDuration);
                var outgoingBackdrop = new Backdrop();
                var incomingBackdrop = new Backdrop();
                foreach (var i in outgoing) outgoingBackdrop.Merge(Backdrop.Of(_entries[i].Clip, 1));
                foreach (var i in incoming) incomingBackdrop.Merge(Backdrop.Of(_entries[i].Clip, 1));
                var joinIndex = _joins.Count;
                _joins.Add(new Join(
                    outgoing.Min(i => _entries[i].Clip.Timing.Start + _entries[i].Clip.Timing.Duration) - before,
                    incoming.Max(i => _entries[i].Clip.Timing.Start) + after,
                    outgoingBackdrop, incomingBackdrop,
                    _entries[incoming.Max()].Clip.AnimationCurve));
                foreach (var i in outgoing) _entries[i].ExitJoin = joinIndex;
                foreach (var i in incoming) _entries[i].EntryJoin = joinIndex;
            }
            // 重叠交接：保持两张贴图共享的背景通道在各自淡入淡出期间不掉。
            var ordered = _entries.Select((e, i) => i)
                .OrderBy(i => (_entries[i].Clip.Timing.Start, _entries[i].Clip.Timing.Start + _entries[i].Clip.Timing.Duration))
                .ToList();
            foreach (var (offset, outgoingIndex) in ordered.Select((v, i) => (i, v)))
            {
                var outgoing = _entries[outgoingIndex];
                foreach (var incomingIndex in ordered.Skip(offset + 1))
                {
                    var incoming = _entries[incomingIndex];
                    if (incoming.Clip.Timing.Start >= outgoing.Clip.Timing.Start + outgoing.Clip.Timing.Duration - EndpointTolerance) break;
                    if (incoming.Clip.Timing.Start + incoming.Clip.Timing.Duration <= outgoing.Clip.Timing.Start + outgoing.Clip.Timing.Duration) continue;
                    var start = outgoing.Clip.Timing.Start + outgoing.Clip.Timing.Duration - outgoing.JoinExitDuration;
                    var end = incoming.Clip.Timing.Start + incoming.JoinEnterDuration;
                    if (end <= start) continue;
                    var from = Backdrop.Of(outgoing.Clip, 1);
                    var to = Backdrop.Of(incoming.Clip, 1);
                    var treatment = new Backdrop
                    {
                        ScreenSuppression = Math.Min(from.ScreenSuppression, to.ScreenSuppression),
                        CameraSuppression = Math.Min(from.CameraSuppression, to.CameraSuppression),
                        ScreenBlur = Math.Min(from.ScreenBlur, to.ScreenBlur),
                        CameraBlur = Math.Min(from.CameraBlur, to.CameraBlur),
                    };
                    if (!treatment.IsZero) _bridges.Add(new BackdropBridge(start, end, treatment));
                }
            }
        }

        /// <summary>导出用的窗口数据：缩放后的进/出时长与交接窗口。</summary>
        public readonly record struct WindowsInfo(
            double EnterDuration, double ExitDuration,
            (double Start, double End)? JoinEnter, (double Start, double End)? JoinExit);

        public WindowsInfo Windows(StickerImage clip)
        {
            var index = _entries.FindIndex(e => ReferenceEquals(e.Clip, clip));
            if (index < 0)
            {
                var fallback = new Entry(clip);
                return new WindowsInfo(fallback.EnterDuration, fallback.ExitDuration, null, null);
            }
            var e = _entries[index];
            (double, double)? J(int? i) => i is { } j ? (_joins[j].Start, _joins[j].End) : null;
            return new WindowsInfo(e.EnterDuration, e.ExitDuration, J(e.EntryJoin), J(e.ExitJoin));
        }

        /// <summary>贴纸 t 的可见窗口（含交接外扩），用于 enable 表达式。</summary>
        public (double Start, double End) VisibleWindow(StickerImage clip)
        {
            var index = _entries.FindIndex(e => ReferenceEquals(e.Clip, clip));
            var start = clip.Timing.Start;
            var end = clip.Timing.Start + clip.Timing.Duration;
            if (index >= 0)
            {
                var e = _entries[index];
                if (e.EntryJoin is { } je) start = Math.Min(start, _joins[je].Start);
                if (e.ExitJoin is { } jx) end = Math.Max(end, _joins[jx].End);
            }
            return (start, end);
        }

        /// <summary>同 sample(at:)：返回每张入场贴图的可见度/预设/进度与合成背景通道。</summary>
        public (List<Sample> Stickers, Backdrop Backdrop) SampleAt(double time, bool suppressesInitialEntry = false)
        {
            var backdrop = new Backdrop();
            foreach (var bridge in _bridges)
                if (bridge.Contains(time)) backdrop.Merge(bridge.Treatment);
            foreach (var join in _joins)
                if (join.Contains(time))
                    backdrop.Merge(join.Outgoing.Interpolated(join.Incoming,
                        CurveProgress(join.ProgressAt(time), join.Curve)));
            var stickers = new List<Sample>();
            foreach (var entry in _entries)
            {
                var clip = entry.Clip;
                var start = clip.Timing.Start;
                var end = start + clip.Timing.Duration;
                var entryJoin = entry.EntryJoin is { } ej ? _joins[ej] : null;
                var exitJoin = entry.ExitJoin is { } xj ? _joins[xj] : null;
                var joinedEntryIsActive = entryJoin?.Contains(time) ?? false;
                var joinedExitIsActive = exitJoin?.Contains(time) ?? false;
                if (!(time >= start && time < end) && !joinedEntryIsActive && !joinedExitIsActive) continue;
                var localTime = Math.Max(time - start, 0);
                var remaining = Math.Max(end - time, 0);
                var suppressesEntry = suppressesInitialEntry && start <= 0.0001;
                var linearEnter = entryJoin?.ProgressAt(time) ?? (suppressesEntry ? 1
                    : entry.EnterDuration > 0 ? Math.Min(localTime / entry.EnterDuration, 1) : 1);
                var linearExit = exitJoin is not null ? 1 - exitJoin.ProgressAt(time)
                    : entry.ExitDuration > 0 ? Math.Min(remaining / entry.ExitDuration, 1) : 1;
                var enterProgress = CurveProgress(linearEnter, clip.AnimationCurve);
                var exitProgress = CurveProgress(linearExit, clip.AnimationCurve);
                var exitPreset = ExitPreset(clip);
                var visibility = Math.Min(clip.Animation == "none" ? 1 : enterProgress,
                    exitPreset == "none" ? 1 : exitProgress);
                var isExiting = exitJoin is not null ? time >= exitJoin.Start
                    : entry.ExitDuration > 0 && remaining < entry.ExitDuration;
                if (!joinedEntryIsActive && !joinedExitIsActive)
                    backdrop.Merge(Backdrop.Of(clip, visibility));
                stickers.Add(new Sample(clip, visibility,
                    isExiting ? exitPreset : clip.Animation,
                    isExiting ? exitProgress : enterProgress));
            }
            return (stickers, backdrop);
        }
    }
}
