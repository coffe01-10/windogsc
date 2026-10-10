using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace DogSC;

public partial class RecordingCompleteWindow : Window
{
    readonly string _directory;

    public RecordingCompleteWindow(string directory)
    {
        InitializeComponent();
        _directory = directory;
        InfoLabel.Text = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
        LoadThumbnail();
    }

    void LoadThumbnail()
    {
        try
        {
            var media = Path.Combine(_directory, "media");
            var video = Directory.Exists(media)
                ? Directory.GetFiles(media, "*.mp4").FirstOrDefault(f => !f.Contains("-camera"))
                : null;
            var ffmpeg = FfmpegTools.LocateFfmpeg();
            if (video is null || ffmpeg is null) return;
            var png = Path.Combine(Path.GetTempPath(), "dogsc-complete-" + Guid.NewGuid().ToString("N") + ".png");
            var p = Process.Start(new ProcessStartInfo(ffmpeg,
                $"-hide_banner -loglevel error -y -i \"{video}\" -frames:v 1 -vf scale=640:-1 \"{png}\"")
            { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(5000);
            if (File.Exists(png) && new FileInfo(png).Length > 0)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(png);
                bmp.EndInit();
                bmp.Freeze();
                Thumb.Source = bmp;
            }
        }
        catch
        {
            // 缩略图只是装饰，失败不影响主流程。
        }
    }

    void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        EditorWindow.Open(_directory);
        Close();
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _directory + "\"") { UseShellExecute = true });
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
