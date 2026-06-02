using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using BlenderRenderQueue.Extensions;

namespace BlenderRenderQueue.Behaviors;

public class ImagePanZoomBehavior : Behavior<Panel>
{
    // 缩放参数
    private const double ZoomScaleLogMap = 1.05;
    private const double MinScale = 0.5; // 限制最小缩放为25%，防止图像缩得过于小
    private const double MaxScale = 10.0;
    private double _currentScale = 1.0;

    // 缩放动画参数
    private double _zoomVelocity;
    private bool _isZooming;
    private const double ZoomAnimSpeed = 1.5;
    private const double ZoomAnimStopVelocity = 0.05;
    private const double ZoomAnimSpeedDecay = 0.85;
    private const int ZoomUpdateInterval = 8; // 毫秒

    // 平移参数
    private Point _lastPanPoint;
    private bool _isPanning;
    
    // 变换对象
    private readonly ScaleTransform _scaleTransform = new() { ScaleX = 1.0, ScaleY = 1.0 };
    private readonly TranslateTransform _translateTransform = new() { X = 0, Y = 0 };
    private readonly TransformGroup _transformGroup = new();

    // 防抖动参数
    private const double MinDragDistance = 1.0; // 最小拖动距离，防止微小抖动

    // 上次双击时间
    private DateTime _lastDoubleClickTime = DateTime.MinValue;
    private const double DoubleClickTimeThreshold = 300; // 毫秒

    // 目标图像控件
    private Image? _viewBoxImage;

    // 属性定义
    public static readonly StyledProperty<bool> EnableZoomProperty =
        AvaloniaProperty.Register<ImagePanZoomBehavior, bool>(nameof(EnableZoom), true);

    public bool EnableZoom
    {
        get => GetValue(EnableZoomProperty);
        set => SetValue(EnableZoomProperty, value);
    }

    public static readonly StyledProperty<bool> EnablePanProperty =
        AvaloniaProperty.Register<ImagePanZoomBehavior, bool>(nameof(EnablePan), true);

    public bool EnablePan
    {
        get => GetValue(EnablePanProperty);
        set => SetValue(EnablePanProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        
        if (AssociatedObject == null) return;

        // 设置变换组
        _transformGroup.Children.Add(_scaleTransform);
        _transformGroup.Children.Add(_translateTransform);
        
        // 查找子Image控件
        AssociatedObject.Loaded += OnLoaded;
        
        // 注册事件
        AssociatedObject.PointerWheelChanged += OnPointerWheelChanged;
        AssociatedObject.PointerPressed += OnPointerPressed;
        AssociatedObject.PointerReleased += OnPointerReleased;
        AssociatedObject.PointerMoved += OnPointerMoved;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.Loaded -= OnLoaded;
            AssociatedObject.PointerWheelChanged -= OnPointerWheelChanged;
            AssociatedObject.PointerPressed -= OnPointerPressed;
            AssociatedObject.PointerReleased -= OnPointerReleased;
            AssociatedObject.PointerMoved -= OnPointerMoved;
        }

        base.OnDetaching();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        FindImageControl();
        Reset();
    }

    private void FindImageControl()
    {
        if (AssociatedObject == null) return;
        
        // 查找第一个Image子控件
        _viewBoxImage = AssociatedObject.FindDescendantOfType<Image>();
        
        if (_viewBoxImage != null)
        {
            _viewBoxImage.RenderTransform = _transformGroup;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (AssociatedObject == null || _viewBoxImage == null || !EnableZoom) return;

        var delta = e.Delta.Y > 0 ? 1 : -1;
        
        // 设置缩放速度并开始缩放动画
        _zoomVelocity = delta * ZoomAnimSpeed;
        if (!_isZooming)
        {
            _isZooming = true;
            StartZoomAnimation().FireAndForget(
                source: nameof(ImagePanZoomBehavior),
                message: "图片缩放动画后台任务失败。");
        }

        e.Handled = true;
    }

    private async Task StartZoomAnimation()
    {
        try
        {
            while (Math.Abs(_zoomVelocity) > ZoomAnimStopVelocity)
            {
                _currentScale *= Math.Pow(ZoomScaleLogMap, _zoomVelocity);
                _currentScale = Math.Clamp(_currentScale, MinScale, MaxScale);

                _scaleTransform.ScaleX = _currentScale;
                _scaleTransform.ScaleY = _currentScale;

                _zoomVelocity *= ZoomAnimSpeedDecay;
                await Task.Delay(ZoomUpdateInterval);
            }
        }
        finally
        {
            _isZooming = false;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (AssociatedObject == null || _viewBoxImage == null) return;

        var position = e.GetPosition(AssociatedObject);
        
        // 检测双击
        if (e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed)
        {
            var now = DateTime.Now;
            var timeDiff = (now - _lastDoubleClickTime).TotalMilliseconds;
            
            if (timeDiff < DoubleClickTimeThreshold)
            {
                // 双击重置
                Reset();
                e.Handled = true;
                _lastDoubleClickTime = DateTime.MinValue; // 重置时间，避免连续重置
                return;
            }
            
            _lastDoubleClickTime = now;
        }

        // 检查是否按下了中键或左键
        if (!EnablePan) return;
        
        if (!e.GetCurrentPoint(AssociatedObject).Properties.IsMiddleButtonPressed &&
            !e.GetCurrentPoint(AssociatedObject).Properties.IsLeftButtonPressed) return;

        _isPanning = true;
        _lastPanPoint = position;
        e.Pointer.Capture(AssociatedObject);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // 只处理中键和左键的释放
        if (!_isPanning || (e.InitialPressMouseButton != MouseButton.Middle && 
                            e.InitialPressMouseButton != MouseButton.Left)) return;
        
        _isPanning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (AssociatedObject == null || _viewBoxImage == null || !_isPanning) return;

        // 计算增量位移
        var currentPoint = e.GetPosition(AssociatedObject);
        var delta = currentPoint - _lastPanPoint;
        _lastPanPoint = currentPoint;
        
        // 直接应用平移变换
        _translateTransform.X += delta.X;
        _translateTransform.Y += delta.Y;
        
        e.Handled = true;
    }

    /// <summary>
    /// 重置图像到初始状态
    /// </summary>
    public void Reset()
    {
        _currentScale = 1.0;
        _scaleTransform.ScaleX = _currentScale;
        _scaleTransform.ScaleY = _currentScale;
        _translateTransform.X = 0;
        _translateTransform.Y = 0;
    }
    
    /// <summary>
    /// 水平翻转图像
    /// </summary>
    public void FlipHorizontally()
    {
        _scaleTransform.ScaleX *= -1;
    }

    /// <summary>
    /// 垂直翻转图像
    /// </summary>
    public void FlipVertically()
    {
        _scaleTransform.ScaleY *= -1;
    }
}
