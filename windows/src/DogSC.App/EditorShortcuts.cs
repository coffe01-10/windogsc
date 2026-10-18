using DogSC.Core;
using System.Windows.Controls.Primitives;

namespace DogSC;

public partial class EditorWindow
{
    bool _inspectorHidden;
    GridLength _visibleInspectorWidth;

    void EditorShortcutFocus_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not (TextBoxBase or PasswordBox)) return;
        var target = e.OriginalSource as DependencyObject;
        while (target is not null)
        {
            if (target is TextBoxBase or PasswordBox) return;
            target = target is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
        }
        // Clicking back into the workspace must release text editing, otherwise
        // S/Q/W and Space remain trapped in the project-title field.
        Keyboard.Focus(Timeline);
    }

    bool HandleEditorShortcut(Key key, ModifierKeys modifiers, bool repeat = false)
    {
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.I)
        { if (!repeat) ToggleInspector(); return true; }
        if (modifiers == ModifierKeys.Control)
        {
            if (repeat && key is Key.E or Key.O) return true;
            switch (key)
            {
                case Key.C: CopyEffect_Click(this, new()); return true;
                case Key.V: PasteEffect_Click(this, new()); return true;
                case Key.Z: Undo_Click(this, new()); return true;
                case Key.Y: Redo_Click(this, new()); return true;
                case Key.E: Export_Click(this, new()); return true;
                case Key.O: OpenProjectShortcut(); return true;
            }
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (key == Key.V) { PasteAttributes_Click(this, new()); return true; }
            if (key == Key.Z) { Redo_Click(this, new()); return true; }
        }
        if (modifiers == ModifierKeys.Alt && key == Key.X)
        { if (!repeat) ExportSelectedRange(); return true; }
        if (modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return false;
        if (key is Key.Left or Key.Right)
        {
            var frames = modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
            _player.Seek(Math.Clamp(_player.Time + (key == Key.Left ? -frames : frames) / (double)Math.Max(1, _project.FrameRate),
                0, TimelineMath.Duration(_project)), play: false);
            return true;
        }
        if (repeat && key is Key.Space or Key.S or Key.Q or Key.W or Key.D or Key.Delete or Key.Back or Key.M) return true;
        switch (key)
        {
            case Key.Space: Play_Click(this, new()); return true;
            case Key.S: Split_Click(this, new()); return true;
            case Key.Q: TrimAtPlayhead(true); return true;
            case Key.W: TrimAtPlayhead(false); return true;
            case Key.D: case Key.Delete: case Key.Back: Delete_Click(this, new()); return true;
            case Key.M: Marker_Click(this, new()); return true;
            case Key.F1: ShowShortcutReference(); return true;
        }
        return false;
    }

    void TrimAtPlayhead(bool before)
    {
        _player.Pause();
        var time = _player.Time;
        var previous = System.Text.Json.JsonSerializer.Serialize(_project, ProjectSchema.Json);
        var effect = _selectedDecoration ?? _selectedZoom;
        bool changed;
        if (effect is not null) changed = TimelineShortcutEdits.TrimEffect(_project, effect, time, before);
        else
        {
            var placed = TimelineMath.Place(_project).FirstOrDefault(x => x.Clip.Id == _selectedClip)
                ?? TimelineMath.Place(_project).FirstOrDefault(x => time > x.Start && time < x.End);
            if (placed is null || time <= placed.Start || time >= placed.End) return;
            var clip = placed.Clip;
            var source = clip.SourceStart + (time - placed.Start) * clip.Speed;
            changed = TimelineEdits.TryTrim(_project, clip.Id, before ? source : clip.SourceStart, before ? clip.SourceEnd : source);
            if (changed && before) time = placed.Start;
        }
        if (!changed) return;
        Push(previous); Commit(); _player.Seek(Math.Min(time, TimelineMath.Duration(_project)), play: false);
    }

    void OpenProjectShortcut()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.Text("打开 DogSC 项目") };
        if (dialog.ShowDialog(this) == true) Open(dialog.FolderName);
    }

    void ToggleInspector()
    {
        _activeParameterReadout?.FinishEditing();
        _inspectorHidden = !_inspectorHidden;
        if (_inspectorHidden) _visibleInspectorWidth = InspectorColumn.Width;
        InspectorColumn.MinWidth = _inspectorHidden ? 0 : 292;
        InspectorColumn.Width = _inspectorHidden ? new GridLength(0) : _visibleInspectorWidth;
        InspectorPane.Visibility = InspectorDivider.Visibility = _inspectorHidden ? Visibility.Collapsed : Visibility.Visible;
        LayoutPreview();
    }

    void ExportSelectedRange()
    {
        var placed = TimelineMath.Place(_project).FirstOrDefault(x => x.Clip.Id == _selectedClip);
        if (placed is null) { TimeLabel.Text = L.Text("请先选中一个主片段。"); return; }
        ShowExport(placed.Start, placed.Duration);
    }

    void ShowShortcutReference()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        foreach (var (keys, title) in new[] {
            ("Space", "播放 / 暂停"), ("← / →", "移动一帧"), ("Shift + ← / →", "移动五帧"),
            ("S", "分割选中动画或主片段"), ("Q / W", "修剪起点 / 终点"), ("D / Delete / Backspace", "删除当前选中项"),
            ("Ctrl + C / V", "复制 / 粘贴效果"), ("Ctrl + Shift + V", "只粘属性"),
            ("Ctrl + Z / Ctrl + Shift + Z", "撤销 / 重做"), ("Ctrl + O", "打开项目"),
            ("Ctrl + E", "导出成片"), ("Alt + X", "导出选中的主片段范围"), ("Ctrl + Alt + I", "显示 / 隐藏检查器"),
            ("Esc", "取消裁切 / 中止时间线拖动"), ("M", "添加标记"), ("F1", "快捷键速查") })
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(240) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = keys, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = L.Text(title), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(label, 1); row.Children.Add(label); panel.Children.Add(row);
        }
        var window = new Window { Owner = this, Title = L.Text("快捷键速查"), Width = 610, Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = panel },
            Background = (System.Windows.Media.Brush)FindResource("CardBrush"), Foreground = (System.Windows.Media.Brush)FindResource("InkBrush") };
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };
        window.ShowDialog();
    }

    void ShortcutReference_Click(object sender, RoutedEventArgs e) => ShowShortcutReference();
}
