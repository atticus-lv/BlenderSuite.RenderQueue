using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BlenderRenderQueue.Controls;

public sealed class MiniUsageChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<MiniUsageChartControl, IReadOnlyList<double>?>(nameof(Values));

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

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
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
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var values = Values;
        if (values == null || values.Count == 0)
        {
            return;
        }

        var plotArea = Bounds.Deflate(2);
        if (plotArea.Width <= 1 || plotArea.Height <= 1)
        {
            return;
        }

        var range = Math.Max(0.0001d, MaxValue - MinValue);
        var points = new Point[values.Count];
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
            points[i] = new Point(x, y);
        }

        if (Fill != null)
        {
            var fillGeometry = new StreamGeometry();
            using (var geometry = fillGeometry.Open())
            {
                geometry.BeginFigure(new Point(points[0].X, plotArea.Bottom), true);
                geometry.LineTo(points[0]);

                for (var i = 1; i < points.Length; i++)
                {
                    geometry.LineTo(points[i]);
                }

                geometry.LineTo(new Point(points[^1].X, plotArea.Bottom));
                geometry.EndFigure(true);
            }

            context.DrawGeometry(Fill, null, fillGeometry);
        }

        if (Stroke != null)
        {
            var strokeGeometry = new StreamGeometry();
            using (var geometry = strokeGeometry.Open())
            {
                geometry.BeginFigure(points[0], false);

                for (var i = 1; i < points.Length; i++)
                {
                    geometry.LineTo(points[i]);
                }

                geometry.EndFigure(false);
            }

            var pen = new Pen(Stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, strokeGeometry);
        }
    }
}
