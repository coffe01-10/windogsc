using System.Windows.Controls.Primitives;

namespace DogSC;

public partial class EditorWindow
{
    double _chromeScale = .86;
    bool _timelineHeightEdited;
    bool _workspaceInitialized;

    void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (InspectorSurface is null || Timeline is null) return;
        var area = SystemParameters.WorkArea;
        var compression = Math.Clamp(Math.Max(Math.Max((1440 - ActualWidth) / 360, (820 - ActualHeight) / 220),
            Math.Max((2048 - area.Width) / 640, (1200 - area.Height) / 260)), 0, 1);
        var scale = 1 - .14 * compression;
        if (!_workspaceInitialized || Math.Abs(scale - _chromeScale) > .001)
        {
            _chromeScale = scale;
            ToolRailSurface.LayoutTransform = new ScaleTransform(scale, scale);
            InspectorSurface.LayoutTransform = new ScaleTransform(scale, scale);
            MonitorToolbarSurface.LayoutTransform = new ScaleTransform(scale, scale);
            InspectorColumn.Width = new GridLength(384 * scale);
            HeaderRow.Height = new GridLength(72 - 16 * compression);
        }
        if (!_workspaceInitialized)
        {
            VideoViewButton.Content = IconLabel(IconKind.InsetRectangle, L.Text("视频"));
            WaveformViewButton.Content = IconLabel(IconKind.WaveformRectangle, L.Text("波形"));
            MarkersToggle.Content = IconLabel(IconKind.Bookmark, L.Text("标记"));
            SnapToggle.Content = IconLabel(IconKind.Magnet, L.Text("吸附"));
            Timeline.TrackMenuRequested += (_, _) => TrackMenu_Click(Timeline, new RoutedEventArgs());
            Timeline.TrackHidden += (_, bit) => ApplyTrackFlags(TrackFlags & ~bit);
            _workspaceInitialized = true;
        }
        InspectorToolbarRow.Height = new GridLength(MonitorToolbarSurface.ActualHeight * scale + 16);
        UpdateTimelineLayout();
        LayoutPreview();
    }

    void UpdateTimelineLayout()
    {
        if (_timelineHeightEdited || WorkspaceRoot is null || Timeline is null || WorkspaceRoot.ActualHeight < 200) return;
        var available = WorkspaceRoot.ActualHeight - HeaderRow.Height.Value;
        TimelineRow.Height = new GridLength(Math.Min(Math.Max(228, Timeline.MinHeight + 64), available * .44));
    }

    void TimelineSplitter_DragCompleted(object sender, DragCompletedEventArgs e) => _timelineHeightEdited = true;

    void PreviewMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        foreach (var (title, key) in new[] { (L.Text("完整分辨率 · 保留源素材细节"), "full"), (L.Text("流畅预览 · 更顺畅地播放"), "low") })
        {
            var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = _player.Quality == key };
            item.Click += (_, _) => { SettingsStore.Current.PreviewQuality = key; SettingsStore.Save(); foreach (var editor in Application.Current.Windows.OfType<EditorWindow>()) editor.SyncPreviewQuality(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
