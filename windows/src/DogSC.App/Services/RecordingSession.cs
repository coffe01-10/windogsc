using System.Text.Json;
using DogSC.Core;

namespace DogSC;

public enum CaptureKind
{
    Display,
    Window,
    Region,
    Device,
}

public sealed record CaptureTarget(CaptureKind Kind, string Label, int X, int Y, int Width, int Height, string? DeviceName)
{
    /// <summary>窗口采集的目标句柄；非窗口来源为 Zero。</summary>
    public IntPtr WindowHandle { get; init; }
}

public sealed class RecordingOptions
{
    public int FrameRate { get; init; } = 30;
    public bool RecordCursor { get; init; } = true;
    public string Quality { get; init; } = "standard";
    public bool Microphone { get; init; }
    public string? MicrophoneId { get; init; }
    public bool SystemAudio { get; init; }
    public string? PlaybackDeviceId { get; init; }
    public bool Camera { get; init; }
    public string? CameraName { get; init; }
}

public sealed class RecordingSession : IAsyncDisposable
{
    readonly string _ffmpeg;
    readonly string _ffprobe;
    CaptureTarget _target;
    readonly RecordingOptions _options;
    readonly string _directory;
    readonly List<Take> _takes = [];
    readonly DateTime _startedUtc = DateTime.UtcNow;
    Take? _current;
    FfmpegProcess? _video;
    NativeWindowCapture? _windowCapture;
    FfmpegProcess? _camera;
    WavRecorder? _microphone;
    WavRecorder? _system;
    readonly System.Diagnostics.Stopwatch _clock = new();
    Timer? _follower;
    Timer? _cursorTimer;
    MouseClickHook? _clickHook;
    bool _followLost;
    int _index;

    public RecordingSession(string ffmpeg, string ffprobe, CaptureTarget target, RecordingOptions options, string directory)
    {
        _ffmpeg = ffmpeg;
        _ffprobe = ffprobe;
        _target = target;
        _options = options;
        _directory = directory;
    }

    public bool IsRunning => _clock.IsRunning;
    public event Action<int, int>? PointerClicked;
    public int MarkerCount => _takes.Sum(t => t.Marks.Count) + (_current?.Marks.Count ?? 0);
    public bool HasMicrophone => _microphone is not null;
    public bool MicrophoneMuted { get; private set; }
    public void SetMicrophoneMuted(bool muted)
    {
        MicrophoneMuted = muted;
        if (_microphone is not null) _microphone.Muted = muted;
    }

    public double Elapsed =>
        _takes.Sum(take => take.Duration) + (_clock.IsRunning ? _clock.Elapsed.TotalSeconds : 0);

