using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DogSC;

public sealed record AudioEndpointInfo(string Id, string Name);

public static class AudioCatalog
{
    public static IReadOnlyList<AudioEndpointInfo> Microphones() => Endpoints(DataFlow.Capture);

    public static IReadOnlyList<AudioEndpointInfo> Speakers() => Endpoints(DataFlow.Render);

    static IReadOnlyList<AudioEndpointInfo> Endpoints(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
                .Select(device => new AudioEndpointInfo(device.ID, device.FriendlyName))
                .ToList();
        }
        catch (Exception ex)
        {
            AppPaths.Log("枚举音频设备失败：" + ex.Message);
            return [];
        }
    }
}

public sealed class WavRecorder : IDisposable
{
    readonly object _gate = new();
    WasapiCapture? _capture;
    WaveFileWriter? _writer;
    bool _open;
    public bool Muted { get; set; }

    public void StartMicrophone(string? deviceId, string path) =>
        Start(false, deviceId, path);

    public void StartLoopback(string? deviceId, string path) =>
        Start(true, deviceId, path);

    void Start(bool loopback, string? deviceId, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var device = Find(loopback ? DataFlow.Render : DataFlow.Capture, deviceId);
        _capture = loopback
            ? device is null ? new WasapiLoopbackCapture() : new WasapiLoopbackCapture(device)
            : device is null ? new WasapiCapture() : new WasapiCapture(device);
        _writer = new WaveFileWriter(path, _capture.WaveFormat);
        _open = true;
        _capture.DataAvailable += (_, args) =>
        {
            lock (_gate)
            {
                if (_open)
                {
                    if (Muted) Array.Clear(args.Buffer, 0, args.BytesRecorded);
                    _writer?.Write(args.Buffer, 0, args.BytesRecorded);
                }
            }
        };
        _capture.StartRecording();
    }

    public void Stop()
    {
        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            // Stopping an already stopped capture is fine.
        }

        lock (_gate)
        {
            _open = false;
            _writer?.Dispose();
            _writer = null;
        }

        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose() => Stop();

    static MMDevice? Find(DataFlow flow, string? id)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return enumerator.GetDevice(id);
            }
            catch
            {
                // Fall back to the default endpoint.
            }
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        }
        catch
        {
            return null;
        }
    }
}

public static class CameraCatalog
{
    public static IReadOnlyList<string> Devices(string ffmpeg)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = "-hide_banner -list_devices true -f dshow -i dummy",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
            });
            if (process is null)
            {
                return [];
            }

            var text = process.StandardError.ReadToEnd();
            process.WaitForExit(8000);
            var names = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                var video = line.IndexOf("(video)", StringComparison.OrdinalIgnoreCase);
                var first = line.IndexOf('"');
                var second = first >= 0 ? line.IndexOf('"', first + 1) : -1;
                if (video > second && first >= 0 && second > first)
                {
                    names.Add(line[(first + 1)..second]);
                }
            }

            return names;
        }
        catch (Exception ex)
        {
            AppPaths.Log("枚举摄像头失败：" + ex.Message);
            return [];
        }
    }
}
