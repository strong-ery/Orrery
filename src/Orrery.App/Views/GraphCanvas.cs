using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Orrery.Core.Engine;

namespace Orrery.App.Views;

/// <summary>
/// What waits for what, left to right. Boxes are sections, loose steps or whole goals; an arrow means "this must be
/// finished before that can start". Each box says where it stands (steps done, how far along, what it is waiting
/// for). Click a box to open it. Pointing at one lights up everything it holds up and everything holding it up; the
/// light eases in and out so moving down a column does not make the graph blink.
/// </summary>
public sealed class GraphCanvas : Control
{
    public static readonly StyledProperty<FlowLayout?> LayoutDataProperty =
        AvaloniaProperty.Register<GraphCanvas, FlowLayout?>(nameof(LayoutData));
    public static readonly StyledProperty<IReadOnlyDictionary<Guid, Color>?> ColorsProperty =
        AvaloniaProperty.Register<GraphCanvas, IReadOnlyDictionary<Guid, Color>?>(nameof(Colors));
    public static readonly StyledProperty<ICommand?> NodeCommandProperty =
        AvaloniaProperty.Register<GraphCanvas, ICommand?>(nameof(NodeCommand));

    static GraphCanvas()
    {
        AffectsRender<GraphCanvas>(LayoutDataProperty, ColorsProperty);
        AffectsMeasure<GraphCanvas>(LayoutDataProperty);
    }

    public FlowLayout? LayoutData { get => GetValue(LayoutDataProperty); set => SetValue(LayoutDataProperty, value); }
    public IReadOnlyDictionary<Guid, Color>? Colors { get => GetValue(ColorsProperty); set => SetValue(ColorsProperty, value); }
    public ICommand? NodeCommand { get => GetValue(NodeCommandProperty); set => SetValue(NodeCommandProperty, value); }

    private double NodeW = 236;
    private const double NodeH = 66, GapX = 72, GapY = 16, Pad = 28, Top = 56;
    private ScrollViewer? _scroll;
    private static readonly string[] ColumnNames = ["FIRST", "THEN", "AFTER THAT"];

    private Guid? _focusId;
    private HashSet<Guid> _chain = [];
    private double _leaveAt = -1;
    private double _dim;
    private readonly Dictionary<Guid, double> _hl = [];
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public GraphCanvas()
    {
        _timer.Tick += (_, _) => Step();
    }

