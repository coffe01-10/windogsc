using DogSC.Core;

namespace DogSC;

static class ScenePresets
{
    public static ScenePreset Capture(ProjectDocument project, string projectDirectory, string name,
        bool canvas = true, bool camera = true, bool opening = true, bool cursorBackdrop = true)
    {
        var preset = ScenePreset.Capture(project, name);
        preset.IncludesCanvas = canvas; preset.IncludesCamera = camera;
        preset.IncludesOpening = opening; preset.IncludesCursorBackdrop = cursorBackdrop;
        preset.Canvas.Stickers.Clear(); preset.Canvas.SoftRegions.Clear();
        if (!cursorBackdrop) preset.Canvas.Wallpaper = null;
        var folder = Path.Combine(AppPaths.Root, "presets", preset.Id);
        CopyImages(preset.Canvas, projectDirectory, folder, absolute: true);
        return preset;
    }

    public static void Apply(ScenePreset preset, ProjectDocument project, string directory)
    {
        var canvas = ScenePreset.Clone(preset.Canvas);
        canvas.Stickers.Clear(); canvas.SoftRegions.Clear();
        // Copy before changing the project, so a missing preset image does not
        // leave a partially applied layout or link to another project's media.
        if (preset.IncludesCursorBackdrop)
            CopyImages(canvas, AppPaths.Root, Path.Combine(directory, "media", "decor"), absolute: false, directory);
        var copied = ScenePreset.Clone(preset);
        copied.Canvas = canvas;
        copied.Apply(project);
    }

    public static void ApplyDefault(ProjectDocument project, string directory)
    {
        var settings = SettingsStore.Current;
        project.Camera.Shape = settings.CameraDefaultShape == "rounded" ? "square" : settings.CameraDefaultShape;
        project.Camera.Corner = settings.CameraDefaultShape == "square" ? 0 : 0.18;
        var preset = settings.ScenePresets.FirstOrDefault(p => p.Id == settings.DefaultScenePresetId);
        if (preset is null) return;
        try { Apply(preset, project, directory); }
        catch (Exception ex) { AppPaths.Log("默认场景未应用：" + ex.Message); }
    }

    public static void RemoveImages(ScenePreset preset)
    {
        if (!Guid.TryParseExact(preset.Id, "N", out _)) return;
        var folder = Path.Combine(AppPaths.Root, "presets", preset.Id);
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
        Directory.Delete(folder);
    }

    static void CopyImages(CanvasSettings canvas, string sourceDirectory, string targetDirectory, bool absolute, string? projectDirectory = null)
    {
        canvas.Wallpaper = Copy(canvas.Wallpaper);
        foreach (var sticker in canvas.Stickers) sticker.Path = Copy(sticker.Path) ?? "";
        string? Copy(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.StartsWith("pattern:") || path.StartsWith("flow:")) return path;
            var source = Path.IsPathRooted(path) ? path : ProjectFile.Media(sourceDirectory, path);
            Directory.CreateDirectory(targetDirectory);
            var target = Path.Combine(targetDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(source));
            File.Copy(source, target);
            return absolute ? target : Path.GetRelativePath(projectDirectory!, target).Replace('\\', '/');
        }
    }
}
