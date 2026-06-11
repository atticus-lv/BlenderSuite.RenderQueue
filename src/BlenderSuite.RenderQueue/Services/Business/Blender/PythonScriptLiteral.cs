using System.Text;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

internal static class PythonScriptLiteral
{
    public static string FromString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                _ when char.IsControl(ch) => $"\\u{(int)ch:x4}",
                _ => ch
            });
        }

        builder.Append('"');
        return builder.ToString();
    }
}
