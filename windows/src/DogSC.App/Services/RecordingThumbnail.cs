using DogSC.Core;
using System.Globalization;
using System.Windows.Media.Imaging;

namespace DogSC;

static class RecordingThumbnail
{
    public static async Task<BitmapSource?> LoadAsync(string directory, CancellationToken cancellation)
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg();
        if (ffmpeg is null) return null;
        var project = ProjectFile.Load(directory);
        var clip = project.Clips.FirstOrDefault();
        var asset = project.Assets.FirstOrDefault(a => a.Id == clip?.AssetId);
        if (clip is null || asset is null) return null;
        var png = Path.Combine(Path.GetTempPath(), "dogsc-complete-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var process = FfmpegProcess.Start(ffmpeg, [
                "-hide_banner", "-loglevel", "error", "-y", "-ss", clip.SourceStart.ToString("0.######", CultureInfo.InvariantCulture),
                "-i", ProjectFile.Media(directory, asset.Video), "-frames:v", "1", "-vf", "scale=640:-1", png,
            ]);
            if (await process.WaitAsync(timeout.Token) != 0 || timeout.IsCancellationRequested) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(png);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            if (File.Exists(png)) File.Delete(png);
        }
    }
}
