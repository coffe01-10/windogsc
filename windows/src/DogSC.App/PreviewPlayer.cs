using DogSC.Core;

namespace DogSC;

public sealed class PreviewPlayer : IDisposable
{
    readonly MediaPlayer _screen = new() { ScrubbingEnabled = true, Volume = 0 };
    readonly MediaPlayer _camera = new() { ScrubbingEnabled = true, Volume = 0 };
    readonly MediaPlayer _microphone = new() { Volume = 1 };
    readonly MediaPlayer _system = new() { Volume = 1 };
    readonly DispatcherTimer _timer;
    ProjectDocument? _project;
    (string Id, string AssetId, double Start, double End, double Speed)[] _timeline = [];
    string _directory = "";
    string? _clipId;
    bool _wantPlay;
    DateTime _holdUntil;
    TimeSpan _pendingPosition;
    bool _screenPending;
    bool _cameraPending;
    bool _microphonePending;
    bool _systemPending;
    MediaAsset? _activeAsset;
    double _cameraAdvance;
    (string Id, string Video, string? Camera, string? Mic, string? System, double CamOffset, double MicOffset)[] _mediaState = [];
    (string Id, double Time, double Offset)[] _syncState = [];
    readonly CancellationTokenSource _proxyCancellation = new();
    readonly HashSet<string> _proxyJobs = new(StringComparer.OrdinalIgnoreCase);
    bool _disposed;

    public PreviewPlayer()
    {
        ScreenDrawing = new VideoDrawing { Rect = new Rect(0, 0, 16, 9), Player = _screen };
        CameraDrawing = new VideoDrawing { Rect = new Rect(0, 0, 16, 9), Player = _camera };
        _screen.MediaOpened += (_, _) => OnOpened(_screen, ScreenDrawing, ref _screenPending);
        _camera.MediaOpened += (_, _) => { OnOpened(_camera, CameraDrawing, ref _cameraPending); MediaGeometryChanged?.Invoke(); };
        _microphone.MediaOpened += (_, _) => OnOpened(_microphone, null, ref _microphonePending);
        _system.MediaOpened += (_, _) => OnOpened(_system, null, ref _systemPending);
        _screen.MediaFailed += (_, args) => Failed?.Invoke(args.ErrorException.Message);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => OnTick();
    }

    public VideoDrawing ScreenDrawing { get; }
    public VideoDrawing CameraDrawing { get; }
    /// <summary>预览画质："low" 走四分之一分辨率代理文件（解码更省），"full" 播原素材。</summary>
    public string Quality { get; set; } = "full";
    public bool IsPlaying { get; private set; }
    public double Time { get; private set; }
    public event Action? Changed;
    public event Action? MediaGeometryChanged;
    public event Action<string>? Failed;

    public void Open(ProjectDocument project, string directory)
    {
        _project = project;
        _directory = directory;
        _clipId = null;
        Time = 0;
        IsPlaying = false;
        _timeline = [];
        UpdateProject(project);
    }

    public void UpdateProject(ProjectDocument project)
    {
        var timeline = project.Clips.Select(clip =>
            (clip.Id, clip.AssetId, clip.SourceStart, clip.SourceEnd, clip.Speed)).ToArray();
        var media = project.Assets.Select(a => (a.Id, a.Video, a.Camera, a.Microphone, a.SystemAudio, a.CameraOffset, a.MicrophoneOffset)).ToArray();
        var sync = project.Assets.SelectMany(a => a.CameraSyncAnchors.Select(s => (a.Id, s.SourceTime, s.Offset))).ToArray();
        var changed = !ReferenceEquals(_project, project) || !_timeline.SequenceEqual(timeline) || _cameraAdvance != project.Camera.TimingAdvance
            || !_mediaState.SequenceEqual(media) || !_syncState.SequenceEqual(sync);
        _mediaState = media;
        _syncState = sync;
        _cameraAdvance = project.Camera.TimingAdvance;
        _project = project;
        SetGains(project.MicrophoneGain, project.SystemGain);
        if (changed)
        {
            _timeline = timeline;
            Seek(Time, IsPlaying);
        }
    }

