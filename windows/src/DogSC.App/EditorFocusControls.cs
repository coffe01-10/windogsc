using DogSC.Core;
using System.Windows.Media.Effects;

namespace DogSC;

public partial class EditorWindow
{
    FocusTrack? _focusTrack;
    WriteableBitmap? _focusMask;
    readonly BlurEffect _spatialBlur = new();

    void BuildFocusControls(Panel panel, Func<FocusEffect?> read, Action<FocusEffect?> write, bool linear)
    {
        AddToggle(panel, L.Text(linear ? "线性柔化" : "启用聚焦"), read() is not null,
            on => { write(on ? linear ? FocusEffect.Linear() : new FocusEffect() : null); RebuildInspector(); });
        if (read() is not { } effect) return;
        var controls = new StackPanel(); panel.Children.Add(controls);
        if (linear)
            controls.Children.Add(ValueSlider(L.Text("方向"), 0, 360, effect.AngleDegrees, v => effect.AngleDegrees = v, v => $"{v:0}°"));
        else
        {
            controls.Children.Add(Segments([(L.Text("随运镜"), "animation"), (L.Text("随鼠标"), "pointer"), (L.Text("固定"), "fixed")],
                effect.Target, target => { Push(); effect.Target = target; Commit(); }));
            if (effect.Target == "fixed")
                controls.Children.Add(VectorPad(L.Text("聚焦位置"), effect.Center.X, effect.Center.Y, (x, y) => effect.Center = new(x, y)));
        }
        controls.Children.Add(ValueSlider(L.Text("清晰范围"), .1, 1, effect.Size, v => effect.Size = v, Percent));
        controls.Children.Add(ValueSlider(L.Text("模糊程度"), 0, 1, effect.Blur, v => effect.Blur = v, Percent));
        controls.Children.Add(ValueSlider(L.Text("羽化范围"), 0, 1, effect.Softness, v => effect.Softness = v, Percent));
        if (!linear) controls.Children.Add(ValueSlider(L.Text("暗角"), 0, 1, effect.Dimming, v => effect.Dimming = v, Percent));
        controls.Children.Add(Caption(L.Text("只处理录屏与背景，保留摄像头和贴图清晰。随动画进入、退出；暗角可设为 0。")));
    }

    void UpdateSpatialFocus(ZoomPose pose, ZoomSpan? editingZoom)
    {
        SpatialFocusLayer.Visibility = Visibility.Collapsed;
        if (_pvFrameScale <= 0 || _pvOutW <= 0 || _pvOutH <= 0) return;
        var selected = !_player.IsPlaying ? _project.ScreenMotions.FirstOrDefault(s => s.Id == _selectedDecoration) : null;
        var scene = (_focusTrack ??= new FocusTrack(_project)).At(_player.Time, editingZoom, selected, pose);
        if (scene is null) return;
        var rw = Math.Min(640, _pvOutW);
        var rh = Math.Max(1, (int)Math.Round(rw * _pvOutH / (double)_pvOutW));
        if (_focusMask is null || _focusMask.PixelWidth != rw || _focusMask.PixelHeight != rh)
        {
            _focusMask = new WriteableBitmap(rw, rh, 96, 96, PixelFormats.Bgra32, null);
            SpatialFocusLayer.OpacityMask = new ImageBrush(_focusMask) { Stretch = Stretch.Fill };
        }
        var raster = new FocusRaster(_project, scene, _pvFrame, _pvOutW, _pvOutH, _player.Time, pose, selected);
        var gray = raster.Gray(rw, rh, _pvOutW, _pvOutH);
        var pixels = new byte[gray.Length * 4];
        for (var i = 0; i < gray.Length; i++)
        {
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = 255;
            pixels[i * 4 + 3] = gray[i];
        }
        _focusMask.WritePixels(new Int32Rect(0, 0, rw, rh), pixels, rw * 4, 0);
        SpatialFocusLayer.Width = PreviewCanvas.Width; SpatialFocusLayer.Height = PreviewCanvas.Height;
        SpatialFocusBlur.Fill ??= new VisualBrush(BaseScene) { Stretch = Stretch.Fill };
        _spatialBlur.Radius = scene.Effect.BlurRadius(_pvOutW, _pvOutH) * _pvFrameScale;
        SpatialFocusBlur.Effect = _spatialBlur.Radius > .01 ? _spatialBlur : null;
        SpatialFocusDim.Opacity = scene.Effect.Dimming;
        SpatialFocusLayer.Visibility = Visibility.Visible;
    }
}
