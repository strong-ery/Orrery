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
/// Goals in time. One lane per bucket, time running left to right, today as a brass line. Each goal is a bar from when
/// it started to when it is due: the filled part is how far along it is, the notch at the end is the deadline, and the
/// next step is written beside its name. Days you planned on the calendar show as brass dots on the bar. Goals with no
/// deadline are a quiet line from today, with a tick wherever a repeating goal comes due.
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

    private const double AxisH = 38, HeadH = 30, RowH = 54, LaneGap = 14, Pad = 4;
    private const int Back = 6;
    private static readonly Color Brass = Color.Parse("#E8B64C");
    private static readonly Color Ink = Color.Parse("#7C8CFF");

    private Guid? _hover;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        var height = AxisH;
        foreach (var lane in Lanes ?? [])
            height += HeadH + lane.Bars.Count * RowH + LaneGap;
        return new Size(Math.Max(width, 560), height + 8);
    }

    private double Left => Pad;
    private double Right => Math.Max(Bounds.Width, 560) - Pad;
    private double PerDay => (Right - Left) / (SpanDays + Back);

    private double XOf(DateOnly day) => Left + (day.DayNumber - Today.DayNumber + Back) * PerDay;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>The y of each row, so drawing and pointing agree.</summary>
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
        ToolTip.SetTip(this, bar is null ? null : Describe(bar));
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

    private static string Describe(TimelineBar b)
    {
        var parts = new List<string> { b.Title };
        if (b.Fraction is { } f)
            parts.Add($"{Math.Round(f * 100)}% done");
        if (b.Deadline is { } d)
            parts.Add($"due {d:ddd MMM d}");
        if (b.Next.Length > 0)
            parts.Add(b.NextIsWaiting ? $"waiting: {b.Next}" : $"next: {b.Next}");
        if (b.Planned.Count > 0)
            parts.Add($"planned {string.Join(", ", b.Planned.Take(4).Select(p => p.ToString("MMM d", CultureInfo.CurrentCulture)))}");
        return string.Join("  ·  ", parts);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var today = Today;
        var height = Bounds.Height;

        DrawAxis(context, today, height);

        var rows = Rows().ToList();
        var y = AxisH;
        foreach (var lane in Lanes ?? [])
        {
            var heading = Text(string.Join(' ', lane.Name.ToUpperInvariant().Select(c => c.ToString())), 10, "#7F88BC", 300, bold: true);
            context.DrawText(heading, new Point(Left + 2, y + 6));
            context.DrawLine(PenOf(Tint(Ink, 0x30), 1), new Point(Left, y + HeadH - 5), new Point(Right, y + HeadH - 5));
            y += HeadH + lane.Bars.Count * RowH + LaneGap;
        }

        foreach (var (bar, rowY) in rows)
            DrawBar(context, bar, rowY, today);

        // Today, on top of everything.
        var tx = XOf(today);
        context.DrawLine(PenOf(Tint(Brass, 0xC0), 1.5), new Point(tx, AxisH - 8), new Point(tx, height - 4));
    }

    private void DrawAxis(DrawingContext context, DateOnly today, double height)
    {
        var from = today.AddDays(-Back);
        var to = today.AddDays(SpanDays);
        var grid = PenOf(Tint(Ink, 0x16), 1);
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
            context.DrawLine(firstOfMonth ? PenOf(Tint(Ink, 0x30), 1) : grid, new Point(x, AxisH - 8), new Point(x, height - 4));
            var label = firstOfMonth
                ? d.ToString("MMM", CultureInfo.CurrentCulture).ToUpperInvariant()
                : d.Day.ToString(CultureInfo.InvariantCulture);
            var ft = Text(label, firstOfMonth ? 10.5 : 10, firstOfMonth ? "#B8C0E8" : "#6E77A8", 60, bold: firstOfMonth);
            if (x + 4 + ft.Width <= Right)
                context.DrawText(ft, new Point(x + 4, 6));
        }

        var todayLabel = Text("TODAY", 10, "#E8B64C", 60, bold: true);
        var tx = XOf(today);
        context.DrawText(todayLabel, new Point(Math.Min(tx - todayLabel.Width / 2, Right - todayLabel.Width), 20));
    }

    private void DrawBar(DrawingContext context, TimelineBar bar, double y, DateOnly today)
    {
        var accent = Accents is not null && Accents.TryGetValue(bar.GoalId, out var c) ? c : Color.Parse("#8890B0");
        var selected = bar.GoalId == SelectedId;
        var hovered = bar.GoalId == _hover;

        if (hovered || selected)
            context.DrawRectangle(new SolidColorBrush(Tint(Ink, hovered ? 0x1C : 0x12)), null, new Rect(Left - 2, y + 2, Right - Left + 4, RowH - 6), 8, 8);
        if (selected)
            context.DrawRectangle(new SolidColorBrush(Brass), null, new Rect(Left - 2, y + 8, 2.5, RowH - 18), 1, 1);

        var barY = y + 31;
        var mid = barY + 5;
        var windowStart = XOf(today.AddDays(-Back));
        var trueFrom = XOf(bar.Created);
        var fromX = Math.Max(trueFrom, windowStart);

        double? deadlineX = null;
        if (bar.Deadline is { } due)
        {
            var dueX = XOf(due);
            var clipped = dueX > Right;
            var endX = Math.Min(dueX, Right);
            if (endX < fromX + 6)
                fromX = Math.Max(windowStart, endX - 6);
            if (trueFrom > fromX)
                trueFrom = fromX;
            context.DrawRectangle(new SolidColorBrush(Tint(accent, 0x38)), null, new Rect(fromX, barY, endX - fromX, 10), 5, 5);
            if (bar.Fraction is { } f && f > 0.005)
            {
                // Progress is a share of the whole span, even when the start is off the left edge.
                var whole = (dueX - trueFrom) * Math.Min(1, f);
                var fillEnd = Math.Min(endX, Math.Max(fromX + 8, trueFrom + whole));
                context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(fromX, barY, fillEnd - fromX, 10), 5, 5);
            }
            deadlineX = endX;

            var late = due < today;
            var markColor = bar.Pressing || late ? Brass : Color.Parse("#F2F4FF");
            if (clipped)
                DrawChevron(context, endX, mid, markColor);
            else
                context.DrawRectangle(new SolidColorBrush(markColor), null, new Rect(endX - 1.5, barY - 4, 3, 18), 1.5, 1.5);

            var dueLabel = Text(late ? $"was due {due:MMM d}" : due.ToString("MMM d", CultureInfo.CurrentCulture), 10.5,
                late || bar.Pressing ? "#E8B64C" : "#8B93C4", 120);
            var labelX = endX - dueLabel.Width - 8;
            if (labelX > fromX + 4 && endX - fromX > dueLabel.Width + 40)
                context.DrawText(dueLabel, new Point(endX + 7 <= Right - dueLabel.Width ? endX + 7 : labelX, barY - 3));
            else if (endX + 7 + dueLabel.Width <= Right)
                context.DrawText(dueLabel, new Point(endX + 7, barY - 3));
        }
        else
        {
            // Nothing to finish by: a quiet line forward from today, with ticks where a repeating goal comes due.
            var startX = Math.Max(XOf(today), Left);
            context.DrawLine(PenOf(Tint(accent, 0x50), 2, new DashStyle([2, 4], 0)), new Point(startX, mid), new Point(Right, mid));
            context.DrawEllipse(new SolidColorBrush(accent), null, new Point(startX, mid), 4, 4);
            foreach (var day in bar.Due)
            {
                var x = XOf(day);
                if (x >= Left && x <= Right)
                    context.DrawRectangle(new SolidColorBrush(Tint(accent, 0xB0)), null, new Rect(x - 1.5, mid - 6, 3, 12), 1.5, 1.5);
            }
        }

        // Planned days.
        foreach (var day in bar.Planned)
        {
            var x = XOf(day);
            if (x < Left || x > Right)
                continue;
            context.DrawEllipse(new SolidColorBrush(Brass), new Pen(new SolidColorBrush(Color.Parse("#14172B")), 1.5), new Point(x, mid), 4.5, 4.5);
        }

        // Name, and what to do next, anchored so it stays readable when the bar starts off-screen.
        var anchor = Left + 2;
        var title = Text(bar.Title, 13.5, "#F2F4FF", Math.Max(120, Right - anchor - 16), bold: true);
        context.DrawText(title, new Point(anchor, y + 7));

        var tail = bar.Next.Length > 0 ? (bar.NextIsWaiting ? $"waiting · {bar.Next}" : $"next · {bar.Next}") : bar.Note;
        var room = Right - anchor - title.Width - 24;
        if (tail.Length > 0 && room > 60)
        {
            var next = Text(tail, 11.5, bar.NextIsWaiting ? "#C9A55A" : "#8B93C4", room);
            context.DrawText(next, new Point(anchor + title.Width + 12, y + 9));
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
