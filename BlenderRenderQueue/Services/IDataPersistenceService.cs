using System.Threading.Tasks;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services;

/// <summary>
/// 数据持久化服务接口
/// </summary>
public interface IDataPersistenceService
{
    /// <summary>
    /// 保存应用程序数据到文件
    /// </summary>
    /// <param name="data">要保存的数据</param>
    /// <returns>保存是否成功</returns>
    Task<bool> SaveDataAsync(AppData data);

    /// <summary>
    /// 从文件加载应用程序数据
    /// </summary>
    /// <returns>加载的数据，如果失败则返回默认数据</returns>
    Task<AppData> LoadDataAsync();

    /// <summary>
    /// 检查数据文件是否存在
    /// </summary>
    /// <returns>文件是否存在</returns>
    bool DataFileExists();

    /// <summary>
    /// 删除数据文件
    /// </summary>
    /// <returns>删除是否成功</returns>
    bool DeleteDataFile();
}
