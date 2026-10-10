namespace DogSC.Core;

/// <summary>
/// Mac「导出源文件」：把项目的原始屏幕 / 摄像头 / 麦克风 / 系统声音素材
/// 原样拷到 <项目名>-源文件/ 下（不做任何剪辑 / 转码），供外部工具加工。
/// Mac 只处理单条录屏会话；windogsc 支持多素材，第二个起文件名加序号。
/// </summary>
public static class SourceExport
{
    /// <summary>导出全部素材源文件；返回建好的文件夹完整路径。没有可拷贝的源文件时报错。</summary>
    public static string Export(ProjectDocument project, string projectDirectory, string destinationParent)
    {
        if (project.Assets.Count == 0)
        {
            throw new InvalidDataException("项目没有素材可导出。");
        }

        var folder = UniqueFolder(destinationParent, Sanitize(project.Name) + "-源文件");
        var copied = new List<string>();
        try
        {
            Directory.CreateDirectory(folder);
            var multiple = project.Assets.Count > 1;
            var ordinal = 0;
            foreach (var asset in project.Assets)
            {
                ordinal++;
                var suffix = multiple ? "-" + ordinal : "";
                Copy(asset.Video, "屏幕" + suffix);
                Copy(asset.Camera, "摄像头" + suffix);
                Copy(asset.Microphone, "麦克风" + suffix);
                Copy(asset.SystemAudio, "系统声音" + suffix);
            }

            if (copied.Count == 0)
            {
                throw new InvalidDataException("项目没有可导出的源文件。");
            }

            return folder;
        }
        catch
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch { }
            throw;
        }

        void Copy(string? relative, string baseName)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return;
            }

            var path = Path.IsPathRooted(relative) ? relative : ProjectFile.Media(projectDirectory, relative);
            if (!File.Exists(path))
            {
                return;
            }

            var ext = Path.GetExtension(path);
            var dest = Path.Combine(folder, baseName + ext);
            File.Copy(path, dest);
            copied.Add(dest);
        }
    }

    private static string UniqueFolder(string parent, string baseName)
    {
        var candidate = Path.Combine(parent, baseName);
        var suffix = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(parent, baseName + "-" + suffix);
            suffix++;
        }

        return candidate;
    }

    private static string Sanitize(string name)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '-');
        return ProjectFile.SanitizeName(name);
    }
}
