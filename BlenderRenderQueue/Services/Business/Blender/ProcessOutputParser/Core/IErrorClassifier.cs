namespace BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

/// <summary>
/// 错误分类器接口
/// </summary>
public interface IErrorClassifier
{
    /// <summary>
    /// 分类错误级别
    /// </summary>
    ErrorLevel ClassifyError(string line);
    
    /// <summary>
    /// 格式化错误信息
    /// </summary>
    string FormatError(string line, ErrorLevel level);
}
