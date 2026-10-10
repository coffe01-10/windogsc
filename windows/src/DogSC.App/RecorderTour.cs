using System.Windows.Controls.Primitives;

namespace DogSC;

public partial class RecorderBarWindow
{
    Popup? _recorderTour;

    void ShowRecorderTour(int step)
    {
        _recorderTour?.SetCurrentValue(Popup.IsOpenProperty, false);
        var steps = new (FrameworkElement Target, string Title, string Body)[]
        {
            (DisplayButton, "选择录制范围", "显示器录全屏，窗口选择应用，区域框选范围；设备用于视频采集设备。选择目标后可开始录制。"),
            (MicButton, "声音与摄像头", "麦克风、系统声音和摄像头分别开启。录制中可暂停、继续、静音麦克风和添加时间标记。"),
            (MainMenuButton, "项目与备忘录", "菜单中打开项目、导入视频、选择保存位置和调整设置。备忘录支持编辑、阅读、字号与透明度，录制时不会进入画面。"),
        };
        var item = steps[step];
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = item.Title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = item.Body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) });
        var row = new DockPanel();
        var skip = new Button { Content = "跳过", Style = (Style)FindResource("QuietButton") };
        skip.Click += (_, _) => EndRecorderTour(); row.Children.Add(skip);
        var next = new Button { Content = step == steps.Length - 1 ? "完成" : "下一步", HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)FindResource("PrimaryButton") };
        next.Click += (_, _) => { if (step == steps.Length - 1) EndRecorderTour(); else ShowRecorderTour(step + 1); };
        row.Children.Add(next); body.Children.Add(row);
        var card = new Border { Width = 320, Padding = new Thickness(18), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = body };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        _recorderTour = new Popup { PlacementTarget = _collapsed ? CollapseButton : item.Target, Placement = VerticalBar ? PlacementMode.Right : PlacementMode.Bottom,
            AllowsTransparency = true, StaysOpen = false, Child = card, VerticalOffset = 6, IsOpen = true };
    }

    void EndRecorderTour()
    {
        if (_recorderTour is not null) _recorderTour.IsOpen = false;
        SettingsStore.Current.RecorderTourSeen = true; SettingsStore.Save();
    }
}
