using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Models;

public class LanguageOption(string value, string displayName)
{
    public string Value { get; } = value;
    public string DisplayName { get; set; } = displayName;

    public static IReadOnlyList<LanguageOption> AllOptions { get; } = new List<LanguageOption>
    {
        new("en-US", "English"),
        new("zh-CN", "中文")
    };

    public static LanguageOption Default => AllOptions[0]; // en-US

    public static LanguageOption? FindByValue(string value)
    {
        return AllOptions.FirstOrDefault(option => option.Value == value);
    }
}