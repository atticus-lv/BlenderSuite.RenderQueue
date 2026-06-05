using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;

namespace BlenderSuite.RenderQueue.Services.Business.Persistence;

/// <summary>
/// 数据持久化服务接口
/// </summary>
public interface IDataPersistenceService
{
    /// <summary>
    /// 保存应用程序数据到文件
    /// </summary>
    /// <param name="data">要保存的数据</param>
    /// <param name="filePath">可选的目标文件路径；为空时使用默认自动恢复文件</param>
    /// <returns>保存是否成功</returns>
    Task<bool> SaveDataAsync(AppData data, string? filePath = null);

    /// <summary>
    /// 从文件加载应用程序数据
    /// </summary>
    /// <param name="filePath">可选的源文件路径；为空时使用默认自动恢复文件</param>
    /// <returns>加载的数据，如果失败则返回默认数据</returns>
    Task<AppData> LoadDataAsync(string? filePath = null);

    /// <summary>
    /// 检查数据文件是否存在
    /// </summary>
    /// <param name="filePath">可选的文件路径；为空时使用默认自动恢复文件</param>
    /// <returns>文件是否存在</returns>
    bool DataFileExists(string? filePath = null);

    /// <summary>
    /// 删除数据文件
    /// </summary>
    /// <param name="filePath">可选的文件路径；为空时使用默认自动恢复文件</param>
    /// <returns>删除是否成功</returns>
    bool DeleteDataFile(string? filePath = null);
}