    public void Toggle()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else if (_project is not null)
        {
            var duration = TimelineMath.Duration(_project);
            if (Time >= duration - 0.05)
            {
                Time = 0;
            }

            Seek(Time, play: true);
        }
    }

    public void Pause()
    {
        IsPlaying = false;
        _wantPlay = false;
        _timer.Stop();
        _screen.Pause();
        _camera.Pause();
        _microphone.Pause();
        _system.Pause();
        Changed?.Invoke();
    }

    public void Seek(double timeline, bool play)
    {
        if (_project is null)
        {
            return;
        }

        var duration = TimelineMath.Duration(_project);
        timeline = Math.Clamp(timeline, 0, Math.Max(0, duration));
        var hit = TimelineMath.Hit(_project, Math.Min(timeline, Math.Max(0, duration - 0.001)));
        if (hit is null)
        {
            Pause();
            Time = duration;
            Changed?.Invoke();
            return;
        }

        Time = timeline;
        _clipId = hit.Value.Clip.Clip.Id;
        _wantPlay = play;
        _holdUntil = DateTime.UtcNow.AddMilliseconds(180);
        _pendingPosition = TimeSpan.FromSeconds(Math.Max(0, hit.Value.SourceTime));
        var speed = hit.Value.Clip.Clip.Speed;
        _screen.SpeedRatio = speed;
        _camera.SpeedRatio = speed;
        _microphone.SpeedRatio = speed;
        _system.SpeedRatio = speed;
        OpenAsset(hit.Value.Clip.Asset);
        _camera.SpeedRatio = speed * SyncAnchorCurve.SourceRate(hit.Value.Clip.Asset.CameraSyncAnchors, hit.Value.SourceTime);
        if (play)
        {
            _screen.Play();
            _camera.Play();
            _microphone.Play();
            _system.Play();
            IsPlaying = true;
            _timer.Start();
        }
        else
        {
            IsPlaying = false;
            _timer.Stop();
            Nudge(_screen);
            Nudge(_camera);
            _microphone.Pause();
            _system.Pause();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// MediaPlayer.Volume 上限是 1，增益超过 100% 的部分预览放不出来，
    /// 由界面提示「以导出为准」（导出走 ffmpeg volume=，上限是
    /// <see cref="ProjectDocument.MaxGain"/>）。
    /// </summary>
    public void SetGains(double microphone, double system)
    {
        _microphone.Volume = Math.Clamp(microphone, 0, 1);
        _system.Volume = Math.Clamp(system, 0, 1);
    }

    public void Dispose()
    {
        _disposed = true;
        _proxyCancellation.Cancel();
        _timer.Stop();
        _screen.Close();
        _camera.Close();
        _microphone.Close();
        _system.Close();
    }

    void OnTick()
    {
        if (_project is null || !IsPlaying || DateTime.UtcNow < _holdUntil)
        {
            return;
        }

        var placed = TimelineMath.Place(_project);
        var clip = placed.FirstOrDefault(item => item.Clip.Id == _clipId) ?? placed.LastOrDefault();
        if (clip is null)
        {
            Pause();
            return;
        }

        var source = _screen.Position.TotalSeconds;
        if (source >= clip.Clip.SourceEnd - 0.04)
        {
            var next = clip.End + 0.01;
            if (next >= placed[^1].End - 0.01)
            {
                Time = placed[^1].End;
                Pause();
                return;
            }

            Seek(next, play: true);
            return;
        }

        Time = clip.Start + Math.Max(0, source - clip.Clip.SourceStart) / clip.Clip.Speed;
        var drift = Math.Abs(_microphone.Position.TotalSeconds - Math.Max(0, source + (_activeAsset?.MicrophoneOffset ?? 0)));
        if (_microphone.Source is not null && drift > 0.35)
        {
            _microphone.Position = TimeSpan.FromSeconds(Math.Max(0, source + (_activeAsset?.MicrophoneOffset ?? 0)));
        }
        var cameraSource = CameraSource(source);
        if (_activeAsset is not null) _camera.SpeedRatio = clip.Clip.Speed * SyncAnchorCurve.SourceRate(_activeAsset.CameraSyncAnchors, source);
        if (_camera.Source is not null && Math.Abs(_camera.Position.TotalSeconds - cameraSource) > 0.12)
            _camera.Position = TimeSpan.FromSeconds(cameraSource);

        if (_system.Source is not null && Math.Abs(_system.Position.TotalSeconds - source) > 0.35)
        {
            _system.Position = TimeSpan.FromSeconds(source);
        }

        Changed?.Invoke();
    }

    void OpenAsset(MediaAsset asset)
    {
        _activeAsset = asset;
        OpenMedia(_screen, PreviewVideo(ProjectFile.Media(_directory, asset.Video)), ref _screenPending);
        if (!string.IsNullOrWhiteSpace(asset.Camera))
        {
            OpenMedia(_camera, PreviewVideo(ProjectFile.Media(_directory, asset.Camera)), ref _cameraPending);
        }
        else _camera.Close();

        OpenAudio(_microphone, asset.Microphone, ref _microphonePending);
        OpenAudio(_system, asset.SystemAudio, ref _systemPending);
    }

    void OpenAudio(MediaPlayer player, string? relative, ref bool pending)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            player.Stop();
            return;
        }

        OpenMedia(player, ProjectFile.Media(_directory, relative), ref pending);
    }

    /// <summary>
    /// 低画质档把屏幕/摄像头视频换成项目目录下 previews/ 里的四分之一分辨率代理，
    /// 代理按源文件 mtime 复用；转码失败或无 ffmpeg 时回退原文件。与 Mac 的
    /// 「清晰预览/完整分辨率」同语义：只影响预览解码，导出不走这里。
    /// </summary>
    string PreviewVideo(string path)
    {
        if (Quality != "low" || !File.Exists(path))
        {
            return path;
        }

        try
        {
            var proxyDirectory = Path.Combine(_directory, "previews");
            var proxy = Path.Combine(proxyDirectory,
                Path.GetFileNameWithoutExtension(path) + ".q.mp4");
            var sourceStamp = File.GetLastWriteTimeUtc(path);
            if (File.Exists(proxy) && new FileInfo(proxy).Length > 0
                && File.GetLastWriteTimeUtc(proxy) >= sourceStamp)
            {
                return proxy;
            }

            var ffmpeg = FfmpegTools.LocateFfmpeg();
            if (ffmpeg is null)
            {
                return path;
            }

            if (_proxyJobs.Add(path)) _ = PrepareProxyAsync(path, proxy, proxyDirectory);
        }
        catch (Exception ex) { AppPaths.Log("预览代理：" + ex.Message); }
        return path;
    }

    async Task PrepareProxyAsync(string source, string proxy, string directory)
    {
        var temporary = proxy + ".tmp.mp4";
        FfmpegProcess? process = null;
        try
        {
            Directory.CreateDirectory(directory);
            process = FfmpegProcess.Start(FfmpegTools.LocateFfmpeg()!, ["-y", "-i", source,
                "-vf", "scale=max(2\\,trunc(iw/4)*2):max(2\\,trunc(ih/4)*2)",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-an",
                "-movflags", "+faststart", temporary]);
            if (await process.WaitAsync(_proxyCancellation.Token) != 0)
                throw new InvalidOperationException(process.ErrorText());
            _proxyCancellation.Token.ThrowIfCancellationRequested();
            File.Move(temporary, proxy, overwrite: true);
            // Keep playback uninterrupted; paused previews can switch immediately.
            if (!_disposed && !IsPlaying) Seek(Time, play: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppPaths.Log("预览代理：" + ex.Message); }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited) await process.StopAsync(graceful: false);
                process.Dispose();
            }
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException ex) { AppPaths.Log("预览代理清理：" + ex.Message); }
            _proxyJobs.Remove(source);
        }
    }

    void OpenMedia(MediaPlayer player, string path, ref bool pending)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var uri = new Uri(path);
        if (player.Source != uri)
        {
            pending = true;
            player.Open(uri);
            return;
        }

        player.Position = PendingPosition(player);
    }

    void OnOpened(MediaPlayer player, VideoDrawing? drawing, ref bool pending)
    {
        if (drawing is not null)
        {
            Fit(player, drawing);
        }

        if (!pending)
        {
            return;
        }

        pending = false;
        player.Position = PendingPosition(player);
        if (_wantPlay)
        {
            player.Play();
        }
        else if (drawing is not null)
        {
            Nudge(player);
        }
        else
        {
            player.Pause();
        }
    }

    static void Fit(MediaPlayer player, VideoDrawing drawing)
    {
        if (player.NaturalVideoWidth > 0)
        {
            drawing.Rect = new Rect(0, 0, player.NaturalVideoWidth, player.NaturalVideoHeight);
        }
    }

    TimeSpan PendingPosition(MediaPlayer player)
    {
        if (ReferenceEquals(player, _camera)) return TimeSpan.FromSeconds(CameraSource(_pendingPosition.TotalSeconds));
        var offset = ReferenceEquals(player, _microphone) ? _activeAsset?.MicrophoneOffset ?? 0 : 0;
        return TimeSpan.FromSeconds(Math.Max(0, _pendingPosition.TotalSeconds + offset));
    }

    double CameraSource(double source) => Math.Max(0, source + (_activeAsset?.CameraOffset ?? 0)
        + _cameraAdvance + (_activeAsset is null ? 0 : SyncAnchorCurve.Offset(_activeAsset.CameraSyncAnchors, source)));

    static void Nudge(MediaPlayer player)
    {
        player.Play();
        player.Pause();
    }
}
