using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    void SceneMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SceneMenuButton };
        foreach (var preset in SettingsStore.Current.ScenePresets)
        {
            var item = new MenuItem { Header = preset.Name, IsCheckable = true, IsChecked = preset.Id == _selectedScenePreset };
            item.Click += (_, _) => ApplyScenePreset(preset); menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var save = new MenuItem { Header = "保存为新场景…", Icon = new Icon { Kind = IconKind.Plus, Width = 16, Height = 16 } };
        save.Click += (_, _) => ShowSaveScene(); menu.Items.Add(save); menu.IsOpen = true;
    }

    void ApplyScenePreset(ScenePreset preset)
    {
        var transition = FluidMotion.PrepareSwap(PreviewCanvas, PreviewTransitionLayer, 0);
        Push(); ScenePresets.Apply(preset, _project, _directory); _selectedScenePreset = preset.Id;
        SceneMenuLabel.Text = preset.Name; Commit(); transition();
    }

    void SaveScene_Click(object sender, RoutedEventArgs e) => ShowSaveScene();

    void ShowSaveScene(ScenePreset? updating = null)
    {
        var dialog = new Window { Title = "保存场景预设", Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "PanelBrush");
        var panel = new StackPanel { Margin = new Thickness(24) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "保存场景预设", FontSize = 20, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(Caption("名称"));
        var name = new TextBox { Text = updating?.Name ?? "我的场景", Margin = new Thickness(0, 0, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetName(name, "场景预设名称"); panel.Children.Add(name);
        CheckBox Choice(string title, bool selected)
        {
            var choice = new CheckBox { Content = title, IsChecked = selected, Margin = new Thickness(0, 8, 0, 8) };
            panel.Children.Add(choice); return choice;
        }
        var canvas = Choice("画布与屏幕", updating?.IncludesCanvas ?? true);
        var camera = Choice("摄像头", updating?.IncludesCamera ?? true);
        var opening = Choice("开场与运动", updating?.IncludesOpening ?? true);
        var cursor = Choice("光标与背景", updating?.IncludesCursorBackdrop ?? true);
        var useDefault = Choice("用于新录制", updating is not null && SettingsStore.Current.DefaultScenePresetId == updating.Id);
        var status = Caption(""); panel.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "取消", Style = (Style)FindResource("QuietButton"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.Close(); actions.Children.Add(cancel);
        var save = new Button { Content = "保存", Style = (Style)FindResource("PrimaryButton"), IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { status.Text = "请输入预设名称。"; name.Focus(); return; }
            try
            {
                var settings = SettingsStore.Current;
                var preset = ScenePresets.Capture(_project, _directory, name.Text,
                    canvas.IsChecked == true, camera.IsChecked == true, opening.IsChecked == true, cursor.IsChecked == true);
                if (updating is not null)
                {
                    settings.ScenePresets[settings.ScenePresets.IndexOf(updating)] = preset;
                    ScenePresets.RemoveImages(updating);
                }
                else settings.ScenePresets.Add(preset);
                if (useDefault.IsChecked == true) settings.DefaultScenePresetId = preset.Id;
                else if (updating?.Id == settings.DefaultScenePresetId) settings.DefaultScenePresetId = null;
                _selectedScenePreset = preset.Id; SceneMenuLabel.Text = preset.Name; SettingsStore.Save(); dialog.Close(); RebuildInspector();
            }
            catch (Exception ex) { status.Text = "预设未保存：" + ex.Message; }
        };
        actions.Children.Add(save); panel.Children.Add(actions);
        dialog.Loaded += (_, _) => { name.Focus(); name.SelectAll(); }; dialog.ShowDialog();
    }
}
