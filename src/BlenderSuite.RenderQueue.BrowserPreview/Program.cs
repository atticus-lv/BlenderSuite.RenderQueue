using Avalonia;
using Avalonia.Browser;
using System.Threading.Tasks;

namespace BlenderSuite.RenderQueue.BrowserPreview;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
        .WithInterFont()
#if DEBUG
        .WithDeveloperTools()
#endif
        .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<PreviewApp>();
}
