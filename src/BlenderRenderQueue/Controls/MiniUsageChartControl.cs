using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BlenderRenderQueue.Controls;

public sealed class MiniUsageChartControl : Control
{
    public static readonly DirectProperty<MiniUsageChartControl, IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.RegisterDirect<MiniUsageChartControl, IReadOnlyList<double>?>(
            nameof(Values),
            o => o.Values,
            (o, v) => o.Values = v);

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<MiniUsageChartControl, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<MiniUsageChartControl, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<MiniUsageChartControl, double>(nameof(StrokeThickness), 1.5d);

    public static readonly StyledProperty<double> MinValueProperty =
        AvaloniaProperty.Register<MiniUsageChartControl, double>(nameof(MinValue), 0d);

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<MiniUsageChartControl, double>(nameof(MaxValue), 100d);

    private IReadOnlyList<double>? _values;
    private bool _geometryDirty = true;
    private Rect _cachedBounds;
    private Point[] _pointBuffer = Array.Empty<Point>();
    private StreamGeometry? _cachedFillGeometry;
    private StreamGeometry? _cachedStrokeGeometry;
    private Pen? _cachedPen;

    static MiniUsageChartControl()
    {
        AffectsRender<MiniUsageChartControl>(
            StrokeProperty,
            FillProperty,
            StrokeThicknessProperty,
            MinValueProperty,
            MaxValueProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => _values;
        set
        {
            if (ReferenceEquals(_values, value))
            {
                return;
            }

            SetAndRaise(ValuesProperty, ref _values, value);
            InvalidateChart();
        }
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double MinValue
    {
        get => GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public double MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValuesProperty ||
            change.Property == StrokeProperty ||
            change.Property == FillProperty ||
            change.Property == StrokeThicknessProperty ||
            change.Property == MinValueProperty ||
            change.Property == MaxValueProperty ||
            change.Property == BoundsProperty)
        {
            _geometryDirty = true;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureGeometry();

        if (Fill != null && _cachedFillGeometry != null)
        {
            context.DrawGeometry(Fill, null, _cachedFillGeometry);
        }

        if (Stroke != null && _cachedStrokeGeometry != null && _cachedPen != null)
        {
            context.DrawGeometry(null, _cachedPen, _cachedStrokeGeometry);
        }
    }

    private void InvalidateChart()
    {
        _geometryDirty = true;
        InvalidateVisual();
    }

    private void EnsureGeometry()
    {
        if (!_geometryDirty && _cachedBounds == Bounds)
        {
            return;
        }

        _cachedBounds = Bounds;
        _geometryDirty = false;
        RebuildGeometry();
    }

    private void RebuildGeometry()
    {
        _cachedFillGeometry = null;
        _cachedStrokeGeometry = null;
        _cachedPen = null;

        var values = Values;
        if (values == null || values.Count == 0)
        {
            return;
        }

        var plotArea = Bounds.Deflate(Math.Max(2d, StrokeThickness));
        if (plotArea.Width <= 1 || plotArea.Height <= 1)
        {
            return;
        }

        if (_pointBuffer.Length < values.Count)
        {
            Array.Resize(ref _pointBuffer, values.Count);
        }

        var range = Math.Max(0.0001d, MaxValue - MinValue);
        var stepX = values.Count == 1 ? 0d : plotArea.Width / (values.Count - 1);

        for (var i = 0; i < values.Count; i++)
        {
            var rawValue = values[i];
            if (double.IsNaN(rawValue) || double.IsInfinity(rawValue))
            {
                rawValue = MinValue;
            }

            var normalized = Math.Clamp((rawValue - MinValue) / range, 0d, 1d);
            var x = values.Count == 1
                ? plotArea.Left + plotArea.Width / 2d
                : plotArea.Left + i * stepX;
            var y = plotArea.Bottom - normalized * plotArea.Height;
            _pointBuffer[i] = new Point(x, y);
        }

        if (Fill != null)
        {
            var fillGeometry = new StreamGeometry();
            using (var geometry = fillGeometry.Open())
            {
                geometry.BeginFigure(new Point(_pointBuffer[0].X, plotArea.Bottom), true);
                geometry.LineTo(_pointBuffer[0]);

                for (var i = 1; i < values.Count; i++)
                {
                    geometry.LineTo(_pointBuffer[i]);
                }

                geometry.LineTo(new Point(_pointBuffer[values.Count - 1].X, plotArea.Bottom));
                geometry.EndFigure(true);
            }

            _cachedFillGeometry = fillGeometry;
        }

        if (Stroke == null)
        {
            return;
        }

        var strokeGeometry = new StreamGeometry();
        using (var geometry = strokeGeometry.Open())
        {
            geometry.BeginFigure(_pointBuffer[0], false);

            for (var i = 1; i < values.Count; i++)
            {
                geometry.LineTo(_pointBuffer[i]);
            }

            geometry.EndFigure(false);
        }

        _cachedStrokeGeometry = strokeGeometry;
        _cachedPen = new Pen(Stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    }
}
