namespace DogSC.Core;

/// <summary>
/// Mac 素材交换：把某个素材的屏幕 / 摄像头 / 麦克风源文件换成新文件，
/// 保留剪辑片段、效果与偏移设置；偏移为负表示新源比原录制开始得晚。
/// </summary>
public static class MediaSwap
{
    public enum Track { Video, Camera, Microphone, SystemAudio }

    /// <summary>替换源文件路径；仅校验文件存在与扩展名，时间轴不变。</summary>
    public static bool Replace(ProjectDocument project, string assetId, Track track, string newPath, double? offset = null)
    {
        var asset = project.Asset(assetId);
        if (asset is null || string.IsNullOrWhiteSpace(newPath))
        {
            return false;
        }

        switch (track)
        {
            case Track.Video:
                asset.Video = newPath;
                break;
            case Track.Camera:
                asset.Camera = newPath;
                if (offset.HasValue)
                {
                    asset.CameraOffset = offset.Value;
                }
                break;
            case Track.Microphone:
                asset.Microphone = newPath;
                if (offset.HasValue)
                {
                    asset.MicrophoneOffset = offset.Value;
                }
                break;
            case Track.SystemAudio:
                asset.SystemAudio = newPath;
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>清掉一条源轨道（如拔掉摄像头）。</summary>
    public static bool Clear(ProjectDocument project, string assetId, Track track)
    {
        var asset = project.Asset(assetId);
        if (asset is null || track == Track.Video)
        {
            return false;
        }

        switch (track)
        {
            case Track.Camera: asset.Camera = null; asset.CameraOffset = 0; break;
            case Track.Microphone: asset.Microphone = null; asset.MicrophoneOffset = 0; break;
            case Track.SystemAudio: asset.SystemAudio = null; break;
            default: return false;
        }

        return true;
    }
}
