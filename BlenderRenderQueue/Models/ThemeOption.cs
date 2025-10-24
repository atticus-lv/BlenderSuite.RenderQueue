using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Models;

public class ThemeOption(string value, string displayName)
{
    public string Value { get; } = value;
    public string DisplayName { get; set; } = displayName;

    public static IReadOnlyList<ThemeOption> AllOptions { get; } = new List<ThemeOption>
    {
        new("Light", "Light"),
        new("Dark", "Dark"),
    };

    public static ThemeOption Default => AllOptions[1]; // Dark

    public static ThemeOption? FindByValue(string value)
    {
        return AllOptions.FirstOrDefault(option => option.Value == value);
    }
}