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
/// The orbit map, seen at a slant. The rings are how soon a goal will want you (inner = now, outer = someday) and the
/// three sectors are the buckets, so how lopsided your life is shows at a glance. A planet's size is its pressure and
/// the arc around it is progress. Each planet is lit by the sun in the middle, so the side facing today is bright.
/// Positions depend only on bucket, ring and name, so nothing jumps between visits.
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

    /// <summary>The map is a disc seen from above and to the side: flat circles become ellipses this tall.</summary>
    private const double Tilt = 0.68;

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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    public OrbitCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        // A slow drift: planets creep along their rings, moons turn, the sun breathes. Nothing you would have to chase.
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

    /// <summary>The map's middle and its radius along the long axis. The slant means the width does most of the work.</summary>
    private (Point Center, double R) Frame() =>
        (new Point(Bounds.Width / 2, Bounds.Height / 2 + 4),
         Math.Max(60, Math.Min(Bounds.Width / 2 - 78, (Bounds.Height / 2 - 40) / Tilt)));

    /// <summary>A point on the slanted disc: a radius and an angle in degrees.</summary>
    private static Point Slant(Point c, double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + radius * Math.Cos(rad), c.Y + Tilt * radius * Math.Sin(rad));
    }

    /// <summary>Draws flat circles and wedges as if lying on the disc: scales everything about the middle.</summary>
    private static Matrix Flatten(Point c) =>
        Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(1, Tilt) * Matrix.CreateTranslation(c.X, c.Y);

    private static double Unit(Guid id)
    {
        var h = (uint)id.GetHashCode();
        return h % 1000 / 1000.0;
    }

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
                // A few degrees of slow drift each way, so the map is alive without anything ever moving far.
                var drift = 2.4 * Math.Sin(Phase * 0.11 + Unit(ordered[i].Id) * 6.28);
                var angle = start + (i + 0.5) / ordered.Count * span + drift;
                var p = Slant(center, radius, angle);
                // Nearer (lower on screen) is a little bigger, farther a little smaller.
                var depth = 1 + 0.16 * Math.Clamp((p.Y - center.Y) / (r * Tilt), -1, 1);
                _placed.Add((ordered[i], p, BodyRadius(ordered[i]) * depth));
            }
        }
    }

    private static double BodyRadius(OrbitBody b) => 16 + Math.Clamp(b.Pressure, 0, 1) * 14;

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
        _hover = hit;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
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

    private static Color A(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
    private static SolidColorBrush Brush(Color c, double alpha = 1) => new(A(c, alpha));

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // so the whole area takes pointer input
        var (center, r) = Frame();
        Layout();

        DrawNebula(context, center, r);
        DrawStars(context);
        DrawDisc(context, center, r);

        // Bucket names sit on the rim: quiet text, with the bucket's colour only as a small dot.
        foreach (var (role, (name, color)) in Buckets)
        {
            var p = Slant(center, r + 30, SectorStart[role] + SectorSpan / 2);
            var label = Format(name, 9.5, "#A3AAD2", 200, bold: true);
            context.DrawEllipse(new SolidColorBrush(color), null, new Point(p.X - label.Width / 2 - 9, p.Y), 3, 3);
            context.DrawText(label, new Point(p.X - label.Width / 2, p.Y - label.Height / 2));
        }

        DrawLinks(context, center);

        // Painter's order: what is behind the sun first, then the sun, then what is in front of it.
        var behind = _placed.Where(x => x.Center.Y < center.Y).OrderBy(x => x.Center.Y).ToList();
        var front = _placed.Where(x => x.Center.Y >= center.Y).OrderBy(x => x.Center.Y).ToList();
        foreach (var (body, c, radius) in behind)
            DrawBody(context, body, c, radius, center);
        DrawSun(context, center, r);
        foreach (var (body, c, radius) in front)
            DrawBody(context, body, c, radius, center);

        var labelRects = new List<Rect>();
        var order = _placed.OrderByDescending(x => x.Body.Id == _hover ? 2 : x.Body.Id == SelectedId ? 1 : 0)
            .ThenByDescending(x => x.Body.Pressure).ToList();
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

    // --- Background ---------------------------------------------------------------------------------

    /// <summary>Far-off colour: a faint cloud of each bucket's hue behind its own side of the map.</summary>
    private void DrawNebula(DrawingContext context, Point center, double r)
    {
        void Cloud(Point at, double radius, Color color, double alpha) =>
            context.DrawEllipse(new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(A(color, alpha), 0),
                    new GradientStop(A(color, alpha * 0.35), 0.55),
                    new GradientStop(A(color, 0), 1),
                },
            }, null, at, radius, radius * 0.72);

        foreach (var (role, (_, color)) in Buckets)
            Cloud(Slant(center, r * 0.7, SectorStart[role] + SectorSpan / 2), r * 0.5, color, 0.10);
        Cloud(center, r * 0.92, Color.Parse("#3A3F9C"), 0.14);
        Cloud(center, r * 0.6, Brass, 0.07);
    }

    private sealed record Star(double X, double Y, double Size, double Alpha, int Layer, double Speed, bool Warm, bool Glint);
    private static readonly List<Star> Sky = MakeSky();

    private static List<Star> MakeSky()
    {
        var rng = new Random(11);
        var stars = new List<Star>();
        for (var i = 0; i < 300; i++)
        {
            var layer = i % 3; // 0 is far and still, 2 is near and drifts most
            stars.Add(new Star(rng.NextDouble(), rng.NextDouble(), 0.4 + rng.NextDouble() * (0.6 + layer * 0.5),
                0.16 + rng.NextDouble() * 0.5, layer, 0.4 + rng.NextDouble() * 0.7, i % 7 == 0, i % 31 == 0));
        }
        return stars;
    }

    /// <summary>Three layers of stars that slide a few pixels against each other as the pointer moves.</summary>
    private void DrawStars(DrawingContext context)
    {
        var dx = (_pointer.X - Bounds.Width / 2) / Math.Max(1, Bounds.Width);
        var dy = (_pointer.Y - Bounds.Height / 2) / Math.Max(1, Bounds.Height);
        var i = 0;
        foreach (var s in Sky)
        {
            var shift = (s.Layer - 1) * 9;
            var x = s.X * Bounds.Width - dx * shift;
            var y = s.Y * Bounds.Height - dy * shift;
            var twinkle = 0.65 + 0.35 * Math.Sin(Phase * s.Speed + i++);
            var color = s.Warm ? Color.Parse("#FFE2A8") : Color.Parse("#C8D0FF");
            context.DrawEllipse(Brush(color, s.Alpha * twinkle), null, new Point(x, y), s.Size, s.Size);
            if (s.Glint)
            {
                var pen = new Pen(Brush(color, s.Alpha * twinkle * 0.5), 0.8);
                context.DrawLine(pen, new Point(x - 5, y), new Point(x + 5, y));
                context.DrawLine(pen, new Point(x, y - 5), new Point(x, y + 5));
            }
        }
    }

    // --- The disc: sectors, rings, dust -------------------------------------------------------------

    private void DrawDisc(DrawingContext context, Point center, double r)
    {
        using (context.PushTransform(Flatten(center)))
        {
            // The plate everything sits on: a soft pool of light that fades toward the edge.
            context.DrawEllipse(new RadialGradientBrush
            {
                Center = new RelativePoint(center, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(center, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(r * 1.04, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(r * 1.04, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(A(Color.Parse("#2A2F6E"), 0.38), 0),
                    new GradientStop(A(Color.Parse("#1B1F4A"), 0.30), 0.55),
                    new GradientStop(A(Color.Parse("#10132E"), 0.10), 1),
                },
            }, new Pen(Brush(Ink, 0.22), 1.2), center, r * 1.04, r * 1.04);

            // Sectors: each bucket's territory, tinted from the sun outward.
            foreach (var (role, (_, color)) in Buckets)
            {
                var a0 = SectorStart[role];
                var a1 = a0 + SectorSpan;
                var wash = new RadialGradientBrush
                {
                    Center = new RelativePoint(center, RelativeUnit.Absolute),
                    GradientOrigin = new RelativePoint(center, RelativeUnit.Absolute),
                    RadiusX = new RelativeScalar(r, RelativeUnit.Absolute),
                    RadiusY = new RelativeScalar(r, RelativeUnit.Absolute),
                    GradientStops =
                    {
                        new GradientStop(A(color, 0.02), 0.15),
                        new GradientStop(A(color, 0.07), 0.6),
                        new GradientStop(A(color, 0.10), 1),
                    },
                };
                context.DrawGeometry(wash, null, Wedge(center, r * RingFractions[0] * 0.5, r * 1.04, a0, a1));
                var edge = new Pen(Brush(Ink, 0.22), 1);
                context.DrawLine(edge, PtFlat(center, r * RingFractions[0] * 0.5, a0), PtFlat(center, r * 1.04 + 6, a0));
            }

            // Rings: a soft underglow and a hairline, with a slow bright arc travelling each one.
            for (var i = 0; i < Rings.Length; i++)
            {
                var radius = r * RingFractions[i];
                context.DrawEllipse(null, new Pen(Brush(Ink, 0.06), 7), center, radius, radius);
                context.DrawEllipse(null, new Pen(Brush(Ink, 0.34), 1), center, radius, radius);
                DrawTicks(context, center, radius, i);
                DrawComet(context, center, radius, i);
            }

            // Dust on the outer ring, so the edge of the map reads as a belt rather than a line.
            var rng = new Random(5);
            for (var i = 0; i < 140; i++)
            {
                var a = rng.NextDouble() * 360 + Phase * 0.4;
                var off = (rng.NextDouble() - 0.5) * r * 0.05;
                var p = PtFlat(center, r * RingFractions[3] + off, a);
                context.DrawEllipse(Brush(Color.Parse("#B5BEFF"), 0.10 + rng.NextDouble() * 0.2), null, p, 0.5 + rng.NextDouble() * 0.8, 0.5 + rng.NextDouble() * 0.8);
            }
        }

        // Each ring is named once, on the downward spoke where no planet is allowed to sit.
        for (var i = 0; i < Rings.Length; i++)
            DrawText(context, Horizons.Label(Rings[i]).ToUpperInvariant(), 8, "#8E97CC",
                new Point(center.X, center.Y + Tilt * r * RingFractions[i]), centered: true, background: true);
    }

    /// <summary>A short fading streak that circles a ring, as if something were slowly passing.</summary>
    private void DrawComet(DrawingContext context, Point center, double radius, int ring)
    {
        var head = Phase * (9 - ring * 1.8) + ring * 70;
        const int steps = 14;
        for (var s = 0; s < steps; s++)
        {
            var a0 = head - s * 2.2;
            var a1 = head - (s + 1) * 2.2;
            var fade = 1 - (double)s / steps;
            context.DrawLine(new Pen(Brush(Color.Parse("#BFC8FF"), 0.5 * fade * fade), 1.4 * (0.5 + fade * 0.5), lineCap: PenLineCap.Round),
                PtFlat(center, radius, a0), PtFlat(center, radius, a1));
        }
    }

    private static void DrawTicks(DrawingContext context, Point center, double radius, int ring)
    {
        // Ticks on a ring mark days (inner ring: one per day of the week) so the rings feel like a scale, not decoration.
        var count = ring switch { 0 => 7, 1 => 28, 2 => 30, _ => 0 };
        if (count == 0)
            return;
        var pen = new Pen(Brush(Ink, 0.22), 1);
        for (var i = 0; i < count; i++)
        {
            var a = i * 360.0 / count;
            context.DrawLine(pen, PtFlat(center, radius - 3.5, a), PtFlat(center, radius + 3.5, a));
        }
    }

    private static Geometry Wedge(Point c, double inner, double outer, double a0, double a1)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(PtFlat(c, outer, a0), true);
            ctx.ArcTo(PtFlat(c, outer, a1), new Size(outer, outer), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(PtFlat(c, inner, a1));
            ctx.ArcTo(PtFlat(c, inner, a0), new Size(inner, inner), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return g;
    }

    // --- The sun ------------------------------------------------------------------------------------

    /// <summary>
    /// Today, as a star. The big number is what is still to do. A bright arc around it shows how much of today's list
    /// is done, and a slanted ring of sixteen marks is the waking day (07:00 to 23:00) with a bead where the time is now.
    /// </summary>
    private void DrawSun(DrawingContext context, Point center, double r)
    {
        var face = Math.Clamp(r * 0.12, 32, 52);
        var breathe = 0.5 + 0.5 * Math.Sin(Phase * 0.8);

        // Bloom: three widening layers of warmth.
        void Glow(double radius, Color color, double alpha) =>
            context.DrawEllipse(new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(A(color, alpha), 0.2),
                    new GradientStop(A(color, alpha * 0.4), 0.55),
                    new GradientStop(A(color, 0), 1),
                },
            }, null, center, radius, radius);
        Glow(face * 5.0, Color.Parse("#E8963C"), 0.16 + 0.04 * breathe);
        Glow(face * 3.0, Color.Parse("#F2B04C"), 0.28 + 0.06 * breathe);
        Glow(face * 1.9, Color.Parse("#FFD27A"), 0.42);

        // Streamers: long soft spikes that turn slowly, two sets against each other.
        for (var set = 0; set < 2; set++)
        {
            var count = set == 0 ? 11 : 7;
            for (var i = 0; i < count; i++)
            {
                var a = i * 360.0 / count + Phase * (set == 0 ? 2.2 : -3.4) + set * 17;
                var len = face * (set == 0 ? 2.5 : 1.9) * (0.85 + 0.15 * Math.Sin(Phase * 0.7 + i * 1.7));
                var half = set == 0 ? 3.2 : 4.4;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(Pt(center, face * 0.8, a - half), true);
                    ctx.LineTo(Pt(center, face + len, a));
                    ctx.LineTo(Pt(center, face * 0.8, a + half));
                    ctx.EndFigure(true);
                }
                context.DrawGeometry(Brush(Color.Parse("#FFD98A"), set == 0 ? 0.10 : 0.07), null, g);
            }
        }

        // The slanted day ring, back half first so the star sits inside it.
        var hourNow = Math.Clamp(DateTime.Now.Hour + DateTime.Now.Minute / 60.0 - 7, 0, 16);
        var ringR = face * 1.62;
        void RingHalf(bool backHalf)
        {
            for (var h = 0; h < 64; h++)
            {
                var a0 = -90 + h * 360.0 / 64;
                var a1 = a0 + 360.0 / 64;
                var mid = (a0 + a1) / 2 * Math.PI / 180;
                var isBack = Math.Sin(mid) < 0;
                if (isBack != backHalf)
                    continue;
                context.DrawLine(new Pen(Brush(Color.Parse("#F0C877"), isBack ? 0.22 : 0.5), 1.3),
                    Slant(center, ringR, a0), Slant(center, ringR, a1 + 0.4));
            }
            for (var h = 0; h < 16; h++)
            {
                var a = -90 + h * 360.0 / 16;
                var isBack = Math.Sin(a * Math.PI / 180) < 0;
                if (isBack != backHalf)
                    continue;
                var len = h % 4 == 0 ? 6 : 3.5;
                context.DrawLine(new Pen(Brush(Color.Parse("#F0C877"), isBack ? 0.3 : 0.7), 1),
                    Slant(center, ringR - len * 0.5, a), Slant(center, ringR + len, a));
            }
        }
        RingHalf(true);

        // The star: white-hot in the middle, orange at the limb.
        var disc = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.42, 0.4, RelativeUnit.Relative),
            RadiusX = RelativeScalar.Parse("62%"),
            RadiusY = RelativeScalar.Parse("62%"),
            GradientStops =
            {
                new GradientStop(Color.Parse("#FFF8E0"), 0),
                new GradientStop(Color.Parse("#FFDF8C"), 0.38),
                new GradientStop(Color.Parse("#F2A93C"), 0.78),
                new GradientStop(Color.Parse("#C9691A"), 1),
            },
        };
        context.DrawEllipse(disc, null, center, face, face);

        // Granulation: soft cells drifting across the surface, kept inside the disc.
        using (context.PushGeometryClip(new EllipseGeometry(new Rect(center.X - face, center.Y - face, face * 2, face * 2))))
        {
            for (var i = 0; i < 9; i++)
            {
                var a = i * 40 + Phase * (3 + i % 3);
                var d = face * (0.15 + 0.55 * ((i * 37) % 10) / 10.0);
                var p = Pt(center, d, a);
                var bright = i % 2 == 0;
                context.DrawEllipse(Brush(bright ? Color.Parse("#FFFFFF") : Color.Parse("#B8560F"), bright ? 0.18 : 0.14), null,
                    p, face * (0.18 + 0.05 * (i % 3)), face * (0.12 + 0.04 * (i % 3)));
            }
            // Limb darkening: the edge of a star is always dimmer than its middle.
            context.DrawEllipse(new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0x7A, 0x30, 0x08), 0.6),
                    new GradientStop(Color.FromArgb(0x70, 0x7A, 0x30, 0x08), 1),
                },
            }, null, center, face, face);
        }

        // Progress: a thin bright arc just outside the star.
        var total = CoreCount + CoreDone;
        var done = total == 0 ? 1.0 : (double)CoreDone / total;
        var arcR = face + 5;
        context.DrawEllipse(null, new Pen(Brush(Color.Parse("#FFE6A8"), 0.2), 2), center, arcR, arcR);
        if (done >= 0.999)
            context.DrawEllipse(null, new Pen(Brush(Color.Parse("#FFF1C9")), 2.2), center, arcR, arcR);
        else if (done > 0.01)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(Pt(center, arcR, -90), false);
                ctx.ArcTo(Pt(center, arcR, -90 + done * 360), new Size(arcR, arcR), 0, done > 0.5, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(Brush(Color.Parse("#FFF1C9")), 2.2, lineCap: PenLineCap.Round), g);
        }

        RingHalf(false);

        // The bead: where the day is right now, riding the ring.
        var nowAngle = -90 + hourNow * 360.0 / 16;
        var bead = Slant(center, ringR, nowAngle);
        context.DrawEllipse(Brush(Color.Parse("#FFE6A8"), 0.3), null, bead, 7, 7);
        context.DrawEllipse(Brush(Color.Parse("#FFFFFF")), null, bead, 2.8, 2.8);

        var left = CoreCount.ToString(CultureInfo.InvariantCulture);
        if (CoreCount == 0 && total > 0)
        {
            DrawText(context, "Clear", 14, "#3A2208", new Point(center.X, center.Y - 4), centered: true, bold: true);
            DrawText(context, "FOR TODAY", 7.5, "#5A3A12", new Point(center.X, center.Y + 11), centered: true);
        }
        else
        {
            DrawText(context, left, 28, "#2E1A06", new Point(center.X, center.Y - 5), centered: true, bold: true);
            DrawText(context, "LEFT TODAY", 7.5, "#5A3A12", new Point(center.X, center.Y + 15), centered: true);
        }
    }

    // --- Planets ------------------------------------------------------------------------------------

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
                var color = emphasised ? Brass : Ink;
                var brush = Brush(color, emphasised ? 0.8 : 0.28);
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

    /// <summary>
    /// One planet, lit by the sun. The bright side faces the middle of the map, a soft terminator darkens the far
    /// side, and a few bands (different for every goal) give each one its own surface. Moons orbit on the same slant
    /// as the map, passing behind and in front.
    /// </summary>
    private void DrawBody(DrawingContext context, OrbitBody body, Point c, double radius, Point sun)
    {
        var accent = body.Accent;
        var hot = body.Pressure >= Planner.MustDo;
        var hovered = body.Id == _hover;
        var done = body.Fraction is >= 0.999;
        var seed = Unit(body.Id);

        // Which way the light comes from: toward the sun.
        var lx = sun.X - c.X;
        var ly = sun.Y - c.Y;
        var ll = Math.Max(1, Math.Sqrt(lx * lx + ly * ly));
        lx /= ll;
        ly /= ll;

        // An atmosphere, always faint; pressing goals and the one under the pointer glow properly.
        var breathe = hovered ? 1 : 0.5 + 0.5 * Math.Sin(Phase * 1.6 + seed * 6);
        var glow = hovered ? 0.5 : hot ? 0.28 + 0.12 * breathe : 0.10;
        context.DrawEllipse(new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(A(accent, glow), 0.5),
                new GradientStop(A(accent, 0), 1),
            },
        }, null, c, radius * (hot || hovered ? 2.2 : 1.7), radius * (hot || hovered ? 2.2 : 1.7));

        // Moons on the far side of the planet go first, so the planet covers them.
        var moonOrbit = radius + 17;
        var moons = new List<(Point At, double Depth, MoonState State)>();
        if (body.Moons.Count > 0)
        {
            var spin = Phase * 5 + seed * 90;
            for (var i = 0; i < body.Moons.Count; i++)
            {
                var a = spin + i * 360.0 / body.Moons.Count;
                moons.Add((Slant(c, moonOrbit, a), Math.Sin(a * Math.PI / 180), body.Moons[i]));
            }
            using (context.PushTransform(Flatten(c)))
                context.DrawEllipse(null, new Pen(Brush(accent, 0.2), 1), c, moonOrbit, moonOrbit);
            foreach (var m in moons.Where(m => m.Depth < 0))
                DrawMoon(context, m.At, m.State, accent, 0.7);
        }

        // The sphere itself, brightest where it faces the sun.
        var sphere = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5 + 0.3 * lx, 0.5 + 0.3 * ly, RelativeUnit.Relative),
            RadiusX = RelativeScalar.Parse("80%"),
            RadiusY = RelativeScalar.Parse("80%"),
            GradientStops =
            {
                new GradientStop(Lighten(accent, 0.62), 0),
                new GradientStop(Lighten(accent, 0.12), 0.32),
                new GradientStop(accent, 0.55),
                new GradientStop(Darken(accent, 0.55), 0.9),
                new GradientStop(Darken(accent, 0.75), 1),
            },
        };
        var disc = new Rect(c.X - radius, c.Y - radius, radius * 2, radius * 2);
        context.DrawEllipse(sphere, null, c, radius, radius);

        using (context.PushGeometryClip(new EllipseGeometry(disc)))
        {
            // Bands, tilted a different way for each goal.
            var bands = 2 + (int)(seed * 3);
            var turn = (seed * 70 - 35) * Math.PI / 180;
            using (context.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(turn) * Matrix.CreateTranslation(c.X, c.Y)))
            {
                for (var k = 0; k < bands; k++)
                {
                    var y = c.Y - radius + (k + 0.6) * 2 * radius / (bands + 0.4);
                    var thick = radius * (0.14 + 0.1 * ((k * 7 + (int)(seed * 10)) % 3) / 2.0);
                    var dark = k % 2 == 0;
                    context.DrawRectangle(Brush(dark ? Darken(accent, 0.6) : Lighten(accent, 0.5), dark ? 0.26 : 0.2), null,
                        new Rect(c.X - radius - 2, y, radius * 2 + 4, thick));
                }
                // One storm, somewhere.
                context.DrawEllipse(Brush(Lighten(accent, 0.55), 0.22), null,
                    new Point(c.X + radius * (seed - 0.5) * 0.9, c.Y + radius * (0.25 - seed * 0.5)), radius * 0.2, radius * 0.11);
            }

            // The night side: a shadow that deepens away from the sun.
            context.DrawEllipse(new RadialGradientBrush
            {
                Center = new RelativePoint(0.5 + 0.18 * lx, 0.5 + 0.18 * ly, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5 + 0.18 * lx, 0.5 + 0.18 * ly, RelativeUnit.Relative),
                RadiusX = RelativeScalar.Parse("88%"),
                RadiusY = RelativeScalar.Parse("88%"),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 4, 5, 14), 0.5),
                    new GradientStop(Color.FromArgb(0xB4, 4, 5, 14), 1),
                },
            }, null, c, radius + 1, radius + 1);
        }

        // Rim light on the side facing the sun, and a small highlight just inside it.
        var lightAngle = Math.Atan2(ly, lx) * 180 / Math.PI;
        var rim = new StreamGeometry();
        using (var ctx = rim.Open())
        {
            ctx.BeginFigure(Pt(c, radius - 0.4, lightAngle - 75), false);
            ctx.ArcTo(Pt(c, radius - 0.4, lightAngle + 75), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Brush(Lighten(accent, 0.75), 0.75), 1.3, lineCap: PenLineCap.Round), rim);
        context.DrawEllipse(Brush(Color.Parse("#FFFFFF"), 0.28), null, new Point(c.X + lx * radius * 0.42, c.Y + ly * radius * 0.42), radius * 0.2, radius * 0.14);

        if (done)
            DrawText(context, "✓", radius * 1.1, "#0B0D1A", c, centered: true, bold: true);

        // Progress is a thin orbit lying flat around the planet, filled clockwise from the top.
        if (body.Fraction is { } fraction)
        {
            using (context.PushTransform(Flatten(c)))
            {
                var arcR = radius + 6;
                context.DrawEllipse(null, new Pen(Brush(accent, 0.28), 1.5), c, arcR, arcR);
                if (fraction > 0.01)
                {
                    var pen = new Pen(Brush(Lighten(accent, 0.3)), 2.2, lineCap: PenLineCap.Round);
                    if (fraction >= 0.999)
                        context.DrawEllipse(null, pen, c, arcR, arcR);
                    else
                    {
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(PtFlat(c, arcR, -90), false);
                            ctx.ArcTo(PtFlat(c, arcR, -90 + fraction * 360), new Size(arcR, arcR), 0, fraction > 0.5, SweepDirection.Clockwise);
                            ctx.EndFigure(false);
                        }
                        context.DrawGeometry(null, pen, g);
                    }
                }
            }
        }

        // Moons in front of the planet.
        foreach (var m in moons.Where(m => m.Depth >= 0))
            DrawMoon(context, m.At, m.State, accent, 1);

        if (body.Id == SelectedId)
        {
            var extent = radius + (body.Moons.Count > 0 ? 26 : 12);
            using (context.PushTransform(Flatten(c)))
                context.DrawEllipse(null, new Pen(Brush(Brass), 1.5), c, extent, extent);
            context.DrawEllipse(null, new Pen(Brush(Brass, 0.35), 1), c, radius + 4, radius + 4);
        }
    }

    /// <summary>A small moon: lit, hollow (ready), or dotted (waiting on something).</summary>
    private static void DrawMoon(DrawingContext context, Point at, MoonState state, Color accent, double alpha)
    {
        switch (state)
        {
            case MoonState.Done:
                context.DrawEllipse(new RadialGradientBrush
                {
                    GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(A(Lighten(accent, 0.6), alpha), 0), new GradientStop(A(accent, alpha), 1) },
                }, null, at, 3.8, 3.8);
                break;
            case MoonState.Open:
                context.DrawEllipse(Brush(Color.Parse("#0B0D1A"), alpha), new Pen(Brush(Lighten(accent, 0.2), alpha), 1.4), at, 3.5, 3.5);
                break;
            default:
                context.DrawEllipse(null, new Pen(Brush(accent, 0.55 * alpha), 1, new DashStyle([1.2, 1.4], 0)), at, 3.5, 3.5);
                break;
        }
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    // --- Labels and tooltip -------------------------------------------------------------------------

    /// <summary>
    /// Tries right, left, below, above in turn and takes the first spot that touches no other label or body. A
    /// label that fits nowhere is left out until you hover it, which beats a pile of overlapping text.
    /// </summary>
    private Rect? PlaceLabel(OrbitBody body, Point c, double radius, Point center, List<Rect> taken, bool forced, out FormattedText? text)
    {
        var maxWidth = Math.Min(170, Math.Max(80, Bounds.Width / 2 - 24));
        var ft = Format(body.Title, 13, forced ? "#E8B64C" : "#E4E7F6", maxWidth, bold: forced);
        text = ft;
        var w = ft.Width;
        var h = ft.Height;
        var gap = radius + (body.Moons.Count > 0 ? 27 : 11);
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
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#D00B0D1A")), null,
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

    /// <summary>A point on a flat circle, for drawing inside a <see cref="Flatten"/> transform.</summary>
    private static Point PtFlat(Point c, double r, double degrees) => Pt(c, r, degrees);

    private static Point Pt(Point c, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
