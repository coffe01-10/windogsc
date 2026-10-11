using DogSC;
using DogSC.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// An invisible HWND supplies WPF layout and animation clocks without opening
// the recorder/editor or reading and changing the user's settings/project.
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DogSC;component/Themes/Light.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DogSC;component/Themes/Controls.xaml", UriKind.Relative) });
        using var source = new HwndSource(new HwndSourceParameters("DogSC UI checks") { Width = 900, Height = 700, WindowStyle = 0 });
        var host = new Grid { Width = 900, Height = 700 }; source.RootVisual = host;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var reportExport = args.Length == 2 && args[0] == "--report-export";
        var task = reportExport ? ReportExportCheck.RunAsync(args[1]) : CheckAsync(host, args.Length > 0 ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])) : null);
        task.ContinueWith(_ => app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background));
        Dispatcher.Run();
        try { task.GetAwaiter().GetResult(); if (!reportExport && args.Length > 0) RenderSymbols(args[0]); return 0; }
        catch (Exception error) { Console.Error.WriteLine("UI_FAIL " + error); return 1; }
    }

    static Button[] Options() => [new Button { Content = "背景" }, new Button { Content = "布局" }, new Button { Content = "外框" }];
    static Border Indicator(FluidSegments segments) => (Border)segments.Children[0];
    static double Position(FluidSegments segments) => ((TranslateTransform)Indicator(segments).RenderTransform).X;
    static void Require(bool condition, string issue) { if (!condition) throw new InvalidOperationException(issue); }
    static async Task CheckAsync(Grid host, string? reviewDirectory)
    {
        // Switching copy must update already-mounted controls and leave authored
        // text alone; a settings-only translation would miss both regressions.
        var languageBefore = SettingsStore.Current.Language;
        var localizedLabel = new TextBlock(); L.Bind(localizedLabel, TextBlock.TextProperty, "背景来源");
        var authoredText = new TextBox { Text = "我的项目 Canvas / 背景来源" };
        host.Children.Add(localizedLabel); host.Children.Add(authoredText);
        try
        {
            SettingsStore.Current.Language = "en"; LanguageCatalog.Current.Refresh(); await Task.Delay(20);
            Require(localizedLabel.Text == "Background source", "Mounted labels do not switch to English.");
            Require(L.Format("{0} 个参与元素", 3) == "3 elements included", "English dynamic labels keep Chinese word order.");
            Require(authoredText.Text == "我的项目 Canvas / 背景来源", "Switching language changed authored text.");
            SettingsStore.Current.Language = "zh-Hans"; LanguageCatalog.Current.Refresh(); await Task.Delay(20);
            Require(localizedLabel.Text == "背景来源", "Mounted labels cannot switch back to Chinese.");
            Console.WriteLine("UI_OK language: live labels, formatted copy and authored text preserved");
        }
        finally { SettingsStore.Current.Language = languageBefore; LanguageCatalog.Current.Refresh(); host.Children.Clear(); }
        await CheckSettingsAsync(host, reviewDirectory);
        foreach (var key in new[] { "FontRegular", "FontMedium", "FontSemi" })
        {
            var weight = key == "FontSemi" ? FontWeights.SemiBold : key == "FontMedium" ? FontWeights.Medium : FontWeights.Normal;
            var face = new Typeface((FontFamily)Application.Current.FindResource(key), FontStyles.Normal, weight, FontStretches.Normal);
            Require(face.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.ToString().Contains("AlibabaPuHuiTi", StringComparison.OrdinalIgnoreCase), "Bundled font fell back to a system face: " + key);
            Require(glyph.Weight == weight, "Bundled font resolved the wrong weight: " + key);
            Console.WriteLine($"UI font {key}: {glyph.Weight}");
        }
        Console.WriteLine("UI animation enabled: " + FluidMotion.Enabled);
        var segments = new FluidSegments(Options(), 0) { Width = 300, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var mount = new Border { Child = segments }; host.Children.Add(mount);
        await Task.Delay(80); host.UpdateLayout();
        Require(segments.IsLoaded, "Selector did not load on the WPF presentation source.");
        segments.Select(2);
        if (FluidMotion.Enabled) Require(Math.Abs(Position(segments)) < .5, "The inactive animation clock exposed the destination on its first frame.");
        await Task.Delay(45);
        var intermediate = Position(segments);
        if (FluidMotion.Enabled)
            Require(intermediate > 0 && intermediate < 199, "Selection snapped to the endpoint instead of sliding.");

        // Rebuild the inspector while the pill is travelling: the next animation
        // must continue from its current position, not restart from the last tab.
        var before = Position(segments);
        var interruptionClock = System.Diagnostics.Stopwatch.StartNew();
        mount.Child = null; segments.Refresh(Options(), 1); mount.Child = segments;
        host.UpdateLayout(); await Task.Delay(1);
        var after = Position(segments);
        Console.WriteLine($"Selector rebuild elapsed {interruptionClock.Elapsed.TotalMilliseconds:0.0}ms, position {before:0.0}->{after:0.0}");
        if (FluidMotion.Enabled) Require(after > 0 && after < 199 && Math.Abs(after - 100) > .5,
            $"Rebuilding snapped the selector onto a tab: {before:0.0} -> {after:0.0}.");
        await Task.Delay(520);
        Require(Math.Abs(Position(segments) - 100) < .5, "Interrupted selector did not settle on the requested tab.");
        Console.WriteLine($"UI_OK selector slides and survives rebuild: mid={intermediate:0.0}, interruption={before:0.0}->{after:0.0}, end={Position(segments):0.0}");
        segments.Select(2); await Task.Delay(30); segments.Height = 48; host.UpdateLayout(); await Task.Delay(1);
        if (FluidMotion.Enabled) Require(Position(segments) < 199, "A height-only layout change snapped the travelling selector to its endpoint.");
        await Task.Delay(520); segments.Select(1); await Task.Delay(520);
        Console.WriteLine("UI_OK layout changes keep selector animation running");
        var pressed = segments.Buttons.ElementAt(1);
        pressed.RenderTransformOrigin = new Point(.5, .5); pressed.RenderTransform = new ScaleTransform(.98, .98);
        FluidMotion.Select(Indicator(segments), pressed, segments, false);
        Require(Math.Abs(Position(segments) - 100) < .01, "Button press feedback shifted the pill away from its layout position.");
        Console.WriteLine("UI_OK pressed button keeps selector aligned");

        var page = new Border { Width = 200, Height = 100, Margin = new Thickness(28, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Red };
        var layer = new Grid { Width = 200, Height = 100, Margin = page.Margin, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Clear(); host.Children.Add(page); host.Children.Add(layer); host.UpdateLayout(); await Task.Delay(40);
        var finish = FluidMotion.PrepareSwap(page, layer);
        if (FluidMotion.Enabled)
        {
            var pixels = new byte[4]; ((BitmapSource)((Image)layer.Children[0]).Source).CopyPixels(new Int32Rect(1, 1, 1, 1), pixels, 4, 0);
            Require(pixels[2] > 200 && pixels[3] > 200, "The outgoing page snapshot repeats its outer margin.");
        }
        page.Background = Brushes.Blue; finish(); await Task.Delay(80);
        if (FluidMotion.Enabled)
        {
            var incoming = ((TranslateTransform)page.RenderTransform).Y;
            var outgoing = ((TranslateTransform)layer.Children[0].RenderTransform).Y;
            Require(incoming > 0 && incoming < 10 && outgoing < 0, "Page contents did not slide in opposite directions.");
            Console.WriteLine($"UI_OK page motion: incoming={incoming:0.00}, outgoing={outgoing:0.00}");
        }
        await Task.Delay(520); Require(layer.Children.Count == 0 && page.Opacity == 1, "Page transition left a ghost layer.");
        Console.WriteLine("UI_OK snapshot local coordinates and transition cleanup");

        var moving = FluidMotion.PrepareSwap(page, layer); page.Background = Brushes.Red;
        moving(); await Task.Delay(65);
        var opacityBeforeInterrupt = page.Opacity;
        var interrupt = FluidMotion.PrepareSwap(page, layer);
        if (FluidMotion.Enabled)
        {
            var pixels = new byte[4]; ((BitmapSource)((Image)layer.Children[0]).Source).CopyPixels(new Int32Rect(100, 50, 1, 1), pixels, 4, 0);
            Console.WriteLine($"Page interrupted opacity={opacityBeforeInterrupt:0.00}, snapshot RGB={pixels[2]},{pixels[1]},{pixels[0]}");
            Require(pixels[0] > 5 && pixels[2] > 5, "Interrupting a page swap discarded the visible outgoing page and snapped to the incoming colour.");
        }
        page.Background = Brushes.Green; interrupt(); await Task.Delay(520);

        // A rapid second page change must supersede the first queued callback.
        var first = FluidMotion.PrepareSwap(page, layer); page.Background = Brushes.Green;
        var second = FluidMotion.PrepareSwap(page, layer); page.Background = Brushes.Purple;
        second(); first(); await Task.Delay(520);
        Require(layer.Children.Count == 0 && page.Opacity == 1, "Rapid page changes left an obsolete transition.");
        Console.WriteLine("UI_OK rapid page change cancels obsolete callbacks");

        var disclosure = new Expander { Header = "展开", Content = new Border { Height = 100 }, Width = 250, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Clear(); host.Children.Add(disclosure); host.UpdateLayout(); await Task.Delay(40);
        disclosure.IsExpanded = true; host.UpdateLayout();
        var body = (Border)disclosure.Template.FindName("DisclosureBody", disclosure);
        Console.WriteLine($"Disclosure first layout: {body.ActualHeight:0.00}");
        if (FluidMotion.Enabled) Require(body.ActualHeight < 100, "Disclosure exposed full height before its animation clock started.");
        await Task.Delay(60);
        if (FluidMotion.Enabled) Require(body.ActualHeight > 0 && body.ActualHeight < 100, "Disclosure jumped to full height.");
        disclosure.IsExpanded = false; await Task.Delay(500);
        Require(body.ActualHeight == 0, "Interrupted disclosure failed to collapse.");
        disclosure.IsExpanded = true; await Task.Delay(500);
        Require(double.IsNaN(body.Height) && body.ActualHeight > 100, "Settled disclosure did not restore automatic content height.");
        ((Border)disclosure.Content).Height = 140; host.UpdateLayout();
        Require(body.ActualHeight > 140, "Expanded disclosure kept a stale height after its content grew.");
        disclosure.IsExpanded = false; await Task.Delay(500);
        Require(body.ActualHeight == 0, "Settled disclosure failed to collapse.");
        Console.WriteLine("UI_OK disclosure animates and can reverse mid-flight");
        await CheckExportAsync(host, reviewDirectory);
        await CheckActionMenuAsync(host, reviewDirectory);
        await CheckRatioAsync(host, reviewDirectory);
        await CheckScenePanelsAsync(host, reviewDirectory);
        CheckNewRecordingAppearance();
        await CheckParameterReadoutAsync(host, reviewDirectory);
        if (reviewDirectory is not null) await RenderPadReviewAsync(host, reviewDirectory);
    }

    static async Task RenderPadReviewAsync(Grid host, string directory)
    {
        foreach (var dark in new[] { false, true })
        {
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri(
                dark ? "/DogSC;component/Themes/Dark.xaml" : "/DogSC;component/Themes/Light.xaml", UriKind.Relative) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var compact = new EditorPadSurface(compact: true); compact.SetPoint(.5, .5);
            var position = new EditorPadSurface { CoordinateEditing = true }; position.SetPoint(.84, 1d / 3);
            var tilt = new EditorPadSurface(tilt: true) { CoordinateEditing = true }; tilt.SetPoint(.5 + 20d / 64, .5 - 16.5 / 56);
            foreach (var (pad, title) in new[] { (compact, "屏幕位置"), (position, "位置 · 输入中"), (tilt, "3D 倾斜 · 输入中") })
            {
                var column = new StackPanel { Margin = new Thickness(18) };
                var caption = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 9), FontSize = 12 };
                caption.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush"); column.Children.Add(caption); column.Children.Add(pad); row.Children.Add(column);
            }
            var card = new Border { Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            host.Children.Clear(); host.Children.Add(card); host.UpdateLayout(); await Task.Delay(50);
            SaveVisual(card, System.IO.Path.Combine(directory, dark ? "windows-position-pads-dark.png" : "windows-position-pads-light.png"));
        }
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("/DogSC;component/Themes/Light.xaml", UriKind.Relative) };
    }

    static async Task CheckParameterReadoutAsync(Grid host, string? reviewDirectory)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var readout = new ParameterReadout("屏幕位置 X 坐标", 0, 1, .5, v => $"{v * 100:0}%");
        var baseline = .5; var starts = 0; var ends = 0; var previews = 0; bool? committed = null;
        readout.EditingStarted += () => { baseline = readout.Value; starts++; };
        readout.ValuePreviewed += _ => previews++;
        readout.EditingFinished += success => { committed = success; ends++; if (!success) readout.Value = baseline; };
        row.Children.Add(readout); row.Children.Add(new Button { Content = "下一项", Margin = new Thickness(20, 0, 0, 0) });
        host.Children.Clear(); host.Children.Add(row); host.UpdateLayout(); await Task.Delay(40);
        var width = readout.ActualWidth; var height = readout.ActualHeight;
        readout.BeginEditing(); await Task.Delay(40); readout.DraftText = "84%"; host.UpdateLayout();
        Require(readout.IsEditing && readout.Value == .84 && previews == 1, "Exact entry did not preview the percent value.");
        Require(Math.Abs(readout.ActualWidth - width) < 1 && height == 30, "The native input moved or resized the value capsule.");
        Require(readout.FinishEditing() && committed == true && ends == 1, "Exact entry did not finish as one interaction.");
        readout.BeginEditing(); await Task.Delay(20); readout.DraftText = "25"; readout.DraftText = "-";
        Require(readout.Value == .25 && !readout.FinishEditing() && committed == false && readout.Value == .84, "An invalid draft was committed instead of restoring the entry's initial value.");
        readout.BeginEditing(); await Task.Delay(20); readout.DraftText = "20"; readout.CancelEditing();
        Require(readout.Value == .84 && committed == false, "Cancelled exact entry kept its preview.");
        readout.BeginEditing(); await Task.Delay(20); readout.DraftText = "200%"; readout.FinishEditing();
        Require(readout.Value == 1 && starts == 4 && ends == 4, "Out-of-range entry differs from its slider or leaves a pending interaction.");
        readout.Value = .5; host.UpdateLayout();
        if (reviewDirectory is not null) SaveVisual(row, System.IO.Path.Combine(reviewDirectory, "windows-parameter-readout.png"));
        Console.WriteLine("UI_OK exact parameter entry: fixed capsule, live percent preview, one finish, invalid / cancelled rollback and slider-range clamping");
    }

    static void CheckNewRecordingAppearance()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dogsc-scene-inheritance-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            var previous = new ProjectDocument(); previous.Canvas.Padding = 108; previous.Canvas.Crop.Width = .6;
            previous.Camera.Hidden = true; previous.Camera.Shape = "square"; previous.Motion.DefaultZoomScale = 3.6;
            var settings = new AppSettings { LastUsedScene = ScenePreset.CaptureLastUsed(previous) };
            var target = new ProjectDocument(); ScenePresets.ApplyDefault(target, folder, settings);
            Require(target.Canvas.Padding == 108 && target.Motion.DefaultZoomScale == 3.6 && target.Canvas.Crop.Width == 1 && !target.Camera.Hidden,
                "New recording does not inherit last-used style while keeping source crop and camera visibility.");
            var selected = ScenePreset.Capture(previous, "明确默认"); selected.Canvas.Padding = 72;
            settings.ScenePresets.Add(selected); settings.DefaultScenePresetId = selected.Id;
            target = new ProjectDocument(); ScenePresets.ApplyDefault(target, folder, settings);
            Require(target.Canvas.Padding == 72 && target.Canvas.Crop.Width == .6 && target.Camera.Hidden, "Explicit default lost crop / visibility or did not take precedence over last-used style.");

            var sourceFile = System.IO.Path.Combine(folder, "system-background.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 80, 120, 160, 255 }, 4)));
            using (var file = System.IO.File.Create(sourceFile)) encoder.Save(file);
            previous.Canvas.Wallpaper = sourceFile;
            settings.DefaultScenePresetId = null; settings.LastUsedScene = ScenePreset.CaptureLastUsed(previous);
            target = new ProjectDocument(); ScenePresets.ApplyDefault(target, folder, settings);
            Require(target.Canvas.Wallpaper is not null && !System.IO.Path.IsPathRooted(target.Canvas.Wallpaper)
                && System.IO.File.Exists(ProjectFile.Media(folder, target.Canvas.Wallpaper)), "Standalone last-used background was not copied into the new project's media.");
            System.IO.File.Delete(sourceFile);
            target = new ProjectDocument(); target.Canvas.Background = "#336699"; target.Canvas.Wallpaper = "pattern:obsidian";
            ScenePresets.ApplyDefault(target, folder, settings);
            Require(target.Canvas.Padding == 108 && target.Canvas.Background == "#336699" && target.Canvas.Wallpaper == "pattern:obsidian", "A removed last-used background loses the whole style or replaces the new project's background with a broken path.");
            selected.Canvas.Wallpaper = sourceFile; settings.DefaultScenePresetId = selected.Id;
            target = new ProjectDocument(); target.Canvas.Wallpaper = "flow:aurora";
            ScenePresets.ApplyDefault(target, folder, settings);
            Require(target.Canvas.Padding == 72 && target.Canvas.Wallpaper == "flow:aurora", "An unavailable default background prevents applying the rest of the scene.");
            Console.WriteLine("UI_OK new recording appearance: last-used / explicit default precedence, source-safe crop and visibility, owned background copy and missing resource fallback");
        }
        finally { System.IO.Directory.Delete(folder, recursive: true); }
    }

    static async Task CheckExportAsync(Grid host, string? reviewDirectory)
    {
        var project = new ProjectDocument { Name = "区域 · 界面布局核对", Width = 960, Height = 720 };
        var asset = new MediaAsset { Duration = 8.75, Width = 960, Height = 720 };
        project.Assets.Add(asset); project.Clips.Add(new ClipSegment { AssetId = asset.Id, SourceEnd = asset.Duration });
        var preview = new DrawingImage(new GeometryDrawing(Brushes.LightSteelBlue, null, new RectangleGeometry(new Rect(0, 0, 960, 720))));
        var window = new ExportWindow(project, "", preview);
        var view = (FrameworkElement)window.Content; window.Content = null;
        view.Width = window.Width; view.Height = window.Height;
        view.HorizontalAlignment = HorizontalAlignment.Left; view.VerticalAlignment = VerticalAlignment.Top;
        host.Children.Clear(); host.Children.Add(view); host.UpdateLayout(); await Task.Delay(80);
        var choices = (FluidSegments)((System.Windows.Controls.Primitives.UniformGrid)window.FindName("OutputChoicesRow")).Children[0];
        var wells = (System.Windows.Controls.Primitives.UniformGrid)choices.Children[0];
        Require(wells.Children.Count == 2 && ((Border)wells.Children[1]).Background is not null, "Unselected export tiles lost their independent surface.");
        var buttons = choices.Buttons.ToArray();
        var gap = buttons[1].TranslatePoint(new Point(), choices).X - buttons[0].TranslatePoint(new Point(buttons[0].ActualWidth, 0), choices).X;
        if (reviewDirectory is not null) SaveVisual(view, System.IO.Path.Combine(reviewDirectory, "windows-export-light.png"));
        var pixel = 1 / VisualTreeHelper.GetDpi(view).DpiScaleX;
        Require(Math.Abs(gap - 10) <= pixel && Math.Abs(buttons[0].ActualHeight - 38) <= pixel, $"Export tile dimensions differ from the original: height={buttons[0].ActualHeight:0.00}, gap={gap:0.00}.");

        // Exercise the actual dialog callback without starting an export or opening it.
        ((Button)window.FindName("AudioChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.Height = window.Height; host.UpdateLayout(); await Task.Delay(520);
        Require(((TextBlock)window.FindName("ExtensionLabel")).Text == ".m4a" && ((StackPanel)window.FindName("VideoMetadata")).Visibility == Visibility.Collapsed,
            "Selecting audio did not switch the extension and metadata.");
        Require(window.Height == 531 && ((TextBlock)window.FindName("AudioDescription")).Visibility == Visibility.Visible, "Audio export did not compact the dialog / reveal its mixing description.");
        Require(((ScrollViewer)window.FindName("ExportOptionsScroll")).ScrollableHeight == 0, "Compact audio options unexpectedly require scrolling.");
        if (reviewDirectory is not null) SaveVisual(view, System.IO.Path.Combine(reviewDirectory, "windows-export-audio.png"));
        ((Button)window.FindName("VideoChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.Height = window.Height; host.UpdateLayout(); await Task.Delay(520);
        Require(window.Height == 635 && ((TextBlock)window.FindName("ExtensionLabel")).Text == ".mp4", "Returning to video did not restore the dialog.");
        var fileName = (TextBox)window.FindName("FileName");
        var nameRow = (Border)((DockPanel)((Grid)fileName.Parent).Parent).Parent;
        nameRow.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        var initialName = fileName.Text; fileName.Text = "取消后不应保留";
        fileName.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(view)!, Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
        Require(fileName.Text == initialName && fileName.Visibility == Visibility.Collapsed, "Escape failed to cancel file-name editing.");
        Console.WriteLine("UI_OK export tiles: 38 high / 10 gap; audio/video callback and file-name cancellation");
        host.Children.Clear(); window.Close();
    }

    static async Task CheckActionMenuAsync(Grid host, string? reviewDirectory)
    {
        var menu = EditorActionMenus.Create("预览选项");
        var full = EditorActionMenus.Add(menu, "完整分辨率", () => { }, detail: "保留素材细节", key: "full");
        EditorActionMenus.Add(menu, "清晰预览", () => { }, detail: "播放更流畅", key: "low"); full.IsChecked = true;
        menu.HorizontalAlignment = HorizontalAlignment.Left; menu.VerticalAlignment = VerticalAlignment.Top;
        var source = (HwndSource)PresentationSource.FromVisual(host)!;
        host.Children.Clear(); source.RootVisual = menu;
        menu.Measure(new Size(360, 700)); menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout(); await Task.Delay(40);
        var content = (Grid)full.Header; var mark = (DogSC.Icon)content.Children[^1];
        Require(mark.Opacity == 1 && Grid.GetColumn(mark) == 2 && full.ActualHeight >= 52, "Preview selection mark / detail rows did not match the original card layout.");
        if (reviewDirectory is not null) SaveVisual(menu, System.IO.Path.Combine(reviewDirectory, "windows-preview-options.png"));
        full.IsChecked = false; Require(mark.Opacity == 0, "Preview menu retains a stale checkmark.");
        Console.WriteLine("UI_OK preview menu: trailing mark and 52-point detail rows"); source.RootVisual = host;
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    static async Task CheckRatioAsync(Grid host, string? reviewDirectory)
    {
        var state = new CanvasSettings { Aspect = "9:16" }; var applied = 0;
        CanvasRatioPanel? panel = null;
        panel = new CanvasRatioPanel(() => state, value => { state = value; applied++; panel!.RefreshFromCanvas(); }) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Clear(); host.Children.Add(panel); host.UpdateLayout(); await Task.Delay(80);
        var buttons = Descendants<Button>(panel).Where(button => button.Tag is string).ToArray();
        Button Find(string tag) => buttons.Single(button => Equals(button.Tag, tag));
        void Click(string tag) => Find(tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (reviewDirectory is not null) SaveVisual(panel, System.IO.Path.Combine(reviewDirectory, "windows-ratio-presets.png"));
        Click("4:3"); await Task.Delay(40);
        Require(state.Aspect == "3:4", "Choosing a preset lost the portrait orientation.");
        Click("landscape"); await Task.Delay(520);
        Require(state.Aspect == "4:3", "Landscape orientation did not map to the selected preset.");

        var beforeDraft = applied; var startHeight = panel.ActualHeight;
        Click("custom"); host.UpdateLayout(); await Task.Delay(45);
        panel.RefreshFromCanvas(); host.UpdateLayout();
        var intermediateHeight = panel.ActualHeight;
        Require(applied == beforeDraft && state.Aspect == "4:3", "Opening custom input changed the authored canvas before Apply.");
        await Task.Delay(480); var customHeight = panel.ActualHeight;
        if (FluidMotion.Enabled) Require(intermediateHeight > startHeight && intermediateHeight < customHeight,
            $"Ratio detail group jumped instead of growing smoothly: {startHeight:0.0}->{intermediateHeight:0.0}->{customHeight:0.0}.");
        var width = Descendants<TextBox>(panel).Single(field => System.Windows.Automation.AutomationProperties.GetName(field) == "自定义比例宽");
        var height = Descendants<TextBox>(panel).Single(field => System.Windows.Automation.AutomationProperties.GetName(field) == "自定义比例高");
        width.Text = "3"; height.Text = "2"; Click("applyCustom");
        Require(state.Aspect == "3:2" && state.AspectIsCustom, "Custom Apply did not author the requested ratio / identity.");
        var size = CanvasLayout.OutputSize(960, 720, state.Aspect, "source");
        Require(size == (960, 640), "Custom ratio did not reach canvas dimensions used by preview/export.");
        host.UpdateLayout(); await Task.Delay(40);
        if (reviewDirectory is not null) SaveVisual(panel, System.IO.Path.Combine(reviewDirectory, "windows-ratio-custom.png"));
        Click("16:9"); await Task.Delay(520); Click("custom"); await Task.Delay(520);
        Require(width.Text == "3" && height.Text == "2", "Switching presets erased the custom draft.");
        panel.Prepare();
        Require(width.Text == "3" && height.Text == "2", "Reopening the panel erased the saved custom ratio.");
        width.Text = "100"; height.Text = "1";
        Require(!Find("applyCustom").IsEnabled, "Unsupported 100:1 ratio remained applicable.");
        width.Text = "16"; height.Text = "9"; Click("custom"); Click("applyCustom");
        Require(state.AspectIsCustom && state.Aspect == "16:9", "Custom input matching a preset lost its distinct custom mode.");
        var clone = ScenePreset.Clone(state);
        Require(clone.AspectIsCustom && clone.CustomAspectWidth == 16 && clone.CustomAspectHeight == 9, "Saved custom state did not survive project serialization / undo snapshots.");
        if (reviewDirectory is not null)
        {
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("/DogSC;component/Themes/Dark.xaml", UriKind.Relative) };
            // Invisible roots are outside Application.Windows' theme invalidation;
            // construct the dark surface directly, as a newly opened popover does.
            var darkPanel = new CanvasRatioPanel(() => state, value => state = value) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            host.Children.Clear(); host.Children.Add(darkPanel); host.UpdateLayout(); await Task.Delay(80);
            SaveVisual(darkPanel, System.IO.Path.Combine(reviewDirectory, "windows-ratio-dark.png"));
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("/DogSC;component/Themes/Light.xaml", UriKind.Relative) };
            host.Children.Clear(); host.Children.Add(panel); host.UpdateLayout();
        }
        state = new CanvasSettings { Aspect = "1:1" }; panel.RefreshFromCanvas(); await Task.Delay(520);
        Require(!CanvasRatioPanel.IsCustom(state), "Restoring a preset retained stale custom mode.");
        Console.WriteLine($"UI_OK ratio: orientation, custom draft/apply and saved mode; detail growth {startHeight:0.0}->{intermediateHeight:0.0}->{customHeight:0.0}");
        host.Children.Clear();
    }

    static async Task CheckScenePanelsAsync(Grid host, string? reviewDirectory)
    {
        var project = new ProjectDocument { Width = 960, Height = 720 };
        project.Canvas.Crop.Height = 625d / 720;
        var first = ScenePreset.Capture(project, "场景 1");
        var list = new List<ScenePreset> { first, ScenePreset.Capture(project, "人像演示") };
        string? active = first.Id, defaultId = null; ScenePreset? applied = null, updated = null;
        var menu = new ScenePresetMenuPanel(() => list, () => active, () => defaultId) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        menu.DefaultRequested += id => defaultId = id; menu.ApplyRequested += preset => applied = preset; menu.SaveRequested += preset => updated = preset;
        host.Children.Clear(); host.Children.Add(menu); host.UpdateLayout(); await Task.Delay(80);
        Button Action(string tag) => Descendants<Button>(menu).Single(button => Equals(button.Tag, tag));
        Action("default:" + first.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); host.UpdateLayout();
        Require(defaultId == first.Id, "Scene row did not set the default for new recordings.");
        if (reviewDirectory is not null) SaveVisual(menu, System.IO.Path.Combine(reviewDirectory, "windows-scene-menu.png"));
        Action("default:" + first.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); host.UpdateLayout(); Require(defaultId is null, "Scene default did not toggle off.");
        Action("update:" + first.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(updated == first, "Scene update targets the wrong preset.");
        Action("apply:" + list[1].Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(applied == list[1], "Scene row applies the wrong preset.");

        var completion = new TaskCompletionSource<string?>(); var saves = 0; var saved = false;
        var sheet = new ScenePresetSavePanel(project, "场景 2", false, name => name.Equals("场景 1", StringComparison.OrdinalIgnoreCase), _ => { saves++; return completion.Task; }) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        sheet.Saved += () => { Require(!sheet.IsSaving, "Successful saving still prevents the modal from closing."); saved = true; };
        host.Children.Clear(); host.Children.Add(sheet); host.UpdateLayout(); await Task.Delay(80);
        Require(!Descendants<CheckBox>(sheet).Any(), "Save scene sheet still presents incorrect group checkboxes.");
        Require(Descendants<TextBlock>(sheet).Any(text => text.Text.Contains("下 95")), "Scene crop summary is not calculated in source pixels.");
        if (reviewDirectory is not null) SaveVisual(sheet, System.IO.Path.Combine(reviewDirectory, "windows-scene-save.png"));
        var name = Descendants<TextBox>(sheet).Single(); var save = Descendants<Button>(sheet).Single(button => Equals(button.Tag, "save"));
        name.Text = "场景 1"; Require(!save.IsEnabled, "Conflicting preset names can still overwrite / duplicate a saved scene.");
        name.Text = "场景 2"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(sheet.IsSaving && saves == 1 && !name.IsEnabled, "Scene save does not show an in-progress state or allows duplicate submissions.");
        completion.SetResult(null); await Task.Delay(30); Require(saved && !sheet.IsSaving, "Successful saving did not finish the sheet.");
        Console.WriteLine("UI_OK scene menu: default, update and apply; save summary, crop, naming and asynchronous completion"); host.Children.Clear();
        await CheckSceneCropPreviewAsync(host, reviewDirectory);
    }

    static async Task CheckSceneCropPreviewAsync(Grid host, string? reviewDirectory)
    {
        var source = new ProjectDocument { Width = 960, Height = 720 };
        source.Canvas.Crop = new SourceCrop { X = .25, Y = .1, Width = .5, Height = .8 };
        var preset = ScenePreset.Capture(source, "局部画面");
        var target = new ProjectDocument { Width = 1920, Height = 1080 };
        var completion = new TaskCompletionSource<BitmapSource?>();
        var sheet = new ScenePresetCropPreviewPanel(preset, target, _ => completion.Task) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var canceled = 0; var applied = 0; sheet.CancelRequested += () => canceled++; sheet.ApplyRequested += () => applied++;
        host.Children.Add(sheet); host.UpdateLayout(); await Task.Delay(40);
        Require(Descendants<ProgressBar>(sheet).Any(), "Crop preview does not show loading before the source frame arrives.");
        Require(Descendants<TextBlock>(sheet).Any(text => text.Text == "960 × 720 → 1920 × 1080"), "Crop preview source / target dimensions are wrong.");
        Require(Descendants<TextBlock>(sheet).Any(text => text.Text == "上 108 · 下 108 · 左 480 · 右 480 px"), "Crop preview uses the preset's old pixel dimensions instead of adapting proportions.");
        var pixels = Enumerable.Repeat((byte)255, 100 * 100 * 4).ToArray();
        var white = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, pixels, 400); white.Freeze();
        completion.SetResult(white); await Task.Delay(40); host.UpdateLayout();
        var preview = Descendants<Grid>(sheet).Single(grid => System.Windows.Automation.AutomationProperties.GetName(grid) == "裁切适配预览");
        Require(Math.Abs(preview.ActualWidth - 452) < 1 && Math.Abs(preview.ActualHeight - 254.25) < 1, "Crop preview aspect or 452 × 260 size limits are wrong.");
        var image = new RenderTargetBitmap((int)Math.Ceiling(sheet.ActualWidth), (int)Math.Ceiling(sheet.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(sheet);
        byte Sample(double x)
        {
            var point = preview.TransformToAncestor(sheet).Transform(new Point(x, preview.ActualHeight / 2));
            var color = new byte[4]; image.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), color, 4, 0); return color[0];
        }
        // Uniform scaling gives this square fixture side bars; use pixels inside the source image.
        Require(Sample(105) is >= 95 and <= 110 && Sample(226) >= 245, "Crop mask darkens the retained region or fails to dim the discarded region by 60%.");
        if (reviewDirectory is not null) SaveVisual(sheet, System.IO.Path.Combine(reviewDirectory, "windows-scene-crop-preview.png"));
        var buttons = Descendants<Button>(sheet).ToArray();
        buttons.Single(button => Equals(button.Tag, "cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(canceled == 1 && applied == 0, "Canceling a crop preview applies the scene.");
        buttons.Single(button => Equals(button.Tag, "apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(applied == 1, "Confirmed crop preview does not request scene application.");
        host.Children.Clear();
        var unavailable = new ScenePresetCropPreviewPanel(preset, target, _ => Task.FromResult<BitmapSource?>(null)); host.Children.Add(unavailable); host.UpdateLayout(); await Task.Delay(40);
        Require(Descendants<TextBlock>(unavailable).Any(text => text.Text.Contains("预览暂不可用")) && !Descendants<ProgressBar>(unavailable).Any(), "Unavailable crop thumbnails remain stuck in a loading state.");
        Require(ScenePresetCropPreviewPanel.CropSummary(new SourceCrop { Y = .125, Height = .75 }, 100, 100).Contains("上 13"), "Crop summary rounds half pixels differently from the Mac source.");
        Console.WriteLine("UI_OK scene crop preview: adapted pixel summary, source frame, retained region, cancel/apply and unavailable thumbnail"); host.Children.Clear();
    }

    static async Task CheckSettingsAsync(Grid host, string? reviewDirectory)
    {
        var miniature = new SettingsAppearancePreview("system") { Width = 140, Height = 58, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(miniature); host.UpdateLayout();
        var pixels = new RenderTargetBitmap(140, 58, 96, 96, PixelFormats.Pbgra32); pixels.Render(miniature);
        byte GrayAt(int x, int y) { var pixel = new byte[4]; pixels.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel[0]; }
        Require(GrayAt(10, 40) == 240 && GrayAt(80, 5) == 26, "System appearance miniature loses its light sidebar or dark right half.");
        Require(GrayAt(34, 19) == 255 && GrayAt(45, 19) == 235 && GrayAt(80, 19) == 59,
            "The miniature draws separate half-width layouts instead of a continuous content line.");
        host.Children.Clear();
        var language = SettingsStore.Current.Language;
        var settings = new SettingsWindow(); var root = (FrameworkElement)settings.Content; settings.Content = null;
        root.Width = 780; root.Height = 640; root.HorizontalAlignment = HorizontalAlignment.Left; root.VerticalAlignment = VerticalAlignment.Top;
        ((Grid)root).SetResourceReference(Panel.BackgroundProperty, "CardBrush"); host.Children.Add(root);
        void Navigate(string section) => Descendants<Button>(root).Single(button => Equals(button.Tag, section)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var locale in new[] { "zh-Hans", "en" })
            {
                SettingsStore.Current.Language = locale; LanguageCatalog.Current.Refresh(); settings.RefreshLanguage();
                Navigate("general"); await Task.Delay(520); host.UpdateLayout();
                var nav = Descendants<Button>(root).Single(button => Equals(button.Tag, "permissions"));
                var label = Descendants<TextBlock>(nav).Single();
                Require(nav.ActualWidth >= label.ActualWidth + 54, "Settings navigation clips translated section labels.");
                if (reviewDirectory is not null) SaveVisual(root, System.IO.Path.Combine(reviewDirectory, "windows-settings-general-" + locale + ".png"));
                Navigate("editor"); await Task.Delay(520); host.UpdateLayout();
                foreach (var choice in Descendants<Button>(root).Where(button => Equals(button.Content, L.Text("清晰预览")) || Equals(button.Content, L.Text("完整分辨率"))))
                {
                    var text = Descendants<TextBlock>(choice).First();
                    text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Require(choice.ActualWidth >= text.DesiredSize.Width + 24, "Translated preview quality options clip their text.");
                }
                var quality = Descendants<FluidSegments>(root).Single();
                Require(Math.Abs(((Border)quality.Parent).ActualHeight - 40) < 1,
                    "Preview quality well stretches to the description height instead of its 32-point buttons plus padding.");
                Navigate("recording"); await Task.Delay(520); host.UpdateLayout();
                var camera = Descendants<FluidSegments>(root).Single(selector => selector.Buttons.Any(button => button.Content is StackPanel));
                var buttons = camera.Buttons.ToArray();
                var gap = buttons[1].TranslatePoint(new Point(), camera).X - buttons[0].TranslatePoint(new Point(buttons[0].ActualWidth, 0), camera).X;
                Require(Math.Abs(gap - 12) <= 1 / VisualTreeHelper.GetDpi(root).DpiScaleX + .01,
                    $"Camera shapes have merged into a continuous segmented well: gap={gap:0.000}.");
                if (reviewDirectory is not null) SaveVisual(root, System.IO.Path.Combine(reviewDirectory, "windows-settings-recording-" + locale + ".png"));
            }
            Console.WriteLine("UI_OK settings: continuous miniature, bilingual navigation/quality fit, separate camera tiles");
        }
        finally { SettingsStore.Current.Language = language; LanguageCatalog.Current.Refresh(); host.Children.Clear(); settings.Close(); }
    }

    static void SaveVisual(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path); encoder.Save(file);
    }

    static void RenderSymbols(string path)
    {
        IconKind[] symbols = [IconKind.CanvasPicture, IconKind.OpeningStack, IconKind.MotionViewfinder,
            IconKind.CursorMotion, IconKind.CameraFilled, IconKind.SpeakerFilled, IconKind.Layers,
            IconKind.InsetRectangle, IconKind.Window, IconKind.MotionWave, IconKind.Bolt, IconKind.Wind,
            IconKind.Converge, IconKind.SideSlide, IconKind.Cube, IconKind.Scope, IconKind.SceneStack,
            IconKind.DocumentUp, IconKind.DocumentDown, IconKind.Code, IconKind.Chat, IconKind.PhotoCamera,
            IconKind.Settings, IconKind.Shield, IconKind.RecordCircle, IconKind.Counterclockwise];
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            for (var theme = 0; theme < 2; theme++)
            {
                var x = theme * 300; var ink = theme == 0 ? Brushes.Black : Brushes.White;
                drawing.DrawRectangle(theme == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(32, 33, 35)), null, new Rect(x, 0, 300, symbols.Length * 32 + 32));
                for (var i = 0; i < symbols.Length; i++)
                {
                    drawing.PushTransform(new TranslateTransform(x + 14, 14 + i * 32));
                    DogSC.Icon.DrawSymbol(drawing, symbols[i], ink, 19, 19); drawing.Pop();
                    drawing.DrawText(new FormattedText(symbols[i].ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, ink, 1), new Point(x + 48, 15 + i * 32));
                }
            }
        }
        var bitmap = new RenderTargetBitmap(600, symbols.Length * 32 + 32, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(path); png.Save(output);
        Console.WriteLine("UI symbol review: " + path);
    }
}
