using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace BlenderSuite.RenderQueue.Controls;

public partial class ImagePreviewControl : UserControl
{
    private Bitmap? _image;
    private bool _isLoading;

    public static readonly DirectProperty<ImagePreviewControl, Bitmap?> ImageProperty =
        AvaloniaProperty.RegisterDirect<ImagePreviewControl, Bitmap?>(
            nameof(Image),
            o => o.Image,
            (o, v) => o.Image = v);

    public static readonly DirectProperty<ImagePreviewControl, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<ImagePreviewControl, bool>(
            nameof(IsLoading),
            o => o.IsLoading,
            (o, v) => o.IsLoading = v);

    public Bitmap? Image
    {
        get => _image;
        set => SetAndRaise(ImageProperty, ref _image, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetAndRaise(IsLoadingProperty, ref _isLoading, value);
    }

    public ImagePreviewControl()
    {
        InitializeComponent();
        DataContext = this;
    }
} 