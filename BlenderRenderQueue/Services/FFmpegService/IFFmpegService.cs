using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.FFmpegService;

/// <summary>
/// FFmpeg 服务接口
/// </summary>
public interface IFFmpegService
{
    /// <summary>
    /// 从图片序列生成 H.265 视频
    /// </summary>
    /// <param name="inputDirectory">输入图片目录</param>
    /// <param name="outputVideoPath">输出视频路径</param>
    /// <param name="fps">帧率</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<bool> GenerateVideoFromImagesAsync(
        string inputDirectory,
        string outputVideoPath,
        double fps,
        Action<double>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查 FFmpeg 是否可用
    /// </summary>
    /// <returns>是否可用</returns>
    Task<bool> IsFFmpegAvailableAsync();
}
