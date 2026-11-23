using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Orrery.App.Views;

/// <summary>A thin ring that fills clockwise from the top. Used wherever a goal has a finish line.</summary>
public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Thickness), 5);

    static ProgressRing() => AffectsRender<ProgressRing>(ValueProperty, StrokeProperty, ThicknessProperty);

    /// <summary>0 to 100.</summary>
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var radius = (size - Thickness) / 2;
        if (radius <= 0)
            return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#2A6B7CFF")), Thickness), center, radius, radius);

        var fraction = Math.Clamp(Value / 100, 0, 1);
        if (fraction <= 0.001)
            return;
        var pen = new Pen(Stroke ?? Brushes.White, Thickness, lineCap: PenLineCap.Round);
        if (fraction >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var start = PointOn(center, radius, -90);
            var end = PointOn(center, radius, -90 + fraction * 360);
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOn(Point c, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
