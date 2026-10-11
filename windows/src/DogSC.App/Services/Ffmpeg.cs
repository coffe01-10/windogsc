using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DogSC;

public sealed record MediaInfo(int Width, int Height, double Duration, bool HasAudio, string? VideoCodec = null);

public sealed class FfmpegProcess : IDisposable
{
    readonly Process _process;
    readonly StringBuilder _error = new();
    public event Action<double>? Progress;

    public bool HasExited => _process.HasExited;
    public ValueTask WriteInputAsync(ReadOnlyMemory<byte> pixels, CancellationToken cancellation) =>
        _process.StandardInput.BaseStream.WriteAsync(pixels, cancellation);
    public void CompleteInput() => _process.StandardInput.Close();

    FfmpegProcess(Process process)
    {
        _process = process;
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data) || _error.Length > 24000)
            {
                return;
            }

            lock (_error)
            {
                _error.AppendLine(args.Data);
            }
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            if (args.Data.StartsWith("out_time_ms=", StringComparison.Ordinal) ||
                args.Data.StartsWith("out_time_us=", StringComparison.Ordinal))
            {
                var raw = args.Data.Split('=', 2)[1];
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                {
                    Progress?.Invoke(microseconds / 1_000_000d);
                }
            }
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
    }

    public static FfmpegProcess Start(string ffmpeg, IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = Join(args),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 ffmpeg。");
        ChildJob.Assign(process);
        AppPaths.Log("ffmpeg " + start.Arguments);
        return new FfmpegProcess(process);
    }

    /// <summary>
    /// 通过 stdin 给滤镜图发运行命令（ffmpeg 交互式 'c' 命令，
    /// 例如 c -1 crop@pos x 100）。失败静默——录制不能因此中断。
    /// </summary>
    public bool SendFilterCommand(string target, string command, string argument)
    {
        if (_process.HasExited)
        {
            return false;
        }

        try
        {
            _process.StandardInput.WriteLine("c -1 " + target + " " + command + " " + argument);
            _process.StandardInput.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> StopAsync(bool graceful)
    {
        if (graceful && !_process.HasExited)
        {
            try
            {
                await _process.StandardInput.WriteLineAsync("q");
                await _process.StandardInput.FlushAsync();
            }
            catch
            {
                // The process may already have closed stdin.
            }

            if (await WaitAsync(TimeSpan.FromSeconds(8)))
            {
                return _process.ExitCode;
            }
        }

        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }

            await WaitAsync(TimeSpan.FromSeconds(3));
        }

        return _process.HasExited ? _process.ExitCode : -1;
    }

    public async Task<int> WaitAsync(CancellationToken cancellation = default)
    {
        try
        {
            await _process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(graceful: false);
        }

        return _process.HasExited ? _process.ExitCode : -1;
    }

    public string ErrorText()
    {
        lock (_error)
        {
            return _error.ToString();
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }

    async Task<bool> WaitAsync(TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cancel.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return _process.HasExited;
        }
    }

    public static string Join(IReadOnlyList<string> args) =>
        string.Join(" ", args.Select(Quote));

    public static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (value.Any(char.IsWhiteSpace) || value.Contains('"'))
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        return value;
    }
}

public static class FfmpegTools
{
    // Fragmented recordings survive an interrupted process, but WPF reports
    // no usable duration for empty_moov files. Remux completed/recovered video
    // without re-encoding before handing it to the editor.
    public static async Task FinalizeRecordingAsync(string ffmpeg, string path)
    {
        var temporary = path + ".tmp";
        try
        {
            using var process = FfmpegProcess.Start(ffmpeg,
                ["-y", "-i", path, "-map", "0", "-c", "copy", "-movflags", "+faststart", "-f", "mp4", temporary]);
            if (await process.WaitAsync() != 0)
                throw new IOException("无法整理录制视频：" + process.ErrorText());
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public static async Task<bool> FinalizeCameraRecordingAsync(string ffmpeg, string path)
    {
        if (!File.Exists(path)) return false;
        try { await FinalizeRecordingAsync(ffmpeg, path); return true; }
        catch (Exception ex)
        {
            // A camera that stopped before writing a frame must not prevent
            // saving the otherwise usable screen recording.
            AppPaths.Log("摄像头录制视频无法整理：" + ex.Message);
            return false;
        }
    }

    public static string? LocateFfmpeg()
    {
        foreach (var candidate in Candidates("ffmpeg.exe"))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Which("ffmpeg.exe");
    }

    public static string? LocateFfprobe(string? ffmpeg)
    {
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        foreach (var candidate in Candidates("ffprobe.exe"))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Which("ffprobe.exe");
    }

    public static async Task<MediaInfo?> Probe(string ffprobe, string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = ffprobe,
            Arguments = "-v error -print_format json -show_streams -show_format " + FfmpegProcess.Quote(path),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        });
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        using var document = JsonDocument.Parse(output);
        var width = 0;
        var height = 0;
        var hasAudio = false;
        string? videoCodec = null;
        if (document.RootElement.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var codec) ? codec.GetString() : "";
                if (type == "video" && width == 0 && stream.TryGetProperty("width", out var w))
                {
                    width = w.GetInt32();
                    height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                    videoCodec = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                }

                if (type == "audio")
                {
                    hasAudio = true;
                }
            }
        }

        double duration = 0;
        if (document.RootElement.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durationValue))
        {
            double.TryParse(durationValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
        }

        return new MediaInfo(width, height, duration, hasAudio, videoCodec);
    }

    static IEnumerable<string> Candidates(string file)
    {
        if (!string.IsNullOrWhiteSpace(SettingsStore.Current.FfmpegPath) && file.StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            yield return SettingsStore.Current.FfmpegPath!;
        }

        var env = Environment.GetEnvironmentVariable("DOGSC_FFMPEG");
        if (!string.IsNullOrWhiteSpace(env) && file.StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            yield return env;
        }

        yield return Path.Combine(AppContext.BaseDirectory, file);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", file);
        yield return Path.Combine(@"C:\ffmpeg\bin", file);
    }

    static string? Which(string file)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = file,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return null;
            }

            var line = process.StandardOutput.ReadLine();
            process.WaitForExit(2000);
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Job Object with KILL_ON_JOB_CLOSE：DogSC 进程退出（含被强杀）时
/// 连带杀掉所有挂进来的子进程，防止孤儿 ffmpeg 继续录。
/// </summary>
static class ChildJob
{
    // Recording and export can start FFmpeg concurrently. Publish the handle
    // only after KILL_ON_JOB_CLOSE has been configured.
    static readonly Lazy<IntPtr> Job = new(Create);

    public static void Assign(Process process)
    {
        try
        {
            var job = Job.Value;
            if (job != IntPtr.Zero && !AssignProcessToJobObject(job, process.Handle))
            {
                AppPaths.Log("FFmpeg 子进程自动清理不可用：" + Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("FFmpeg 子进程自动清理不可用：" + ex.Message);
        }
    }

    static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            AppPaths.Log("FFmpeg 作业对象创建失败：" + Marshal.GetLastWin32Error());
            return IntPtr.Zero;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JobObjectLimitKillOnJobClose },
        };
        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (SetInformationJobObject(job, JobObjectInfoTypeExtendedLimitInformation, pointer, (uint)length)) return job;
            AppPaths.Log("FFmpeg 作业对象退出清理配置失败：" + Marshal.GetLastWin32Error());
            CloseHandle(job);
            return IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    const uint JobObjectLimitKillOnJobClose = 0x2000;
    const int JobObjectInfoTypeExtendedLimitInformation = 9;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int infoType, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
