using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Models;

/// <summary>
/// 语言选项模型
/// </summary>
public class LanguageOption
{
    public string Value { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public LanguageOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    /// <summary>
    /// 所有可用的语言选项
    /// </summary>
    public static IReadOnlyList<LanguageOption> AllOptions { get; } = new List<LanguageOption>
    {
        new("en-US", "English"),
        new("zh-CN", "中文")
    };

    /// <summary>
    /// 默认语言选项
    /// </summary>
    public static LanguageOption Default => AllOptions[0]; // en-US

    /// <summary>
    /// 根据值查找语言选项
    /// </summary>
    public static LanguageOption? FindByValue(string value)
    {
        return AllOptions.FirstOrDefault(option => option.Value == value);
    }
}
