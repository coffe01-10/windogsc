namespace DogSC.Core;

// Only stable target descriptions are persisted. HWNDs are resolved from the
// current catalog, because Windows can reuse a closed window's native handle.
public sealed class RecentCaptureTarget
{
    public string Kind { get; set; } = "Display";
    public string Label { get; set; } = "";
    public string? ProcessName { get; set; }
    public string? DeviceName { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
