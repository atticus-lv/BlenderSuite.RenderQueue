using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Models;

public class ThemeOption
{
    public string Value { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public ThemeOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

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