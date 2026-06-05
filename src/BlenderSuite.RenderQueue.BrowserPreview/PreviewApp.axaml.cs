using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System.Globalization;
using SukiUI;

namespace BlenderSuite.RenderQueue.BrowserPreview;

public partial class PreviewApp : Application
{
    public override void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        AvaloniaXamlLoader.Load(this);
        global::BlenderSuite.RenderQueue.Localizer.Localizer.Instance.LoadLanguage("en-US");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        SukiTheme.GetInstance(this).ChangeBaseTheme(ThemeVariant.Dark);

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new PreviewRootView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
