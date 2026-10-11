using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    (string Id, double Scale)? _zoomStyleBefore;
    (string Id, ScreenMotionCreationStyle Style)? _screenStyleBefore;
    bool _pendingMotionPreferences;

    void CaptureMotionEdit()
    {
        var zoom = _project.Zooms.FirstOrDefault(clip => clip.Id == _selectedZoom);
        _zoomStyleBefore = zoom is null ? null : (zoom.Id, zoom.Scale);
        var screen = _project.ScreenMotions.FirstOrDefault(clip => clip.Id == _selectedDecoration);
        _screenStyleBefore = screen is null ? null : (screen.Id, ScreenMotionCreationStyle.Capture(screen));
    }

    void RememberMotionEdit()
    {
        if (_zoomStyleBefore is { } beforeZoom && _project.Zooms.FirstOrDefault(clip => clip.Id == beforeZoom.Id) is { } zoom
            && Math.Abs(beforeZoom.Scale - zoom.Scale) > .000001)
        {
            _project.Motion.DefaultZoomScale = zoom.Scale;
            SettingsStore.Current.RememberedZoomScale = zoom.Scale; _pendingMotionPreferences = true;
            _zoomStyleBefore = (zoom.Id, zoom.Scale);
        }
        if (_screenStyleBefore is { } beforeScreen && _project.ScreenMotions.FirstOrDefault(clip => clip.Id == beforeScreen.Id) is { } screen)
        {
            var style = ScreenMotionCreationStyle.Capture(screen);
            if (!beforeScreen.Style.Matches(style))
            {
                _project.Motion.DefaultScreenMotion = style;
                SettingsStore.Current.RememberedScreenMotion = ScenePreset.Clone(style); _pendingMotionPreferences = true;
                _screenStyleBefore = (screen.Id, style);
            }
        }
    }
}
