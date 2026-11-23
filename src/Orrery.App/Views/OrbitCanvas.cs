using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
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

    public static readonly StyledProperty<int> CoreCountProperty =
        AvaloniaProperty.Register<OrbitCanvas, int>(nameof(CoreCount));

    static OrbitCanvas() => AffectsRender<OrbitCanvas>(BodiesProperty, SelectedIdProperty, CoreCountProperty);

    /// <summary>How many things are on today's list, shown in the core.</summary>
    public int CoreCount { get => GetValue(CoreCountProperty); set => SetValue(CoreCountProperty, value); }

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

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    public OrbitCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        // A slow drift: pressing goals breathe, moons turn. Nothing moves that you would have to chase.
        _timer.Tick += (_, _) =>
        {
            if (IsEffectivelyVisible)
                InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private double Phase => _clock.Elapsed.TotalSeconds;

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

    private static double BodyRadius(OrbitBody b) => 11 + Math.Clamp(b.Pressure, 0, 1) * 9;

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

        DrawStars(context);

        // Sectors: a faint wash of each bucket's colour, so the map reads as three territories.
        foreach (var (role, (_, color)) in Buckets)
        {
            var a0 = SectorStart[role] + 1.5;
            var a1 = SectorStart[role] + SectorSpan - 1.5;
            var mid = (a0 + a1) / 2 * Math.PI / 180;
            var wash = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(Math.Cos(mid) * 0.5 + 0.5, Math.Sin(mid) * 0.5 + 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 0),
                    new GradientStop(Color.FromArgb(0x1C, color.R, color.G, color.B), 1),
                },
            };
            context.DrawGeometry(wash, null, Wedge(center, r * 0.14, r * 1.0, a0, a1));
        }

        // Rings: layered discs give a gentle well toward the middle, with a crisp line on each.
        var ringPen = new Pen(new SolidColorBrush(Color.Parse("#2E7C8CFF")), 1);
        for (var i = Rings.Length - 1; i >= 0; i--)
        {
            var radius = r * RingFractions[i];
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#0A7C8CFF")), ringPen, center, radius, radius);
            DrawTicks(context, center, radius, i);
        }
        for (var i = 0; i < Rings.Length; i++)
            DrawText(context, Horizons.Label(Rings[i]).ToUpperInvariant(), 8.5, "#7A83B8",
                new Point(center.X, center.Y + r * RingFractions[i]), centered: true, background: true);

        foreach (var (role, (name, color)) in Buckets)
        {
            var mid = (SectorStart[role] + SectorSpan / 2) * Math.PI / 180;
            var p = new Point(center.X + (r + 20) * Math.Cos(mid), center.Y + (r + 16) * Math.Sin(mid));
            DrawText(context, name, 10, color.ToString(), p, centered: true, bold: true);
        }

        DrawLinks(context, center);

        // The core: today.
        var pulse = 0.5 + 0.5 * Math.Sin(Phase * 1.6);
        var coreR = Math.Clamp(r * 0.07, 14, 24);
        var glow = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(0x70 + 0x20 * pulse), 0xE8, 0xB6, 0x4C), 0),
                new GradientStop(Color.FromArgb(0x00, 0xE8, 0xB6, 0x4C), 1),
            },
        };
        context.DrawEllipse(glow, null, center, coreR * 2.6, coreR * 2.6);
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#E8B64C")), null, center, coreR, coreR);
        DrawText(context, CoreCount.ToString(CultureInfo.InvariantCulture), 15, "#1B1407", new Point(center.X, center.Y - 3), centered: true, bold: true);
        DrawText(context, "TODAY", 7, "#1B1407", new Point(center.X, center.Y + 9), centered: true, bold: true);

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

    /// <summary>A fixed scatter of faint points. Seeded, so it never shimmers or moves.</summary>
    private void DrawStars(DrawingContext context)
    {
        var rng = new Random(7);
        var dot = new SolidColorBrush(Color.Parse("#14B4BEFF"));
        for (var i = 0; i < 90; i++)
        {
            var x = rng.NextDouble() * Bounds.Width;
            var y = rng.NextDouble() * Bounds.Height;
            var size = 0.6 + rng.NextDouble() * 1.1;
            context.DrawEllipse(dot, null, new Point(x, y), size, size);
        }
    }

    private static void DrawTicks(DrawingContext context, Point center, double radius, int ring)
    {
        // Ticks on a ring mark days (inner ring: one per day of the week) so the rings feel like a scale, not decoration.
        var count = ring switch { 0 => 7, 1 => 28, 2 => 30, _ => 0 };
        if (count == 0)
            return;
        var pen = new Pen(new SolidColorBrush(Color.Parse("#287C8CFF")), 1);
        for (var i = 0; i < count; i++)
        {
            var a = i * 360.0 / count;
            context.DrawLine(pen, Pt(center, radius - 3, a), Pt(center, radius + 3, a));
        }
    }

    private static Geometry Wedge(Point c, double inner, double outer, double a0, double a1)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Pt(c, outer, a0), true);
            ctx.ArcTo(Pt(c, outer, a1), new Size(outer, outer), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(Pt(c, inner, a1));
            ctx.ArcTo(Pt(c, inner, a0), new Size(inner, inner), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return g;
    }

    /// <summary>Goal-level dependencies as arcs from what must finish to what waits. Faint until you point at one.</summary>
    private void DrawLinks(DrawingContext context, Point center)
    {
        foreach (var (body, to, toRadius) in _placed)
        {
            foreach (var depId in body.DependsOn)
            {
                if (_placed.FirstOrDefault(p => p.Body.Id == depId) is not { Body: not null } dep)
                    continue;
                var emphasised = body.Id == _hover || body.Id == SelectedId || dep.Body.Id == _hover || dep.Body.Id == SelectedId;
                var color = emphasised ? Color.Parse("#E8B64C") : Color.Parse("#7C8CFF");
                var brush = new SolidColorBrush(Color.FromArgb((byte)(emphasised ? 0xC8 : 0x40), color.R, color.G, color.B));
                var pen = new Pen(brush, emphasised ? 1.6 : 1, new DashStyle([3, 3], 0));

                var from = dep.Center;
                var mid = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
                var control = new Point(mid.X + (center.X - mid.X) * 0.35, mid.Y + (center.Y - mid.Y) * 0.35);
                var dx = to.X - control.X;
                var dy = to.Y - control.Y;
                var len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                var end = new Point(to.X - dx / len * (toRadius + 4), to.Y - dy / len * (toRadius + 4));

                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(from, false);
                    ctx.QuadraticBezierTo(control, end);
                    ctx.EndFigure(false);
                }
                context.DrawGeometry(null, pen, g);

                // An arrowhead, so the direction (this must finish first) is never a guess.
                var ux = dx / len;
                var uy = dy / len;
                var head = new StreamGeometry();
                using (var ctx = head.Open())
                {
                    ctx.BeginFigure(end, true);
                    ctx.LineTo(new Point(end.X - ux * 7 - uy * 3.5, end.Y - uy * 7 + ux * 3.5));
                    ctx.LineTo(new Point(end.X - ux * 7 + uy * 3.5, end.Y - uy * 7 - ux * 3.5));
                    ctx.EndFigure(true);
                }
                context.DrawGeometry(brush, null, head);
            }
        }
    }

    private void DrawBody(DrawingContext context, OrbitBody body, Point c, double radius)
    {
        var accent = body.Accent;
        var hot = body.Pressure >= Planner.MustDo;
        var hovered = body.Id == _hover;

        if (hot || hovered)
        {
            var breathe = hovered ? 1 : 0.5 + 0.5 * Math.Sin(Phase * 2.2 + c.X * 0.01);
            var halo = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(0x60 + 0x30 * breathe), accent.R, accent.G, accent.B), 0.3),
                    new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1),
                },
            };
            context.DrawEllipse(halo, null, c, radius + 14 + 3 * breathe, radius + 14 + 3 * breathe);
        }

        // A lit sphere rather than a flat dot: highlight up and to the left, shade toward the far edge.
        var sphere = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.32, 0.28, RelativeUnit.Relative),
            RadiusX = RelativeScalar.Parse("62%"),
            RadiusY = RelativeScalar.Parse("62%"),
            GradientStops =
            {
                new GradientStop(Lighten(accent, 0.55), 0),
                new GradientStop(accent, 0.5),
                new GradientStop(Darken(accent, 0.45), 1),
            },
        };
        context.DrawEllipse(sphere, null, c, radius, radius);

        if (body.Fraction is { } fraction)
        {
            var arcR = radius + 4;
            var track = new Pen(new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), 2);
            context.DrawEllipse(null, track, c, arcR, arcR);
            if (fraction > 0.01)
            {
                var pen = new Pen(new SolidColorBrush(Lighten(accent, 0.25)), 2.5, lineCap: PenLineCap.Round);
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

        // Moons: one per section (or step). Solid = finished, ring = open, dim = waiting on something.
        if (body.Moons.Count > 0)
        {
            var orbit = radius + 15;
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)), 1), c, orbit, orbit);
            var spin = Phase * 7;
            for (var i = 0; i < body.Moons.Count; i++)
            {
                var at = Pt(c, orbit, spin + i * 360.0 / body.Moons.Count);
                switch (body.Moons[i])
                {
                    case MoonState.Done:
                        context.DrawEllipse(new SolidColorBrush(accent), null, at, 3.6, 3.6);
                        break;
                    case MoonState.Open:
                        context.DrawEllipse(new SolidColorBrush(Color.Parse("#0B0D1A")), new Pen(new SolidColorBrush(Lighten(accent, 0.2)), 1.5), at, 3.4, 3.4);
                        break;
                    default:
                        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x70, accent.R, accent.G, accent.B)), 1), at, 3.2, 3.2);
                        break;
                }
            }
        }

        if (body.Id == SelectedId)
        {
            var extent = radius + (body.Moons.Count > 0 ? 24 : 10);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#E8B64C")), 1.6), c, extent, extent);
        }
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    /// <summary>
    /// Tries right, left, below, above in turn and takes the first spot that touches no other label or body. A
    /// label that fits nowhere is left out until you hover it, which beats a pile of overlapping text.
    /// </summary>
    private Rect? PlaceLabel(OrbitBody body, Point c, double radius, Point center, List<Rect> taken, bool forced, out FormattedText? text)
    {
        var maxWidth = Math.Min(170, Math.Max(80, Bounds.Width / 2 - 24));
        var ft = Format(body.Title, 12.5, forced ? "#E8B64C" : "#E4E7F6", maxWidth, bold: forced);
        text = ft;
        var w = ft.Width;
        var h = ft.Height;
        var gap = radius + (body.Moons.Count > 0 ? 25 : 9);
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
