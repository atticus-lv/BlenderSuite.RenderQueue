using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;

namespace BlenderSuite.RenderQueue.Services.Business.Persistence;

/// <summary>
/// 设置数据持久化服务接口
/// </summary>
public interface ISettingsPersistenceService
{
    /// <summary>
    /// 保存设置数据到文件
    /// </summary>
    /// <param name="settings">设置数据</param>
    /// <returns>是否保存成功</returns>
    Task<bool> SaveSettingsAsync(SettingsData settings);

    /// <summary>
    /// 从文件加载设置数据
    /// </summary>
    /// <returns>设置数据，如果加载失败返回默认设置</returns>
    Task<SettingsData> LoadSettingsAsync();
}
