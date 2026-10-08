using DogSC.Core;

namespace DogSC;

public enum CaptureKind
{
    Display,
    Window,
    Region,
    Device,
}

public sealed record CaptureTarget(CaptureKind Kind, string Label, int X, int Y, int Width, int Height, string? DeviceName);

public sealed class RecordingOptions
{
    public int FrameRate { get; init; } = 30;
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
    readonly CaptureTarget _target;
    readonly RecordingOptions _options;
    readonly string _directory;
    readonly List<Take> _takes = [];
    Take? _current;
    FfmpegProcess? _video;
    FfmpegProcess? _camera;
    WavRecorder? _microphone;
    WavRecorder? _system;
    readonly System.Diagnostics.Stopwatch _clock = new();
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

        var first = await FfmpegTools.Probe(_ffprobe, usable[0].VideoPath);
        var project = new ProjectDocument
        {
            Name = Path.GetFileName(_directory).Replace(".dogscproject", ""),
            Width = first?.Width > 0 ? first.Width : _target.Width,
            Height = first?.Height > 0 ? first.Height : _target.Height,
            FrameRate = _options.FrameRate,
            SourceLabel = _target.Label,
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
                Video = "media/" + Path.GetFileName(take.VideoPath),
                Microphone = take.MicrophonePath is null ? null : "media/" + Path.GetFileName(take.MicrophonePath),
                SystemAudio = take.SystemPath is null ? null : "media/" + Path.GetFileName(take.SystemPath),
                Camera = take.CameraPath is null ? null : "media/" + Path.GetFileName(take.CameraPath),
                Duration = probed?.Duration > 0.2 ? probed.Duration : take.Duration,
                Width = probed?.Width > 0 ? probed.Width : project.Width,
                Height = probed?.Height > 0 ? probed.Height : project.Height,
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

        ProjectFile.Save(_directory, project);
        return project;
    }

    public async ValueTask DisposeAsync()
    {
        await StopProcessesAsync(graceful: false);
    }

    async Task BeginTakeAsync()
    {
        _index++;
        var take = new Take("media-" + _index.ToString("00"));
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
        else
        {
            var region = Native.EvenRegion(_target.X, _target.Y, _target.Width, _target.Height);
            videoArgs.AddRange([
                "-f", "gdigrab",
                "-framerate", _options.FrameRate.ToString(),
                "-draw_mouse", "1",
                "-offset_x", region.X.ToString(),
                "-offset_y", region.Y.ToString(),
                "-video_size", region.Width + "x" + region.Height,
                "-i", "desktop",
            ]);
        }

        videoArgs.AddRange(["-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", take.VideoPath]);
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
                    take.CameraPath,
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
        _clock.Restart();
        await Task.Delay(350);
        if (_video is { HasExited: true })
        {
            var error = _video.ErrorText();
            await StopProcessesAsync(graceful: false);
            _current = null;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "画面录制没有开始。" : Tail(error));
        }
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
        if (take.Duration >= 0.35 && File.Exists(take.VideoPath))
        {
            _takes.Add(take);
        }
    }

    async Task StopProcessesAsync(bool graceful)
    {
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

    sealed class Take(string token)
    {
        public string Token { get; } = token;
        public string AssetId { get; } = Guid.NewGuid().ToString("N");
        public string VideoPath { get; set; } = "";
        public string? MicrophonePath { get; set; }
        public string? SystemPath { get; set; }
        public string? CameraPath { get; set; }
        public double Duration { get; set; }
        public List<(double Time, string? Note)> Marks { get; } = [];
    }
}
