using DogSC.Core;
using System.Text.Json;

namespace DogSC;

public partial class EditorWindow
{
    ParameterReadout? _activeParameterReadout;
    string? _parameterSnapshot;

    ParameterReadout Parameter(string title, double min, double max, double value, Action<double> apply, Func<double, string> format,
        Action<ProjectDocument>? cancelOtherChanges = null)
    {
        var readout = new ParameterReadout(title, min, max, value, format);
        var initialValue = value;
        readout.EditingStarted += () =>
        {
            _activeParameterReadout?.FinishEditing();
            FlushPendingSave();
            _parameterSnapshot = JsonSerializer.Serialize(_project, ProjectSchema.Json);
            initialValue = readout.Value;
            CaptureMotionEdit(); _activeParameterReadout = readout;
        };
        readout.ValuePreviewed += v => { apply(v); Commit(rebuild: false); };
        readout.EditingFinished += commits =>
        {
            if (_activeParameterReadout != readout || _parameterSnapshot is not { } snapshot) return;
            _activeParameterReadout = null; _parameterSnapshot = null;
            if (!commits)
            {
                // Keep the same controls and objects through focus loss, so the
                // mouse-up can still activate the button the user clicked.
                apply(initialValue); readout.Value = initialValue;
                if (cancelOtherChanges is not null)
                    cancelOtherChanges(JsonSerializer.Deserialize<ProjectDocument>(snapshot, ProjectSchema.Json)!);
                _zoomStyleBefore = null; _screenStyleBefore = null;
                Commit(rebuild: false); FlushPendingSave(); return;
            }
            // A timing edit can be refused because it overlaps another clip.
            // Only an actual project change belongs in the undo stack.
            if (snapshot != JsonSerializer.Serialize(_project, ProjectSchema.Json))
                Push(snapshot, captureMotion: false);
            Commit(rebuild: false); FlushPendingSave();
        };
        return readout;
    }

    static void SyncParameterSlider(Slider slider, double value, ref bool synchronizing)
    {
        synchronizing = true;
        try { slider.Value = value; }
        finally { synchronizing = false; }
    }

}