    public async Task StartAsync()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "media"));
        await BeginTakeAsync();
    }

    public void Mark(string? note)
    {
        if (_current is null || !_clock.IsRunning)
        {
            return;
        }

        _current.Marks.Add((_clock.Elapsed.TotalSeconds, note));
        SaveJournal();
    }

    public async Task PauseAsync()
    {
        if (_current is null)
        {
            return;
        }

        await EndTakeAsync();
    }

    public async Task ResumeAsync() => await BeginTakeAsync();

    public async Task SwitchTargetAsync(CaptureTarget target)
    {
        var running = _current is not null;
        if (running) await EndTakeAsync();
        var previous = _target; _target = target;
        try { if (running) await BeginTakeAsync(); }
        catch { _target = previous; throw; } // Retained takes remain usable; resume the previous target.
    }

    public async Task<ProjectDocument> FinishAsync()
    {
        if (_current is not null)
        {
            await EndTakeAsync();
        }

        var usable = _takes.Where(take => take.Duration >= 0.35 && File.Exists(take.VideoPath)).ToList();
        if (usable.Count == 0)
        {
            throw new InvalidOperationException("录制太短，没有保存。");
        }

        foreach (var take in usable)
        {
            await FfmpegTools.FinalizeRecordingAsync(_ffmpeg, take.VideoPath);
            if (take.CameraPath is not null && !await FfmpegTools.FinalizeCameraRecordingAsync(_ffmpeg, take.CameraPath))
                take.CameraPath = null;
        }

        var first = await FfmpegTools.Probe(_ffprobe, usable[0].VideoPath);
        var project = new ProjectDocument
        {
            Name = Path.GetFileName(_directory).Replace(".dogscproject", ""),
            Width = first?.Width > 0 ? first.Width : _target.Width,
            Height = first?.Height > 0 ? first.Height : _target.Height,
            FrameRate = _options.FrameRate,
            SourceLabel = usable[0].TargetLabel,
        };
        if (project.Width <= 0 || project.Height <= 0)
        {
            project.Width = 1280;
            project.Height = 720;
        }

        foreach (var take in usable)
        {
            var probed = await FfmpegTools.Probe(_ffprobe, take.VideoPath);
            var asset = new MediaAsset
            {
                Id = take.AssetId,
                SourceLabel = take.TargetLabel,
                Video = "media/" + Path.GetFileName(take.VideoPath),
                Microphone = take.MicrophonePath is null ? null : "media/" + Path.GetFileName(take.MicrophonePath),
                SystemAudio = take.SystemPath is null ? null : "media/" + Path.GetFileName(take.SystemPath),
                Camera = take.CameraPath is null ? null : "media/" + Path.GetFileName(take.CameraPath),
                Duration = probed?.Duration > 0.2 ? probed.Duration : take.Duration,
                Width = probed?.Width > 0 ? probed.Width : project.Width,
                Height = probed?.Height > 0 ? probed.Height : project.Height,
                Cursor = take.Cursor.Where(p => p.Time <= (probed?.Duration > 0.2 ? probed.Duration : take.Duration)).ToList(),
            };
            project.Assets.Add(asset);
            project.Clips.Add(new ClipSegment
            {
                AssetId = asset.Id,
                SourceStart = 0,
                SourceEnd = asset.Duration,
            });
            foreach (var mark in take.Marks)
            {
                if (mark.Time <= asset.Duration)
                {
                    project.Markers.Add(new SourceMarker
                    {
                        AssetId = asset.Id,
                        SourceTime = mark.Time,
                        Note = mark.Note,
                    });
                }
            }
        }

        if (project.Width == 0)
        {
            project.Width = project.Assets[0].Width;
            project.Height = project.Assets[0].Height;
        }

        ScenePresets.ApplyDefault(project, _directory);
        ProjectFile.Save(_directory, project);
        try
        {
            File.Delete(JournalPath());
        }
        catch
        {
            // Journal cleanup is best-effort; recovery ignores it once project.json exists.
        }

        return project;
    }

    /// <summary>Mac 恢复日志：录制中实时写 media/ 旁的 journal.json，崩溃后恢复流程拿得到 take/assetId/标记。</summary>
    string JournalPath() => Path.Combine(_directory, "journal.json");

    void SaveJournal(Take? finished = null)
    {
        var temporary = JournalPath() + ".tmp";
        try
        {
            var takes = _takes.Concat(_current is null ? [] : [_current]).Concat(finished is null || _takes.Contains(finished) || _current == finished ? [] : [finished])
                .Select(take => new JournalTake
                {
                    Token = take.Token,
                    AssetId = take.AssetId,
                    TargetLabel = take.TargetLabel,
                    Duration = take == _current ? _clock.Elapsed.TotalSeconds : take.Duration,
                    Marks = take.Marks.Select(m => new JournalMark { Time = m.Time, Note = m.Note }).ToList(),
                }).ToList();
            var journal = new RecoveryJournal
            {
                StartedUtc = _startedUtc,
                TargetLabel = _takes.FirstOrDefault()?.TargetLabel ?? _current?.TargetLabel ?? _target.Label,
                Kind = _target.Kind.ToString(),
                FrameRate = _options.FrameRate,
                Takes = takes,
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(journal));
            File.Move(temporary, JournalPath(), overwrite: true);
        }
        catch (Exception ex)
        {
            AppPaths.Log("录制恢复日志保存失败：" + ex.Message);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch { /* A failed cleanup must not interrupt the recording. */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopProcessesAsync(graceful: false);
    }

    async Task BeginTakeAsync()
    {
        try { await BeginTakeCoreAsync(); }
        catch
        {
            _current = null;
            _clock.Reset();
            await StopProcessesAsync(graceful: false);
            throw;
        }
    }

    async Task BeginTakeCoreAsync()
    {
        _followLost = false;
        _index++;
        var take = new Take("media-" + _index.ToString("00"));
        take.TargetLabel = _target.Label;
        var media = Path.Combine(_directory, "media");
        take.VideoPath = Path.Combine(media, take.Token + ".mp4");
        var videoArgs = new List<string> { "-y", "-hide_banner", "-loglevel", "warning" };
        if (_target.Kind == CaptureKind.Device)
        {
            videoArgs.AddRange([
                "-f", "dshow",
                "-framerate", _options.FrameRate.ToString(),
                "-i", "video=" + (_target.DeviceName ?? ""),
            ]);
        }
        else if (_target.Kind == CaptureKind.Window)
        {
            _windowCapture = new NativeWindowCapture(_target.WindowHandle, _options.FrameRate);
            try { await _windowCapture.ReadyAsync(); }
            catch { await _windowCapture.DisposeAsync(); _windowCapture = null; throw; }
            var live = Native.WindowRect(_target.WindowHandle);
            take.Area = live is null ? new(_target.X, _target.Y, _target.Width, _target.Height)
                : new(live.Left, live.Top, live.Width, live.Height);
            videoArgs.AddRange(["-f", "rawvideo", "-pixel_format", "bgra", "-video_size",
                _windowCapture.Width + "x" + _windowCapture.Height, "-framerate", _options.FrameRate.ToString(), "-i", "pipe:0"]);
        }
        else
        {
            var region = Native.EvenRegion(_target.X, _target.Y, _target.Width, _target.Height);
            take.Area = new CaptureRegion.Box(region.X, region.Y, region.Width, region.Height);
            videoArgs.AddRange([
                "-f", "gdigrab",
                "-framerate", _options.FrameRate.ToString(),
                "-draw_mouse", "0",
                "-offset_x", region.X.ToString(),
                "-offset_y", region.Y.ToString(),
                "-video_size", region.Width + "x" + region.Height,
                "-i", "desktop",
            ]);
        }

        // 每秒提交并写出分片，避免短录制在首个关键帧间隔内崩溃时只留下 MP4 文件头。
        var crf = _options.Quality switch { "high" => "18", "compact" => "24", _ => "20" };
        videoArgs.AddRange(["-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", crf, "-pix_fmt", "yuv420p",
            "-movflags", "+frag_keyframe+empty_moov", "-frag_duration", "1000000", "-flush_packets", "1", take.VideoPath]);
        _video = FfmpegProcess.Start(_ffmpeg, videoArgs);
        if (_options.Camera && !string.IsNullOrWhiteSpace(_options.CameraName) &&
            !string.Equals(_options.CameraName, _target.DeviceName, StringComparison.Ordinal))
        {
            take.CameraPath = Path.Combine(media, take.Token + "-camera.mp4");
            try
            {
                _camera = FfmpegProcess.Start(_ffmpeg, [
                    "-y", "-hide_banner", "-loglevel", "warning",
                    "-f", "dshow", "-framerate", "30",
                    "-i", "video=" + _options.CameraName,
                    "-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
                    "-movflags", "+frag_keyframe+empty_moov", "-frag_duration", "1000000", "-flush_packets", "1", take.CameraPath,
                ]);
            }
            catch (Exception ex)
            {
                AppPaths.Log("摄像头启动失败：" + ex.Message);
                take.CameraPath = null;
            }
        }

        if (_options.Microphone)
        {
            take.MicrophonePath = Path.Combine(media, take.Token + "-mic.wav");
            try
            {
                _microphone = new WavRecorder();
                _microphone.StartMicrophone(_options.MicrophoneId, take.MicrophonePath);
                _microphone.Muted = MicrophoneMuted;
            }
            catch (Exception ex)
            {
                AppPaths.Log("麦克风启动失败：" + ex.Message);
                _microphone?.Dispose();
                _microphone = null;
                take.MicrophonePath = null;
            }
        }

        if (_options.SystemAudio)
        {
            take.SystemPath = Path.Combine(media, take.Token + "-system.wav");
            try
            {
                _system = new WavRecorder();
                _system.StartLoopback(_options.PlaybackDeviceId, take.SystemPath);
            }
            catch (Exception ex)
            {
                AppPaths.Log("系统声音启动失败：" + ex.Message);
                _system?.Dispose();
                _system = null;
                take.SystemPath = null;
            }
        }

        _current = take;
        SaveJournal();
        _clock.Restart();
        _windowCapture?.StartFrames(_video);
        await Task.Delay(350);
        if (_video is { HasExited: true } || _windowCapture?.Error is not null)
        {
            var error = _windowCapture?.Error?.Message ?? _video!.ErrorText();
            await StopProcessesAsync(graceful: false);
            _current = null;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "画面录制没有开始。" : Tail(error));
        }

        if (_target.Kind == CaptureKind.Window && _target.WindowHandle != IntPtr.Zero)
        {
            _follower = new Timer(FollowWindow, null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
        }

        var samplePeriod = TimeSpan.FromSeconds(1d / _options.FrameRate);
        _cursorTimer = new Timer(SampleCursor, null, samplePeriod, samplePeriod);
        if (_target.Kind != CaptureKind.Device && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try { _clickHook = new MouseClickHook((x, y) => RecordPointer(x, y, click: true)); }
            catch (Exception ex) { AppPaths.Log("鼠标事件监听未启动，使用帧率取样：" + ex.Message); }
        }
    }

    void SampleCursor(object? state)
    {
        var take = _current;
        if (take is null) return;
        var (x, y) = Native.CursorPos();
        var down = Native.LeftButtonDown();
        var click = _clickHook is null && down && !take.ButtonWasDown;
        take.ButtonWasDown = down;
        RecordPointer(x, y, click);
    }

    void RecordPointer(int x, int y, bool click)
    {
        var take = _current;
        if (take is null || take.Area.Width < 2 || take.Area.Height < 2)
        {
            return;
        }

        lock (take.Cursor)
        {
            var last = take.Cursor.LastOrDefault();
            var area = take.Area;
            var visible = x >= area.X && y >= area.Y && x < area.X + area.Width && y < area.Y + area.Height;
            if (_windowCapture is not null)
            {
                var live = Native.WindowRect(_target.WindowHandle);
                visible = live is not null && Native.PointerOverWindow(_target.WindowHandle, x, y);
                if (live is not null) area = new(live.Left, live.Top, live.Width, live.Height);
            }
            if (!visible)
            {
                if (_options.RecordCursor && last?.Cursor != "system.hidden")
                    take.Cursor.Add(new CursorPoint { Time = _clock.Elapsed.TotalSeconds,
                        X = last?.X ?? .5, Y = last?.Y ?? .5, Cursor = "system.hidden" });
                return;
            }
            if (_options.RecordCursor)
            {
                var nx = Math.Clamp((x - area.X) / (double)area.Width, 0, 1);
                var ny = Math.Clamp((y - area.Y) / (double)area.Height, 0, 1);
                if (_windowCapture is { } native)
                {
                    var content = native.Content;
                    nx = (content.X + nx * content.Width) / native.Width;
                    ny = (content.Y + ny * content.Height) / native.Height;
                }
                var cursor = Native.CursorAssetId();
                // Deduplicate stationary samples without dropping small pixel moves.
                if (!click && last is not null && last.X == nx && last.Y == ny && last.Cursor == cursor) return;
                take.Cursor.Add(new CursorPoint { Time = _clock.Elapsed.TotalSeconds,
                    X = nx, Y = ny, Click = click, Cursor = cursor });
            }
        }
        if (click) PointerClicked?.Invoke(x, y);
    }

    // Native capture follows the HWND independently of desktop position. This
    // timer only updates pointer coordinates and records unavailable targets.
    void FollowWindow(object? state)
    {
        var take = _current;
        if (take is null) return;
        var rect = Native.WindowRect(_target.WindowHandle);
        if (rect is null || _windowCapture?.TargetClosed == true)
        {
            if (!_followLost)
            {
                _followLost = true;
                take.Marks.Add((_clock.Elapsed.TotalSeconds, "目标窗口已最小化或关闭，保留最后画面"));
                SaveJournal();
            }
            return;
        }
        _followLost = false;
        take.Area = new(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    static string Tail(string error)
    {
        var lines = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(6));
    }

    async Task EndTakeAsync()
    {
        var take = _current;
        _current = null;
        if (take is null)
        {
            return;
        }

        take.Duration = _clock.Elapsed.TotalSeconds;
        _clock.Reset();
        await StopProcessesAsync(graceful: true);
        SaveJournal(take);
        if (take.Duration >= 0.35 && File.Exists(take.VideoPath))
        {
            _takes.Add(take);
        }
    }

    async Task StopProcessesAsync(bool graceful)
    {
        _clickHook?.Dispose(); _clickHook = null;
        var follower = _follower;
        _follower = null;
        if (follower is not null)
        {
            await follower.DisposeAsync();
        }

        var cursorTimer = _cursorTimer;
        _cursorTimer = null;
        if (cursorTimer is not null)
        {
            await cursorTimer.DisposeAsync();
        }

        var capture = _windowCapture; _windowCapture = null;
        if (capture is not null) await capture.DisposeAsync();
        var video = _video;
        var camera = _camera;
        _video = null;
        _camera = null;
        if (video is not null)
        {
            await video.StopAsync(graceful);
            video.Dispose();
        }

        if (camera is not null)
        {
            await camera.StopAsync(graceful);
            camera.Dispose();
        }

        _microphone?.Stop();
        _microphone = null;
        _system?.Stop();
        _system = null;
    }

    sealed class JournalMark
    {
        public double Time { get; set; }
        public string? Note { get; set; }
    }

    sealed class JournalTake
    {
        public string Token { get; set; } = "";
        public string AssetId { get; set; } = "";
        public string TargetLabel { get; set; } = "";
        public double Duration { get; set; }
        public List<JournalMark> Marks { get; set; } = [];
    }

    sealed class RecoveryJournal
    {
        public DateTime StartedUtc { get; set; }
        public string TargetLabel { get; set; } = "";
        public string Kind { get; set; } = "";
        public int FrameRate { get; set; }
        public List<JournalTake> Takes { get; set; } = [];
    }

    public sealed class JournalTakeInfo
    {
        public string AssetId { get; set; } = "";
        public string TargetLabel { get; set; } = "";
        public List<(double Time, string? Note)> Marks { get; set; } = [];
    }

    public sealed class JournalInfo
    {
        public string TargetLabel { get; init; } = "";
        public int FrameRate { get; init; } = 30;
        public IReadOnlyDictionary<string, JournalTakeInfo> Takes { get; init; } = new Dictionary<string, JournalTakeInfo>();
    }

    /// <summary>读取录制来源、帧率和 token → take 信息；没有或损坏的日志退回默认值。</summary>
    public static JournalInfo ReadJournal(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "journal.json");
            if (!File.Exists(path))
            {
                return new JournalInfo();
            }

            var journal = JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(path));
            if (journal is null) return new JournalInfo();
            return new JournalInfo
            {
                TargetLabel = journal.TargetLabel,
                FrameRate = journal.FrameRate > 0 ? journal.FrameRate : 30,
                Takes = journal.Takes.ToDictionary(
                take => take.Token,
                take => new JournalTakeInfo
                {
                    AssetId = take.AssetId,
                    TargetLabel = take.TargetLabel,
                    Marks = take.Marks.Select(m => (m.Time, m.Note)).ToList(),
                }),
            };
        }
        catch
        {
            return new JournalInfo();
        }
    }

    sealed class Take(string token)
    {
        public string Token { get; } = token;
        public string AssetId { get; } = Guid.NewGuid().ToString("N");
        public string TargetLabel { get; set; } = "";
        public string VideoPath { get; set; } = "";
        public string? MicrophonePath { get; set; }
        public string? SystemPath { get; set; }
        public string? CameraPath { get; set; }
        public double Duration { get; set; }
        /// <summary>视频帧内容在屏幕上的物理矩形（光标归一化基准）。</summary>
        public CaptureRegion.Box Area { get; set; }
        public List<CursorPoint> Cursor { get; } = [];
        public bool ButtonWasDown { get; set; }
        public List<(double Time, string? Note)> Marks { get; } = [];
    }
}
