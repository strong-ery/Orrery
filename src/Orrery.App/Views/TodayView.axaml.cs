using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Orrery.App.ViewModels;

namespace Orrery.App.Views;

public partial class TodayView : UserControl
{
    private PlanRowViewModel? _dragging;
    private int _target = -1;
    private readonly List<(Border Box, string Class)> _marks = [];

    public TodayView() => InitializeComponent();

    /// <summary>Every row in list order: the "up next" card first, then the rest.</summary>
    private List<(Control Visual, PlanRowViewModel Row)> Rows()
    {
        var rows = new List<(Control, PlanRowViewModel)>();
        if (DataContext is TodayViewModel { Hero: { } hero })
            rows.Add((HeroHost, hero));
        if (RestList.ItemsPanelRoot is { } panel)
            foreach (var child in panel.Children)
                if (child is Control { DataContext: PlanRowViewModel row } c)
                    rows.Add((c, row));
        return rows;
    }

    private static Border? BoxOf(Control visual) =>
        visual.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("line") || b.Classes.Contains("hero"));

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PlanRowViewModel row } grip
            || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;
        _dragging = row;
        _target = -1;
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null || DataContext is not TodayViewModel vm)
            return;
        var rows = Rows();
        var y = e.GetPosition(this).Y;

        // The row the pointer is on is the one it will take the place of.
        var at = 0;
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Visual.TranslatePoint(default, this) is { } top && top.Y <= y)
                at = i;
        var from = rows.FindIndex(r => r.Row == _dragging);
        _target = vm.ClampTarget(_dragging.Key, at);

        ClearMarks();
        if (from >= 0 && BoxOf(rows[from].Visual) is { } own)
            Mark(own, "dragging");
        if (from >= 0 && _target != from && _target < rows.Count && BoxOf(rows[_target].Visual) is { } box)
            Mark(box, rows[_target].Visual == HeroHost ? "drop" : _target > from ? "drop" : "dropup");
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        var row = _dragging;
        var target = _target;
        _dragging = null;
        e.Pointer.Capture(null);
        ClearMarks();
        if (row is not null && target >= 0 && DataContext is TodayViewModel vm)
            vm.MoveTo(row.Key, target);
    }

    private void Mark(Border box, string cls)
    {
        box.Classes.Add(cls);
        _marks.Add((box, cls));
    }

    private void ClearMarks()
    {
        foreach (var (box, cls) in _marks)
            box.Classes.Remove(cls);
        _marks.Clear();
    }
}
