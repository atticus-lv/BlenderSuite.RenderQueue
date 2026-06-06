using Avalonia.Controls;
using Avalonia.Interactivity;
using BlenderSuite.RenderQueue.Helpers;

namespace BlenderSuite.RenderQueue.Views;

public partial class InfoView : UserControl
{
    public InfoView()
    {
        InitializeComponent();
    }

    private void OpenUrlButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url })
        {
            return;
        }

        UrlLaunchHelper.OpenUrl(url);
    }
}