    private Rect RectOf(FlowNode n) =>
        new(Pad + n.Column * (NodeW + GapX), Top + n.Row * (NodeH + GapY), NodeW, NodeH);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ScrollViewer>(this);
        if (_scroll is not null)
            _scroll.SizeChanged += (_, _) => InvalidateMeasure();
    }

    // Boxes share the width they are given: as the window narrows they shrink toward a minimum, and only then scroll.
    protected override Size MeasureOverride(Size availableSize)
    {
        if (LayoutData is not { Nodes.Count: > 0 } l)
            return new Size(0, 0);
        var room = (_scroll?.Bounds.Width ?? 1000) - Pad * 2 - 18;
        NodeW = Math.Clamp((room - (l.Columns - 1) * GapX) / l.Columns, 180, 250);
        return new Size(Pad * 2 + l.Columns * NodeW + (l.Columns - 1) * GapX, Top + Pad + l.Rows * NodeH + (l.Rows - 1) * GapY);
    }

    private FlowNode? HitTest(Point p) => LayoutData?.Nodes.FirstOrDefault(n => RectOf(n).Inflate(GapY / 2.0).Contains(p));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (hit is not null)
        {
            _leaveAt = -1;
            if (hit.Id != _focusId)
            {
                _focusId = hit.Id;
                _chain = Chain(hit.Id);
            }
        }
        else if (_focusId is not null && _leaveAt < 0)
            _leaveAt = _clock.Elapsed.TotalSeconds + 0.3; // linger across a gap
        _timer.Start();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_focusId is not null)
            _leaveAt = _clock.Elapsed.TotalSeconds + 0.15;
        _timer.Start();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTest(e.GetPosition(this)) is { } node && NodeCommand?.CanExecute(node) == true)
            NodeCommand.Execute(node);
    }

    private void Step()
    {
        if (_leaveAt >= 0 && _clock.Elapsed.TotalSeconds >= _leaveAt)
        {
            _focusId = null;
            _leaveAt = -1;
            _chain = [];
        }
        var settled = true;
        var dimTarget = _focusId is null ? 0.0 : 1.0;
        _dim += (dimTarget - _dim) * 0.2;
        if (Math.Abs(dimTarget - _dim) > 0.01)
            settled = false;
        else
            _dim = dimTarget;
        foreach (var id in _hl.Keys.ToList())
        {
            var target = _chain.Contains(id) ? 1.0 : 0.0;
            var next = _hl[id] + (target - _hl[id]) * 0.25;
            if (Math.Abs(target - next) > 0.01)
                settled = false;
            else
                next = target;
            _hl[id] = next;
        }
        InvalidateVisual();
        if (settled && _leaveAt < 0)
            _timer.Stop();
    }

    /// <summary>Everything upstream and downstream of a node, for lighting up its whole chain.</summary>
    private HashSet<Guid> Chain(Guid start)
    {
        var seen = new HashSet<Guid> { start };
        var edges = LayoutData!.Edges;
        foreach (var forward in new[] { true, false })
        {
            var stack = new Stack<Guid>([start]);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                foreach (var e in edges)
                {
                    var next = forward ? (e.From == id ? e.To : (Guid?)null) : (e.To == id ? e.From : (Guid?)null);
                    if (next is { } n && seen.Add(n))
                        stack.Push(n);
                }
            }
        }
        return seen;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (LayoutData is not { Nodes.Count: > 0 } layout)
            return;
        foreach (var n in layout.Nodes)
            _hl.TryAdd(n.Id, 0);

        var byId = layout.Nodes.ToDictionary(n => n.Id);
        var brass = Color.Parse("#E8B64C");
        var ink = Color.Parse("#7C8CFF");

        // Column headings: how far along the chain you are.
        for (var c = 0; c < layout.Columns; c++)
        {
            var name = c < ColumnNames.Length ? ColumnNames[c] : "THEN";
            var ft = Format(string.Join(' ', name.Select(ch => ch.ToString())), 10, "#7F88BC", 200, bold: true);
            var x = Pad + c * (NodeW + GapX);
            context.DrawText(ft, new Point(x + 2, Top - 30));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)), 1),
                new Point(x, Top - 10), new Point(x + NodeW, Top - 10));
        }

        foreach (var edge in layout.Edges)
        {
            var a = RectOf(byId[edge.From]);
            var b = RectOf(byId[edge.To]);
            var on = Math.Min(_hl.GetValueOrDefault(edge.From), _hl.GetValueOrDefault(edge.To));
            var color = Mix(ink, brass, on);
            var alpha = 0x5A + (0xE0 - 0x5A) * on;
            alpha *= 1 - _dim * (1 - on) * 0.6;
            var brush = new SolidColorBrush(Color.FromArgb((byte)alpha, color.R, color.G, color.B));
            var start = new Point(a.Right, a.Center.Y);
            var end = new Point(b.Left - 2, b.Center.Y);
            var bend = (end.X - start.X) / 2;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, false);
                ctx.CubicBezierTo(new Point(start.X + bend, start.Y), new Point(end.X - bend, end.Y), end);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(brush, 1.4 + 0.8 * on), g);
            var head = new StreamGeometry();
            using (var ctx = head.Open())
            {
                ctx.BeginFigure(end, true);
                ctx.LineTo(new Point(end.X - 8, end.Y - 4.5));
                ctx.LineTo(new Point(end.X - 8, end.Y + 4.5));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(brush, null, head);
        }

        foreach (var node in layout.Nodes)
        {
            var rect = RectOf(node);
            var accent = Colors is not null && Colors.TryGetValue(node.GoalId, out var c) ? c : Color.Parse("#8890B0");
            var lit = _hl.GetValueOrDefault(node.Id);
            var focused = node.Id == _focusId;
            var done = node.State == NodeState.Done;
            var next = node.State == NodeState.Open && node.Holds > 0;

            using (context.PushOpacity(1 - _dim * (1 - lit) * 0.55))
            {
                var fill = Mix(Color.Parse("#141831"), Color.Parse("#1E2347"), focused ? 1 : lit * 0.5);
                var edgeColor = next ? brass : accent;
                var edgeAlpha = next ? 0xB0 : node.State == NodeState.Blocked ? 0x55 : 0x80;
                edgeAlpha = (int)(edgeAlpha + (0xFF - edgeAlpha) * (focused ? 1 : lit * 0.4));
                context.DrawRectangle(new SolidColorBrush(fill),
                    new Pen(new SolidColorBrush(Color.FromArgb((byte)edgeAlpha, edgeColor.R, edgeColor.G, edgeColor.B)),
                        1.2 + 0.6 * lit, node.State == NodeState.Blocked ? new DashStyle([3, 3], 0) : null),
                    rect, 10, 10);
                context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(rect.X + 1, rect.Y + 12, 4, rect.Height - 24), 2, 2);

                using (context.PushOpacity(done ? 0.6 : 1))
                {
                    var title = Format(node.Title, 13, "#F2F4FF", NodeW - 96, bold: true);
                    context.DrawText(title, new Point(rect.X + 16, rect.Y + 9));
                    var line = string.IsNullOrEmpty(node.Detail) ? node.GoalTitle : $"{node.GoalTitle}  ·  {node.Detail}";
                    var sub = Format(line, 11, node.State == NodeState.Blocked ? "#C9A55A" : "#8B93C4", NodeW - 32);
                    context.DrawText(sub, new Point(rect.X + 16, rect.Y + 12 + title.Height));

                    var (label, color) = node.State switch
                    {
                        NodeState.Done => ("Done", "#62B8A9"),
                        NodeState.Blocked => ("Waiting", "#C9A55A"),
                        _ when next => ("Up next", "#E8B64C"),
                        _ => ("Ready", "#B8C0E8"),
                    };
                    var state = Format(label, 11, color, 80, bold: true);
                    context.DrawText(state, new Point(rect.Right - 12 - state.Width, rect.Y + 9));
                }

                // How far along it is, as a hairline along the bottom edge.
                if (node.Fraction is { } fraction)
                {
                    var trackY = rect.Bottom - 9;
                    var trackW = rect.Width - 32;
                    context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)), null,
                        new Rect(rect.X + 16, trackY, trackW, 3), 1.5, 1.5);
                    if (fraction > 0.01)
                        context.DrawRectangle(new SolidColorBrush(done ? Color.Parse("#62B8A9") : accent), null,
                            new Rect(rect.X + 16, trackY, Math.Max(3, trackW * Math.Min(1, fraction)), 3), 1.5, 1.5);
                }
            }
        }
    }

    private FormattedText Format(string text, double size, string color, double maxWidth, bool bold = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this), FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal),
            size, new SolidColorBrush(Color.Parse(color)))
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
}
