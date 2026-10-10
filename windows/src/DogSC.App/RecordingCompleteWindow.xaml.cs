using System.Diagnostics;

namespace DogSC;

public partial class RecordingCompleteWindow : Window
{
    readonly string _directory;
    CancellationTokenSource? _thumbnailCancellation;

    public RecordingCompleteWindow(string directory)
    {
        InitializeComponent();
        _directory = directory;
        InfoLabel.Text = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
        Loaded += async (_, _) =>
        {
            WindowPlacement.KeepInWorkArea(this, center: true);
            await LoadThumbnailAsync();
        };
        Closed += (_, _) => _thumbnailCancellation?.Cancel();
    }

    async Task LoadThumbnailAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _thumbnailCancellation = cancellation;
        try
        {
            var bitmap = await RecordingThumbnail.LoadAsync(_directory, cancellation.Token);
            if (!cancellation.IsCancellationRequested) Thumb.Source = bitmap;
        }
        catch
        {
            // 缩略图只是装饰，失败不影响主流程。
        }
        finally { _thumbnailCancellation = null; }
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
