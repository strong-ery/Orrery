using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Orrery.Core.Engine;

namespace Orrery.App.Views;

/// <summary>
/// What waits for what, left to right. Boxes are sections, loose steps or whole goals; an arrow means "this must be
/// finished before that can start". Click a box to open it. Pointing at one lights up everything it holds up and
/// everything holding it up.
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

    private const double NodeW = 200, NodeH = 58, GapX = 90, GapY = 18, Pad = 28;
    private Guid? _hover;

    private static Rect RectOf(FlowNode n) =>
        new(Pad + n.Column * (NodeW + GapX), Pad + n.Row * (NodeH + GapY), NodeW, NodeH);

    protected override Size MeasureOverride(Size availableSize) =>
        LayoutData is { Nodes.Count: > 0 } l
            ? new Size(Pad * 2 + l.Columns * NodeW + (l.Columns - 1) * GapX, Pad * 2 + l.Rows * NodeH + (l.Rows - 1) * GapY)
            : new Size(0, 0);

    private FlowNode? HitTest(Point p) => LayoutData?.Nodes.FirstOrDefault(n => RectOf(n).Contains(p));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (hit?.Id != _hover)
        {
            _hover = hit?.Id;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (HitTest(e.GetPosition(this)) is { } node && NodeCommand?.CanExecute(node) == true)
            NodeCommand.Execute(node);
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

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (LayoutData is not { Nodes.Count: > 0 } layout)
            return;

        var lit = _hover is { } h ? Chain(h) : null;
        var byId = layout.Nodes.ToDictionary(n => n.Id);

        foreach (var edge in layout.Edges)
        {
            var a = RectOf(byId[edge.From]);
            var b = RectOf(byId[edge.To]);
            var on = lit is not null && lit.Contains(edge.From) && lit.Contains(edge.To);
            var color = on ? Color.Parse("#E8B64C") : Color.Parse("#7C8CFF");
            var alpha = (byte)(lit is null ? 0x70 : on ? 0xE0 : 0x22);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
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
            context.DrawGeometry(null, new Pen(brush, on ? 2 : 1.4), g);
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
            var dim = lit is not null && !lit.Contains(node.Id);
            var hovered = node.Id == _hover;
            var done = node.State == NodeState.Done;

            var fill = hovered ? "#1E2347" : "#141831";
            context.DrawRectangle(new SolidColorBrush(Color.Parse(fill)),
                new Pen(new SolidColorBrush(Color.FromArgb((byte)(hovered ? 0xFF : node.State == NodeState.Blocked ? 0x55 : 0x99), accent.R, accent.G, accent.B)),
                    hovered ? 1.8 : 1.2, node.State == NodeState.Blocked ? new DashStyle([3, 3], 0) : null),
                rect, 10, 10);
            // A colour tab on the left, the way a goal's colour is carried everywhere else.
            context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(rect.X + 1, rect.Y + 12, 4, rect.Height - 24), 2, 2);

            var opacity = dim ? 0.3 : done ? 0.55 : 1;
            using (context.PushOpacity(opacity))
            {
                var title = Format(node.Title, 12.5, "#F2F4FF", NodeW - 70, bold: true);
                var goal = Format(node.GoalTitle, 10, "#8B93C4", NodeW - 70);
                context.DrawText(title, new Point(rect.X + 16, rect.Y + 11));
                context.DrawText(goal, new Point(rect.X + 16, rect.Y + 14 + title.Height));

                var (label, color) = node.State switch
                {
                    NodeState.Done => ("done", "#3CC8B4"),
                    NodeState.Blocked => ("waiting", "#E8B64C"),
                    _ => ("ready", "#E4E7F6"),
                };
                var state = Format(label, 10, color, 60, bold: true);
                context.DrawText(state, new Point(rect.Right - 10 - state.Width, rect.Y + rect.Height / 2 - state.Height / 2));
            }
        }
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
}
