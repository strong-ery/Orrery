using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.Views;

/// <summary>
/// Goals in time. Names and what to do next sit in a column on the left; time runs left to right beside them, with
/// today as a brass line. A goal with a deadline is a bar from when it started to when it is due: the filled part is how
/// far along it is, the notch is the deadline. A goal with no deadline is a quiet dotted line from today, with a tick
/// wherever a repeating goal comes due. Brass dots are days you planned on the calendar.
/// </summary>
public sealed class TimelineCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<TimelineLane>?> LanesProperty =
        AvaloniaProperty.Register<TimelineCanvas, IReadOnlyList<TimelineLane>?>(nameof(Lanes));
    public static readonly StyledProperty<IReadOnlyDictionary<Guid, Color>?> AccentsProperty =
        AvaloniaProperty.Register<TimelineCanvas, IReadOnlyDictionary<Guid, Color>?>(nameof(Accents));
    public static readonly StyledProperty<int> SpanDaysProperty =
        AvaloniaProperty.Register<TimelineCanvas, int>(nameof(SpanDays), 42);
    public static readonly StyledProperty<Guid?> SelectedIdProperty =
        AvaloniaProperty.Register<TimelineCanvas, Guid?>(nameof(SelectedId));
    public static readonly StyledProperty<ICommand?> BarCommandProperty =
        AvaloniaProperty.Register<TimelineCanvas, ICommand?>(nameof(BarCommand));

    static TimelineCanvas()
    {
        AffectsRender<TimelineCanvas>(LanesProperty, AccentsProperty, SpanDaysProperty, SelectedIdProperty);
        AffectsMeasure<TimelineCanvas>(LanesProperty);
    }

    public IReadOnlyList<TimelineLane>? Lanes { get => GetValue(LanesProperty); set => SetValue(LanesProperty, value); }
    public IReadOnlyDictionary<Guid, Color>? Accents { get => GetValue(AccentsProperty); set => SetValue(AccentsProperty, value); }
    public int SpanDays { get => GetValue(SpanDaysProperty); set => SetValue(SpanDaysProperty, value); }
    public Guid? SelectedId { get => GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }
    public ICommand? BarCommand { get => GetValue(BarCommandProperty); set => SetValue(BarCommandProperty, value); }

    private const double AxisH = 40, HeadH = 34, RowH = 52, LaneGap = 12, Pad = 4, EndRoom = 62;
    private const int Back = 5;
    private static readonly Color Brass = Color.Parse("#E8B64C");
    private static readonly Color Ink = Color.Parse("#7C8CFF");

    private Guid? _hover;

    // The zoom eases from one span to the other instead of jumping.
    private double _shown = -1;
    private readonly Avalonia.Threading.DispatcherTimer _ease = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public TimelineCanvas() => _ease.Tick += (_, _) =>
    {
        var gap = SpanDays - _shown;
        if (Math.Abs(gap) < 0.4)
        {
            _shown = SpanDays;
            _ease.Stop();
        }
        else
        {
            _shown += gap * 0.17;
        }
        InvalidateVisual();
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SpanDaysProperty && _shown >= 0)
            _ease.Start();
    }

    private double Shown => _shown < 0 ? (_shown = SpanDays) : _shown;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        var height = AxisH;
        foreach (var lane in Lanes ?? [])
            height += HeadH + lane.Bars.Count * RowH + LaneGap;
        return new Size(Math.Max(width, 620), height + 8);
    }

    private double FullWidth => Math.Max(Bounds.Width, 620);
    private double LabelW => Math.Clamp(FullWidth * 0.2, 170, 250);
    private double Left => Pad + LabelW + 14;
    private double Right => FullWidth - Pad - EndRoom;
    private double PerDay => (Right - Left) / (Shown + Back);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private double XOf(DateOnly day) => Left + (day.DayNumber - Today.DayNumber + Back) * PerDay;

    private IEnumerable<(TimelineBar Bar, double Y)> Rows()
    {
        var y = AxisH;
        foreach (var lane in Lanes ?? [])
        {
            y += HeadH;
            foreach (var bar in lane.Bars)
            {
                yield return (bar, y);
                y += RowH;
            }
            y += LaneGap;
        }
    }

    private TimelineBar? BarAt(Point p)
    {
        foreach (var (bar, y) in Rows())
            if (p.Y >= y && p.Y < y + RowH)
                return bar;
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var bar = BarAt(e.GetPosition(this));
        Cursor = bar is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (bar?.GoalId == _hover)
            return;
        _hover = bar?.GoalId;
        InvalidateVisual();
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
        if (BarAt(e.GetPosition(this)) is { } bar && BarCommand?.CanExecute(bar.GoalId) == true)
            BarCommand.Execute(bar.GoalId);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var today = Today;
        var height = Bounds.Height;

        DrawAxis(context, today, height);

        var y = AxisH;
        foreach (var lane in Lanes ?? [])
        {
            var heading = Text(lane.Name, 12, "#B8C0E8", 300, bold: true);
            context.DrawText(heading, new Point(Pad + 2, y + 10));
            context.DrawLine(PenOf(Tint(Ink, 0x30), 1), new Point(Pad, y + HeadH - 6), new Point(FullWidth - Pad, y + HeadH - 6));
            y += HeadH + lane.Bars.Count * RowH + LaneGap;
        }

        foreach (var (bar, rowY) in Rows())
            DrawRow(context, bar, rowY, today);

        var tx = XOf(today);
        context.DrawLine(PenOf(Tint(Brass, 0xC0), 1.5), new Point(tx, AxisH - 8), new Point(tx, height - 4));
    }

    private void DrawAxis(DrawingContext context, DateOnly today, double height)
    {
        var from = today.AddDays(-Back);
        var to = today.AddDays((int)Math.Ceiling(Shown));
        var weekly = SpanDays <= 90;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var firstOfMonth = d.Day == 1;
            var nearMonth = d.Day is > 1 and < 5 || d.AddDays(1).Day == 1 || d.AddDays(2).Day == 1;
            var tick = weekly ? firstOfMonth || d.DayOfWeek == DayOfWeek.Monday && !nearMonth : firstOfMonth;
            if (!tick)
                continue;
            var x = XOf(d);
            if (x < Left || x > Right)
                continue;
            context.DrawLine(PenOf(Tint(Ink, firstOfMonth ? 0x30 : 0x16), 1), new Point(x, AxisH - 8), new Point(x, height - 4));
            var label = firstOfMonth
                ? d.ToString("MMM", CultureInfo.CurrentCulture)
                : d.Day.ToString(CultureInfo.InvariantCulture);
            var ft = Text(label, firstOfMonth ? 10.5 : 10, firstOfMonth ? "#B8C0E8" : "#6E77A8", 60, bold: firstOfMonth);
            context.DrawText(ft, new Point(x + 4, 6));
        }

        var todayLabel = Text("Today", 10, "#E8B64C", 60, bold: true);
        context.DrawText(todayLabel, new Point(Math.Max(Left - 4, XOf(today) - todayLabel.Width / 2), 21));
    }

    private void DrawRow(DrawingContext context, TimelineBar bar, double y, DateOnly today)
    {
        var accent = Accents is not null && Accents.TryGetValue(bar.GoalId, out var c) ? c : Color.Parse("#8890B0");
        var selected = bar.GoalId == SelectedId;
        var hovered = bar.GoalId == _hover;

        if (hovered || selected)
            context.DrawRectangle(new SolidColorBrush(Tint(Ink, hovered ? 0x1C : 0x12)), null,
                new Rect(Pad - 2, y + 2, FullWidth - Pad * 2 + 4, RowH - 4), 8, 8);
        if (selected)
            context.DrawRectangle(new SolidColorBrush(Brass), null, new Rect(Pad - 2, y + 9, 2.5, RowH - 18), 1, 1);

        // Left column: what it is, then how it stands and what to do next.
        context.DrawText(Text(bar.Title, 13.5, "#F2F4FF", LabelW - 8, bold: true), new Point(Pad + 8, y + 8));
        var facts = new List<string>();
        if (bar.Fraction is { } f && bar.Deadline is not null)
            facts.Add($"{Math.Round(f * 100)}%");
        if (bar.Next.Length > 0)
            facts.Add(bar.NextIsWaiting ? $"waiting · {bar.Next}" : bar.Next);
        else if (bar.Note.Length > 0)
            facts.Add(bar.Note);
        if (facts.Count > 0)
            context.DrawText(Text(string.Join("  ·  ", facts), 11.5, bar.NextIsWaiting ? "#C9A55A" : "#8B93C4", LabelW - 8),
                new Point(Pad + 8, y + 28));

        // Right: the span of time.
        var barY = y + 21;
        var mid = barY + 5;
        var windowStart = XOf(today.AddDays(-Back));

        if (bar.Deadline is { } due)
        {
            var trueFrom = XOf(bar.Created);
            var fromX = Math.Max(trueFrom, windowStart);
            var dueX = XOf(due);
            var clipped = dueX > Right;
            var endX = Math.Min(dueX, Right);
            if (endX < fromX + 6)
                fromX = Math.Max(windowStart, endX - 6);
            if (trueFrom > fromX)
                trueFrom = fromX;

            context.DrawRectangle(new SolidColorBrush(Tint(accent, 0x38)), null, new Rect(fromX, barY, endX - fromX, 10), 5, 5);
            if (bar.Fraction is { } fr && fr > 0.005)
            {
                // Progress is a share of the whole span, even when the start is off the left edge.
                var fillEnd = Math.Min(endX, Math.Max(fromX + 8, trueFrom + (dueX - trueFrom) * Math.Min(1, fr)));
                context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(fromX, barY, fillEnd - fromX, 10), 5, 5);
            }

            var late = due < today;
            var markColor = bar.Pressing || late ? Brass : Color.Parse("#F2F4FF");
            if (clipped)
                DrawChevron(context, endX, mid, markColor);
            else
                context.DrawRectangle(new SolidColorBrush(markColor), null, new Rect(endX - 1.5, barY - 4, 3, 18), 1.5, 1.5);

            var dueLabel = Text(late ? $"late · {due:MMM d}" : due.ToString("MMM d", CultureInfo.CurrentCulture), 10.5,
                late || bar.Pressing ? "#E8B64C" : "#8B93C4", EndRoom + 20);
            context.DrawText(dueLabel, new Point(endX + 9, mid - dueLabel.Height / 2));
        }
        else
        {
            var startX = XOf(today);
            context.DrawLine(PenOf(Tint(accent, 0x50), 2, new DashStyle([2, 4], 0)), new Point(startX, mid), new Point(Right, mid));
            context.DrawEllipse(new SolidColorBrush(accent), null, new Point(startX, mid), 4, 4);
            foreach (var day in bar.Due)
            {
                var x = XOf(day);
                if (x >= Left && x <= Right)
                    context.DrawRectangle(new SolidColorBrush(Tint(accent, 0xB0)), null, new Rect(x - 1.5, mid - 6, 3, 12), 1.5, 1.5);
            }
        }

        foreach (var day in bar.Planned)
        {
            var x = XOf(day);
            if (x < Left || x > Right)
                continue;
            context.DrawEllipse(new SolidColorBrush(Brass), new Pen(new SolidColorBrush(Color.Parse("#14172B")), 1.5), new Point(x, mid), 4.5, 4.5);
        }
    }

    private static void DrawChevron(DrawingContext context, double x, double y, Color color)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x - 6, y - 8), false);
            ctx.LineTo(new Point(x, y));
            ctx.LineTo(new Point(x - 6, y + 8));
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2, lineCap: PenLineCap.Round), g);
    }

    private static Color Tint(Color c, int alpha) => Color.FromArgb((byte)alpha, c.R, c.G, c.B);

    private static Pen PenOf(Color c, double thickness, DashStyle? dash = null) => new(new SolidColorBrush(c), thickness, dash);

    private FormattedText Text(string text, double size, string color, double maxWidth, bool bold = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this), FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal),
            size, new SolidColorBrush(Color.Parse(color)))
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
}
