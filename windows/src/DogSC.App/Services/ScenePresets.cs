using DogSC.Core;

namespace DogSC;

static class ScenePresets
{
    public static ScenePreset Capture(ProjectDocument project, string projectDirectory, string name,
        bool canvas = true, bool camera = true, bool opening = true, bool cursorBackdrop = true, string? id = null)
    {
        var preset = ScenePreset.Capture(project, name, id, SettingsStore.Current.RememberedZoomScale);
        preset.IncludesCanvas = canvas; preset.IncludesCamera = camera;
        preset.IncludesOpening = opening; preset.IncludesCursorBackdrop = cursorBackdrop;
        preset.Canvas.Stickers.Clear(); preset.Canvas.SoftRegions.Clear();
        if (!cursorBackdrop) preset.Canvas.Wallpaper = null;
        var folder = Path.Combine(AppPaths.Root, "presets", preset.Id);
        CopyImages(preset.Canvas, projectDirectory, folder, absolute: true);
        return preset;
    }

    public static void Apply(ScenePreset preset, ProjectDocument project, string directory)
        => PrepareApplication(preset, directory).Apply(project);

    public static ScenePreset PrepareApplication(ScenePreset preset, string directory)
    {
        var copied = ScenePreset.Clone(preset);
        var canvas = copied.Canvas;
        canvas.Stickers.Clear(); canvas.SoftRegions.Clear();
        // Resolve the background before changing the project. Missing images
        // retain the current background; available images become owned media.
        if (copied.IncludesCursorBackdrop && copied.IncludesBackground)
        {
            var path = canvas.Wallpaper;
            if (!string.IsNullOrWhiteSpace(path) && !path.StartsWith("pattern:") && !path.StartsWith("flow:")
                && (!Path.IsPathRooted(path) || !File.Exists(path)))
                copied.IncludesBackground = false;
            else CopyImages(canvas, AppPaths.Root, Path.Combine(directory, "media", "decor"), absolute: false, directory);
        }
        return copied;
    }

    public static void RememberLastUsed(ProjectDocument project)
    {
        SettingsStore.Current.LastUsedScene = ScenePreset.CaptureLastUsed(project);
        SettingsStore.Save();
    }

    public static void ApplyDefault(ProjectDocument project, string directory, AppSettings? settings = null)
    {
        settings ??= SettingsStore.Current;
        project.Camera.Shape = settings.CameraDefaultShape == "rounded" ? "square" : settings.CameraDefaultShape;
        project.Camera.Corner = settings.CameraDefaultShape == "square" ? 0 : 0.18;
        var preset = settings.ScenePresets.FirstOrDefault(p => p.Id == settings.DefaultScenePresetId) ?? settings.LastUsedScene;
        if (preset is null) return;
        try { Apply(preset, project, directory); }
        catch (Exception ex) { AppPaths.Log("默认场景未应用：" + ex.Message); }
    }

    public static void RemoveImages(ScenePreset preset, ScenePreset? retaining = null)
    {
        if (!Guid.TryParseExact(preset.Id, "N", out _)) return;
        var folder = Path.Combine(AppPaths.Root, "presets", preset.Id);
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
            if (!string.Equals(file, retaining?.Canvas.Wallpaper, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
        if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
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
            try { File.Copy(source, target); }
            catch
            {
                // A failed copy can leave a partial background; only remove this copy.
                if (File.Exists(target)) File.Delete(target);
                if (!Directory.EnumerateFileSystemEntries(targetDirectory).Any()) Directory.Delete(targetDirectory);
                throw;
            }
            return absolute ? target : Path.GetRelativePath(projectDirectory!, target).Replace('\\', '/');
        }
    }
}
