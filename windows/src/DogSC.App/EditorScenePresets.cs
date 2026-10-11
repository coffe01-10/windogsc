using DogSC.Core;
using System.Windows.Controls.Primitives;

namespace DogSC;

public partial class EditorWindow
{
    string? _selectedScenePreset;
    ScenePreset? _activeSceneSnapshot;
    Popup? _scenePopup;

    void SyncScenePresetLabel()
    {
        var presets = SettingsStore.Current.ScenePresets;
        var active = presets.FirstOrDefault(preset => preset.Id == _selectedScenePreset);
        var name = active?.Name ?? (presets.Count == 0 ? "保存场景" : "场景预设");
        SceneMenuLabel.Text = name.Length > 14 ? name[..13] + "…" : name;
        SceneMenuArrow.Visibility = presets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SceneModified.Visibility = _activeSceneSnapshot is not null && !_activeSceneSnapshot.MatchesConfiguration(_project, SettingsStore.Current.RememberedZoomScale) ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(SceneMenuButton, name);
        System.Windows.Automation.AutomationProperties.SetItemStatus(SceneMenuButton, SceneModified.Visibility == Visibility.Visible ? "已修改" : "场景预设");
    }

    void SceneMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_scenePopup?.IsOpen == true) { _scenePopup.IsOpen = false; return; }
        if (SettingsStore.Current.ScenePresets.Count == 0) { ShowSaveScene(); return; }
        var panel = new ScenePresetMenuPanel(() => SettingsStore.Current.ScenePresets, () => _selectedScenePreset, () => SettingsStore.Current.DefaultScenePresetId);
        var popup = new Popup { PlacementTarget = SceneMenuButton, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = false, Child = new Border { Padding = new Thickness(6), Child = panel } };
        _scenePopup = popup;
        panel.SaveRequested += preset => { popup.IsOpen = false; ShowSaveScene(preset); };
        panel.ApplyRequested += preset => { popup.IsOpen = false; try { RequestScenePresetApplication(preset); } catch (Exception error) { TimeLabel.Text = "场景未应用：" + error.Message; } };
        panel.DefaultRequested += id =>
        {
            var settings = SettingsStore.Current; var old = settings.DefaultScenePresetId;
            try { settings.DefaultScenePresetId = id; SettingsStore.SaveOrThrow(); }
            catch (Exception error) { settings.DefaultScenePresetId = old; TimeLabel.Text = "默认场景未保存：" + error.Message; }
        };
        panel.DeleteRequested += preset =>
        {
            var settings = SettingsStore.Current; var index = settings.ScenePresets.IndexOf(preset); var oldDefault = settings.DefaultScenePresetId;
            settings.ScenePresets.Remove(preset);
            if (settings.DefaultScenePresetId == preset.Id) settings.DefaultScenePresetId = null;
            try { SettingsStore.SaveOrThrow(); }
            catch (Exception error) { settings.ScenePresets.Insert(index, preset); settings.DefaultScenePresetId = oldDefault; TimeLabel.Text = "预设未删除：" + error.Message; return; }
            if (_selectedScenePreset == preset.Id) { _selectedScenePreset = null; _activeSceneSnapshot = null; }
            try { ScenePresets.RemoveImages(preset); } catch (Exception error) { AppPaths.Log("预设资源未清理：" + error.Message); }
            SyncScenePresetLabel(); panel.Refresh(); if (settings.ScenePresets.Count == 0) popup.IsOpen = false;
        };
        panel.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) { popup.IsOpen = false; key.Handled = true; } };
        popup.Opened += (_, _) => FluidMotion.Entrance(panel); popup.Closed += (_, _) => SceneMenuButton.Focus(); popup.IsOpen = true;
    }

    void RequestScenePresetApplication(ScenePreset preset)
    {
        if (!preset.NeedsCropPreview(_project)) { ApplyScenePreset(preset); return; }
        _player.Pause();
        var outputTime = _player.Time;
        var dialog = new Window { Title = "检查裁切适配", SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new ScenePresetCropPreviewPanel(preset, _project,
            token => RecordingThumbnail.LoadAtOutputTimeAsync(_project, _directory, outputTime, token));
        dialog.Content = panel; panel.CancelRequested += dialog.Close;
        panel.ApplyRequested += () =>
        {
            try { ApplyScenePreset(preset); dialog.Close(); }
            catch (Exception error) { TimeLabel.Text = "场景未应用：" + error.Message; }
        };
        dialog.Loaded += (_, _) => { WindowPlacement.KeepInWorkArea(dialog, center: true); FluidMotion.Entrance(panel); };
        ModalShade.Visibility = Visibility.Visible;
        try { dialog.ShowDialog(); } finally { ModalShade.Visibility = Visibility.Collapsed; }
    }

    void ApplyScenePreset(ScenePreset preset)
    {
        var resolved = ScenePresets.PrepareApplication(preset, _directory);
        var transition = FluidMotion.PrepareSwap(PreviewCanvas, PreviewTransitionLayer, 0);
        Push(); resolved.Apply(_project); _selectedScenePreset = preset.Id;
        _activeSceneSnapshot = ScenePreset.Capture(_project, preset.Name, zoomCreationScale: SettingsStore.Current.RememberedZoomScale); Commit(); transition();
        if (preset.IncludesCursorBackdrop && preset.IncludesBackground && !resolved.IncludesBackground)
            TimeLabel.Text = "此预设的背景未包含或已不可用，已保留当前背景。";
    }

    string NextSceneName()
    {
        var index = 1;
        while (SettingsStore.Current.ScenePresets.Any(preset => string.Equals(preset.Name, "场景 " + index, StringComparison.CurrentCultureIgnoreCase))) index++;
        return "场景 " + index;
    }

    void ShowSaveScene(ScenePreset? updating = null)
    {
        var dialog = new Window { Title = updating is null ? "保存场景预设" : "更新场景预设", SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new ScenePresetSavePanel(_project, updating?.Name ?? NextSceneName(), updating is not null,
            name => SettingsStore.Current.ScenePresets.Any(preset => preset.Id != updating?.Id && string.Equals(preset.Name, name, StringComparison.CurrentCultureIgnoreCase)),
            async name =>
            {
                var snapshot = ScenePreset.Clone(_project);
                ScenePreset? captured = null;
                var settings = SettingsStore.Current; var index = updating is null ? -1 : settings.ScenePresets.IndexOf(updating);
                try
                {
                    captured = await Task.Run(() => ScenePresets.Capture(snapshot, _directory, name, id: updating?.Id));
                    if (index >= 0) settings.ScenePresets[index] = captured; else settings.ScenePresets.Add(captured);
                    try { SettingsStore.SaveOrThrow(); }
                    catch { if (index >= 0) settings.ScenePresets[index] = updating!; else settings.ScenePresets.Remove(captured); throw; }
                    _selectedScenePreset = captured.Id; _activeSceneSnapshot = ScenePreset.Capture(snapshot, captured.Name, zoomCreationScale: SettingsStore.Current.RememberedZoomScale);
                    if (updating is not null)
                        try { ScenePresets.RemoveImages(updating, retaining: captured); } catch (Exception error) { AppPaths.Log("旧预设背景未清理：" + error.Message); }
                    SyncScenePresetLabel(); return null;
                }
                catch (Exception error)
                {
                    if (captured is not null) ScenePresets.RemoveImages(captured, retaining: updating);
                    return "预设未保存：" + error.Message;
                }
            });
        dialog.Content = panel; panel.CancelRequested += dialog.Close; panel.Saved += dialog.Close;
        dialog.Closing += (_, e) => { if (panel.IsSaving) e.Cancel = true; };
        dialog.Loaded += (_, _) => { WindowPlacement.KeepInWorkArea(dialog, center: true); FluidMotion.Entrance(panel); };
        ModalShade.Visibility = Visibility.Visible;
        try { dialog.ShowDialog(); } finally { ModalShade.Visibility = Visibility.Collapsed; }
    }
}
