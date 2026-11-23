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

    public static readonly StyledProperty<int> CoreDoneProperty =
        AvaloniaProperty.Register<OrbitCanvas, int>(nameof(CoreDone));

    static OrbitCanvas() => AffectsRender<OrbitCanvas>(BodiesProperty, SelectedIdProperty, CoreCountProperty, CoreDoneProperty);

    /// <summary>How many things have already been worked on today.</summary>
    public int CoreDone { get => GetValue(CoreDoneProperty); set => SetValue(CoreDoneProperty, value); }

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
        [CategoryRole.Obligation] = ("OBLIGATIONS", Color.Parse("#E0896C")),
        [CategoryRole.Passion] = ("PASSIONS", Color.Parse("#9F8FE6")),
        [CategoryRole.Routine] = ("ROUTINES", Color.Parse("#62B8A9")),
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

    private static readonly Color Brass = Color.Parse("#E8B64C");
    private static readonly Color Ink = Color.Parse("#7C8CFF");

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // so the whole area takes pointer input
        var (center, r) = Frame();
        Layout();

        DrawStars(context);

        // Sectors: the faintest wash of each bucket's colour, edged by a hairline. Territory, not decoration.
        foreach (var (role, (_, color)) in Buckets)
        {
            var a0 = SectorStart[role];
            var a1 = SectorStart[role] + SectorSpan;
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x0B, color.R, color.G, color.B)), null,
                Wedge(center, r * RingFractions[0] * 0.55, r, a0, a1));
            var edge = new Pen(new SolidColorBrush(Color.FromArgb(0x30, Ink.R, Ink.G, Ink.B)), 1);
            context.DrawLine(edge, Pt(center, r * RingFractions[0] * 0.55, a0), Pt(center, r + 6, a0));
        }

        // Rings: hairlines only. The scale is the information; nothing is filled in.
        var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(0x3A, Ink.R, Ink.G, Ink.B)), 1);
        for (var i = 0; i < Rings.Length; i++)
        {
            var radius = r * RingFractions[i];
            context.DrawEllipse(null, ringPen, center, radius, radius);
            DrawTicks(context, center, radius, i);
        }

        // Each ring is named once, on the downward spoke where no planet is allowed to sit.
        for (var i = 0; i < Rings.Length; i++)
            DrawText(context, Horizons.Label(Rings[i]).ToUpperInvariant(), 8, "#7F88BC",
                new Point(center.X, center.Y + r * RingFractions[i]), centered: true, background: true);

        // Bucket names sit on the rim: quiet text, with the bucket's colour only as a small dot.
        foreach (var (role, (name, color)) in Buckets)
        {
            var mid = (SectorStart[role] + SectorSpan / 2) * Math.PI / 180;
            var p = new Point(center.X + (r + 22) * Math.Cos(mid), center.Y + (r + 18) * Math.Sin(mid));
            var label = Format(name, 9.5, "#A3AAD2", 200, bold: true);
            context.DrawEllipse(new SolidColorBrush(color), null, new Point(p.X - label.Width / 2 - 8, p.Y), 3, 3);
            context.DrawText(label, new Point(p.X - label.Width / 2, p.Y - label.Height / 2));
        }

        DrawLinks(context, center);
        DrawCore(context, center, r);

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

    /// <summary>
    /// The middle of the map is today, drawn as a dial rather than a sun. The big number is what is still to do.
    /// The brass arc is how much of today's list is already done, and the ring of hour marks around it is the
    /// waking day (07:00 to 23:00) with a notch at the current time, so "how much is left" and "how much day
    /// is left" sit side by side.
    /// </summary>
    private void DrawCore(DrawingContext context, Point center, double r)
    {
        var face = Math.Clamp(r * 0.135, 36, 56);
        var brass = new SolidColorBrush(Brass);

        // The corona: a wide, soft warmth that breathes very slowly, and a few hairline rays. It is the one warm
        // thing on the map, so the eye goes to "today" first.
        var breathe = 0.5 + 0.5 * Math.Sin(Phase * 0.8);
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(0x46 + 0x14 * breathe), Brass.R, Brass.G, Brass.B), 0.3),
                new GradientStop(Color.FromArgb(0x14, Brass.R, Brass.G, Brass.B), 0.62),
                new GradientStop(Color.FromArgb(0x00, Brass.R, Brass.G, Brass.B), 1),
            },
        }, null, center, face * 3.4, face * 3.4);
        var ray = new Pen(new SolidColorBrush(Color.FromArgb(0x26, Brass.R, Brass.G, Brass.B)), 1);
        for (var i = 0; i < 24; i++)
        {
            var a = i * 15 + Phase * 1.5;
            var len = i % 2 == 0 ? 1.9 : 1.5;
            context.DrawLine(ray, Pt(center, face + 16, a), Pt(center, face * len + 16, a));
        }
        var total = CoreCount + CoreDone;
        var done = total == 0 ? 1.0 : (double)CoreDone / total;

        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.Parse("#2C2210"), 0), new GradientStop(Color.Parse("#0F1226"), 1) },
        }, new Pen(new SolidColorBrush(Color.FromArgb(0x90, Brass.R, Brass.G, Brass.B)), 1.2), center, face, face);

        // Progress: a thin track with the finished part in brass.
        var arcR = face - 5;
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x2A, Brass.R, Brass.G, Brass.B)), 2), center, arcR, arcR);
        if (done >= 0.999)
            context.DrawEllipse(null, new Pen(brass, 2), center, arcR, arcR);
        else if (done > 0.01)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(Pt(center, arcR, -90), false);
                ctx.ArcTo(Pt(center, arcR, -90 + done * 360), new Size(arcR, arcR), 0, done > 0.5, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(brass, 2, lineCap: PenLineCap.Round), g);
        }

        // The day: sixteen hour marks outside the face; the current hour is a longer brass notch.
        var now = DateTime.Now;
        var hourNow = Math.Clamp(now.Hour + now.Minute / 60.0 - 7, 0, 16);
        var dayPen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, Ink.R, Ink.G, Ink.B)), 1);
        for (var h = 0; h < 16; h++)
        {
            var a = -90 + h * 360.0 / 16;
            context.DrawLine(dayPen, Pt(center, face + 5, a), Pt(center, face + (h % 4 == 0 ? 11 : 8), a));
        }
        var notch = -90 + hourNow * 360.0 / 16;
        context.DrawLine(new Pen(brass, 2, lineCap: PenLineCap.Round), Pt(center, face + 4, notch), Pt(center, face + 13, notch));

        var left = CoreCount.ToString(CultureInfo.InvariantCulture);
        if (CoreCount == 0 && total > 0)
        {
            DrawText(context, "Clear", 13, "#E8B64C", new Point(center.X, center.Y - 3), centered: true, bold: true);
            DrawText(context, "FOR TODAY", 7.5, "#8B93C4", new Point(center.X, center.Y + 10), centered: true);
        }
        else
        {
            DrawText(context, left, 28, "#F2F4FF", new Point(center.X, center.Y - 5), centered: true, bold: true);
            DrawText(context, "LEFT TODAY", 7.5, "#8B93C4", new Point(center.X, center.Y + 15), centered: true);
        }
    }

    /// <summary>A fixed, seeded scatter of stars with a slow twinkle. A few have a tiny cross, so it reads as sky.</summary>
    private void DrawStars(DrawingContext context)
    {
        var rng = new Random(11);
        for (var i = 0; i < 130; i++)
        {
            var x = rng.NextDouble() * Bounds.Width;
            var y = rng.NextDouble() * Bounds.Height;
            var size = 0.5 + rng.NextDouble() * 1.0;
            var twinkle = 0.65 + 0.35 * Math.Sin(Phase * (0.4 + rng.NextDouble() * 0.6) + i);
            var alpha = (byte)(0x2A + rng.NextDouble() * 0x40 * twinkle);
            var warm = i % 9 == 0;
            var color = warm ? Color.FromArgb(alpha, 0xFF, 0xE2, 0xA8) : Color.FromArgb(alpha, 0xC8, 0xD0, 0xFF);
            context.DrawEllipse(new SolidColorBrush(color), null, new Point(x, y), size, size);
            if (i % 17 == 0)
            {
                var glint = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha / 2), color.R, color.G, color.B)), 0.8);
                context.DrawLine(glint, new Point(x - 4, y), new Point(x + 4, y));
                context.DrawLine(glint, new Point(x, y - 4), new Point(x, y + 4));
            }
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
        var done = body.Fraction is >= 0.999;

        // Pressing goals get a close, quiet halo; there is no glow on anything that is not asking for you.
        if (hot || hovered)
        {
            var breathe = hovered ? 1 : 0.5 + 0.5 * Math.Sin(Phase * 1.8 + c.X * 0.01);
            var halo = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(0x34 + 0x1C * breathe), accent.R, accent.G, accent.B), 0.55),
                    new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1),
                },
            };
            context.DrawEllipse(halo, null, c, radius + 11, radius + 11);
        }

        // A matte disc with a lit upper edge: enough to feel solid, not enough to feel like a toy.
        var disc = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
            RadiusX = RelativeScalar.Parse("75%"),
            RadiusY = RelativeScalar.Parse("75%"),
            GradientStops =
            {
                new GradientStop(Lighten(accent, 0.5), 0),
                new GradientStop(accent, 0.5),
                new GradientStop(Darken(accent, 0.55), 1),
            },
        };
        context.DrawEllipse(disc, new Pen(new SolidColorBrush(Darken(accent, 0.5)), 1), c, radius, radius);

        // A small specular glint: the one detail that makes a flat disc read as a sphere.
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), null,
            new Point(c.X - radius * 0.35, c.Y - radius * 0.38), radius * 0.2, radius * 0.14);

        if (done)
            DrawText(context, "✓", radius * 1.1, "#0B0D1A", c, centered: true, bold: true);

        // Progress is the hairline orbit around the planet, filled clockwise from the top.
        if (body.Fraction is { } fraction)
        {
            var arcR = radius + 5;
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)), 1.5), c, arcR, arcR);
            if (fraction > 0.01)
            {
                var pen = new Pen(new SolidColorBrush(Lighten(accent, 0.2)), 2, lineCap: PenLineCap.Round);
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

        // Moons: one per section (or step). Filled = finished, open ring = ready, dotted = waiting on something.
        if (body.Moons.Count > 0)
        {
            var orbit = radius + 16;
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x1E, accent.R, accent.G, accent.B)), 1), c, orbit, orbit);
            var spin = Phase * 4;
            for (var i = 0; i < body.Moons.Count; i++)
            {
                var at = Pt(c, orbit, spin + i * 360.0 / body.Moons.Count);
                switch (body.Moons[i])
                {
                    case MoonState.Done:
                        context.DrawEllipse(new SolidColorBrush(accent), null, at, 3.4, 3.4);
                        break;
                    case MoonState.Open:
                        context.DrawEllipse(new SolidColorBrush(Color.Parse("#0B0D1A")),
                            new Pen(new SolidColorBrush(Lighten(accent, 0.15)), 1.4), at, 3.2, 3.2);
                        break;
                    default:
                        context.DrawEllipse(null,
                            new Pen(new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B)), 1, new DashStyle([1.2, 1.4], 0)), at, 3.2, 3.2);
                        break;
                }
            }
        }

        if (body.Id == SelectedId)
        {
            var extent = radius + (body.Moons.Count > 0 ? 25 : 11);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Brass), 1.4), c, extent, extent);
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
        var title = Format(body.Title, 13, "#F2F4FF", 280, bold: true);
        var line1 = Format(body.Line1, 11, "#8B93C4", 280);
        var line2 = Format(body.Line2, 11.5, "#E8B64C", 280);
        var w = Math.Max(title.Width, Math.Max(line1.Width, line2.Width)) + 34;
        var h = title.Height + line1.Height + line2.Height + 26;

        var x = Math.Clamp(c.X + radius + 22, 6, Math.Max(6, Bounds.Width - w - 6));
        var y = c.Y - h - radius - 14;
        if (y < 6)
            y = c.Y + radius + 16;
        y = Math.Clamp(y, 6, Math.Max(6, Bounds.Height - h - 6));

        var rect = new Rect(x, y, w, h);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#F2121630")), new Pen(new SolidColorBrush(Color.Parse("#40C0C8FF")), 1), rect, 10, 10);
        // The goal's colour as a tab on the left, the same way it appears on the dependency graph.
        context.DrawRectangle(new SolidColorBrush(body.Accent), null, new Rect(x + 1, y + 12, 3.5, h - 24), 2, 2);
        context.DrawText(title, new Point(x + 18, y + 10));
        context.DrawText(line1, new Point(x + 18, y + 12 + title.Height));
        context.DrawText(line2, new Point(x + 18, y + 15 + title.Height + line1.Height));
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
