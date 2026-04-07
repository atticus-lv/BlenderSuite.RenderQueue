using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.Business.Blender;

/// <summary>
/// Blender视频生成服务接口
/// </summary>
public interface IBlenderVideoService
{
    /// <summary>
    /// 从图片序列生成视频
    /// </summary>
    /// <param name="inputDirectory">输入图片目录</param>
    /// <param name="outputVideoPath">输出视频路径</param>
    /// <param name="fps">帧率</param>
    /// <param name="videoCodec">视频编码器 (H264, H265, AV1)</param>
    /// <param name="videoQuality">视频质量 (PERC_LOSSLESS，LOSSLESS, HIGH, MEDIUM, LOW)</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<bool> GenerateVideoFromImagesAsync(
        string inputDirectory,
        string outputVideoPath,
        double fps,
        string videoCodec = "H264",
        string videoQuality = "PERC_LOSSLESS",
        Action<double>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查Blender是否可用
    /// </summary>
    /// <returns>是否可用</returns>
    Task<bool> IsBlenderAvailableAsync();

    /// <summary>
    /// 获取Blender版本信息
    /// </summary>
    /// <returns>版本信息，如果获取失败则返回 null</returns>
    Task<string?> GetBlenderVersionAsync();
}
