using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Orrery.App.ViewModels;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.Views;

/// <summary>
/// The orbit map. The rings are how soon a goal will want you (inner = now, outer = someday) and the three
/// sectors are the buckets, so how lopsided your life is shows at a glance. A body's size is its pressure and the
/// arc around it is progress. Positions depend only on bucket, ring and name, so nothing jumps between visits.
/// </summary>
public sealed class OrbitCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<OrbitBody>?> BodiesProperty =
        AvaloniaProperty.Register<OrbitCanvas, IReadOnlyList<OrbitBody>?>(nameof(Bodies));
    public static readonly StyledProperty<Guid?> SelectedIdProperty =
        AvaloniaProperty.Register<OrbitCanvas, Guid?>(nameof(SelectedId));
    public static readonly StyledProperty<ICommand?> BodyCommandProperty =
        AvaloniaProperty.Register<OrbitCanvas, ICommand?>(nameof(BodyCommand));

    static OrbitCanvas() => AffectsRender<OrbitCanvas>(BodiesProperty, SelectedIdProperty);

    public IReadOnlyList<OrbitBody>? Bodies { get => GetValue(BodiesProperty); set => SetValue(BodiesProperty, value); }
    public Guid? SelectedId { get => GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }
    public ICommand? BodyCommand { get => GetValue(BodyCommandProperty); set => SetValue(BodyCommandProperty, value); }

    private static readonly Horizon[] Rings = [Horizon.Now, Horizon.Week, Horizon.Month, Horizon.Later];
    private static readonly double[] RingFractions = [0.30, 0.52, 0.74, 0.96];

    // Where each bucket's sector starts, in degrees clockwise from east. Obligations sit at the top.
    private static readonly Dictionary<CategoryRole, double> SectorStart = new()
    {
        [CategoryRole.Obligation] = 210,
        [CategoryRole.Passion] = 330,
        [CategoryRole.Routine] = 90,
    };
    private const double SectorSpan = 120;
    private const double SectorPad = 16;

    private static readonly Dictionary<CategoryRole, (string Name, Color Color)> Buckets = new()
    {
        [CategoryRole.Obligation] = ("OBLIGATIONS", Color.Parse("#FF7A5C")),
        [CategoryRole.Passion] = ("PASSIONS", Color.Parse("#9B7CFF")),
        [CategoryRole.Routine] = ("ROUTINES", Color.Parse("#3CC8B4")),
    };

    private readonly List<(OrbitBody Body, Point Center, double Radius)> _placed = [];
    private Guid? _hover;
    private Point _pointer;

    public OrbitCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    // --- Layout -------------------------------------------------------------------------------------

    private (Point Center, double R) Frame() =>
        (new Point(Bounds.Width / 2, Bounds.Height / 2), Math.Max(40, Math.Min(Bounds.Height / 2 - 30, Bounds.Width / 2 - 20)));

    private void Layout()
    {
        _placed.Clear();
        if (Bodies is not { Count: > 0 } bodies)
            return;
        var (center, r) = Frame();

        foreach (var cell in bodies.GroupBy(b => (b.Role, b.Horizon)))
        {
            var ordered = cell.OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList();
            var ring = Array.IndexOf(Rings, cell.Key.Horizon);
            var radius = r * RingFractions[ring];
            var start = SectorStart[cell.Key.Role] + SectorPad;
            var span = SectorSpan - 2 * SectorPad;
            for (var i = 0; i < ordered.Count; i++)
            {
                var angle = start + (i + 0.5) / ordered.Count * span;
                var rad = angle * Math.PI / 180;
                var p = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
                _placed.Add((ordered[i], p, BodyRadius(ordered[i])));
            }
        }
    }

    private static double BodyRadius(OrbitBody b) => 7 + Math.Clamp(b.Pressure, 0, 1) * 8;

    // --- Pointer ------------------------------------------------------------------------------------

    private Guid? HitTest(Point p)
    {
        Guid? hit = null;
        var best = double.MaxValue;
        foreach (var (body, c, radius) in _placed)
        {
            var d = Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y));
            if (d <= radius + 7 && d < best)
            {
                best = d;
                hit = body.Id;
            }
        }
        return hit;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        var hit = HitTest(_pointer);
        Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (hit != _hover)
        {
            _hover = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is not null)
        {
            _hover = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTest(e.GetPosition(this)) is { } id && BodyCommand?.CanExecute(id) == true)
            BodyCommand.Execute(id);
    }

    // --- Drawing ------------------------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // so the whole area takes pointer input
        var (center, r) = Frame();
        Layout();

        var ringPen = new Pen(new SolidColorBrush(Color.Parse("#306B7CFF")), 1);
        var spokePen = new Pen(new SolidColorBrush(Color.Parse("#186B7CFF")), 1);

        foreach (var (role, start) in SectorStart)
        {
            var rad = start * Math.PI / 180;
            context.DrawLine(spokePen,
                new Point(center.X + r * 0.12 * Math.Cos(rad), center.Y + r * 0.12 * Math.Sin(rad)),
                new Point(center.X + r * 1.02 * Math.Cos(rad), center.Y + r * 1.02 * Math.Sin(rad)));
        }

        for (var i = 0; i < Rings.Length; i++)
        {
            var radius = r * RingFractions[i];
            context.DrawEllipse(null, ringPen, center, radius, radius);
            // Ring names sit on the spoke that points straight down, which no body is allowed to touch.
            DrawText(context, Horizons.Label(Rings[i]), 9, "#7A83B8", new Point(center.X, center.Y + radius), centered: true, background: true);
        }

        foreach (var (role, (name, color)) in Buckets)
        {
            var mid = (SectorStart[role] + SectorSpan / 2) * Math.PI / 180;
            var p = new Point(center.X + (r * 1.0 + 20) * Math.Cos(mid), center.Y + (r * 1.0 + 16) * Math.Sin(mid));
            DrawText(context, name, 10, color.ToString(), p, centered: true, bold: true);
        }

        // The core: today.
        var glow = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.Parse("#66E8B64C"), 0), new GradientStop(Color.Parse("#00E8B64C"), 1) },
        };
        context.DrawEllipse(glow, null, center, r * 0.12, r * 0.12);
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#E8B64C")), null, center, 5, 5);

        var labelRects = new List<Rect>();
        var order = _placed.OrderByDescending(x => x.Body.Id == _hover ? 2 : x.Body.Id == SelectedId ? 1 : 0)
            .ThenByDescending(x => x.Body.Pressure).ToList();

        // Bodies first, so labels never sit under one.
        foreach (var (body, c, radius) in _placed)
            DrawBody(context, body, c, radius);

        foreach (var (body, c, radius) in order)
        {
            var forced = body.Id == _hover || body.Id == SelectedId;
            var rect = PlaceLabel(body, c, radius, center, labelRects, forced, out var text);
            if (rect is { } rr)
            {
                labelRects.Add(rr);
                context.DrawText(text!, rr.TopLeft);
            }
        }

        if (_hover is { } hid && _placed.FirstOrDefault(x => x.Body.Id == hid) is { Body: not null } hovered)
            DrawTooltip(context, hovered.Body, hovered.Center, hovered.Radius);
    }

    private void DrawBody(DrawingContext context, OrbitBody body, Point c, double radius)
    {
        var accent = body.Accent;
        var hot = body.Pressure >= Planner.MustDo;
        var hovered = body.Id == _hover;

        if (hot || hovered)
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B)), null, c, radius + 6, radius + 6);

        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xE6, accent.R, accent.G, accent.B)), null, c, radius, radius);

        if (body.Fraction is { } fraction)
        {
            var arcR = radius + 4;
            var track = new Pen(new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), 2);
            context.DrawEllipse(null, track, c, arcR, arcR);
            if (fraction > 0.01)
            {
                var pen = new Pen(new SolidColorBrush(accent), 2.5, lineCap: PenLineCap.Round);
                if (fraction >= 0.999)
                    context.DrawEllipse(null, pen, c, arcR, arcR);
                else
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(Pt(c, arcR, -90), false);
                        ctx.ArcTo(Pt(c, arcR, -90 + fraction * 360), new Size(arcR, arcR), 0, fraction > 0.5, SweepDirection.Clockwise);
                        ctx.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, g);
                }
            }
        }

        if (body.Id == SelectedId)
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#E8B64C")), 1.6), c, radius + 9, radius + 9);
    }

    /// <summary>
    /// Tries right, left, below, above in turn and takes the first spot that touches no other label or body. A
    /// label that fits nowhere is left out until you hover it, which beats a pile of overlapping text.
    /// </summary>
    private Rect? PlaceLabel(OrbitBody body, Point c, double radius, Point center, List<Rect> taken, bool forced, out FormattedText? text)
    {
        var maxWidth = Math.Min(170, Math.Max(80, Bounds.Width / 2 - 24));
        var ft = Format(body.Title, 11.5, forced ? "#E8B64C" : "#E4E7F6", maxWidth, bold: forced);
        text = ft;
        var w = ft.Width;
        var h = ft.Height;
        var gap = radius + 8;
        var rightFirst = c.X >= center.X - 2;

        var spots = new List<Rect>
        {
            new(c.X + gap, c.Y - h / 2, w, h),
            new(c.X - gap - w, c.Y - h / 2, w, h),
            new(c.X - w / 2, c.Y + gap, w, h),
            new(c.X - w / 2, c.Y - gap - h, w, h),
        };
        if (!rightFirst)
            (spots[0], spots[1]) = (spots[1], spots[0]);

        var area = new Rect(2, 2, Bounds.Width - 4, Bounds.Height - 4);
        foreach (var spot in spots)
        {
            var padded = spot.Inflate(2);
            if (!area.Contains(spot.TopLeft) || !area.Contains(spot.BottomRight))
                continue;
            if (taken.Any(t => t.Intersects(padded)))
                continue;
            // Its own body is skipped: the gap is built to just clear it, and rounding must not reject the spot.
            if (_placed.Any(p => p.Body.Id != body.Id && Intersects(padded, p.Center, p.Radius + 6)))
                continue;
            return spot;
        }
        return forced ? spots[0] : null;
    }

    private static bool Intersects(Rect rect, Point c, double radius)
    {
        var x = Math.Clamp(c.X, rect.Left, rect.Right);
        var y = Math.Clamp(c.Y, rect.Top, rect.Bottom);
        var dx = c.X - x;
        var dy = c.Y - y;
        return dx * dx + dy * dy < radius * radius;
    }

    private void DrawTooltip(DrawingContext context, OrbitBody body, Point c, double radius)
    {
        var title = Format(body.Title, 12.5, "#F2F4FF", 260, bold: true);
        var line1 = Format(body.Line1, 11, "#9AA3D0", 260);
        var line2 = Format(body.Line2, 11, "#E8B64C", 260);
        var w = Math.Max(title.Width, Math.Max(line1.Width, line2.Width)) + 24;
        var h = title.Height + line1.Height + line2.Height + 22;

        var x = Math.Clamp(c.X + radius + 14, 6, Math.Max(6, Bounds.Width - w - 6));
        var y = Math.Clamp(c.Y - h - radius - 10, 6, Math.Max(6, Bounds.Height - h - 6));
        if (c.Y - h - radius - 10 < 6)
            y = Math.Clamp(c.Y + radius + 12, 6, Math.Max(6, Bounds.Height - h - 6));

        var rect = new Rect(x, y, w, h);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#F214172B")), new Pen(new SolidColorBrush(Color.Parse("#556B7CFF")), 1), rect, 9, 9);
        context.DrawText(title, new Point(x + 12, y + 8));
        context.DrawText(line1, new Point(x + 12, y + 10 + title.Height));
        context.DrawText(line2, new Point(x + 12, y + 12 + title.Height + line1.Height));
    }

    private static void DrawText(DrawingContext context, string text, double size, string color, Point at,
        bool centered = false, bool bold = false, bool background = false)
    {
        var ft = Format(text, size, color, 400, bold);
        var origin = centered ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at;
        if (background)
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#E50B0D1A")), null,
                new Rect(origin.X - 4, origin.Y - 1, ft.Width + 8, ft.Height + 2), 4, 4);
        context.DrawText(ft, origin);
    }

    private static FormattedText Format(string text, double size, string color, double maxWidth, bool bold = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal),
            size, new SolidColorBrush(Color.Parse(color)))
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    private static Point Pt(Point c, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
