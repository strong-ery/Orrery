using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Orrery.App.ViewModels;

namespace Orrery.App.Views;

public partial class CalendarView : UserControl
{
    private bool _stacked;

    // Dragging a step onto a day.
    private Guid? _dragGoal;
    private Guid? _dragStep;
    private Point _dragStart;
    private bool _dragMoved;
    private Control? _hover;
    private TrayItemViewModel? _dragTray;

    public CalendarView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
    }

    /// <summary>
    /// Wide: the month with the day's agenda beside it. Narrow: the agenda drops under the month, so neither is cut off.
    /// </summary>
    private void Fit(double width)
    {
        var stacked = width < 780;
        if (stacked == _stacked)
            return;
        _stacked = stacked;
        if (stacked)
        {
            Root.ColumnDefinitions[1].Width = new GridLength(0);
            Root.ColumnSpacing = 0;
            Grid.SetColumn(Side, 0);
            Grid.SetRow(Side, 1);
            Grid.SetRowSpan(Side, 1);
            Grid.SetRowSpan(Month, 1);
            Side.MaxHeight = 230;
            Side.Margin = new Thickness(0, 14, 0, 0);
        }
        else
        {
            Root.ColumnDefinitions[1].Width = new GridLength(300);
            Root.ColumnSpacing = 28;
            Grid.SetColumn(Side, 1);
            Grid.SetRow(Side, 0);
            Grid.SetRowSpan(Side, 2);
            Grid.SetRowSpan(Month, 2);
            Side.MaxHeight = double.PositiveInfinity;
            Side.Margin = default;
        }
    }

    // --- Drag to plan -------------------------------------------------------------------------------

    private void OnTrayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TrayItemViewModel item } c)
        {
            _dragTray = item;
            Begin(c, e, item.GoalId, item.StepId, item.Title);
        }
    }

    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only things you planned can be picked up, and only while planning; otherwise the click selects the day.
        if (DataContext is CalendarViewModel { IsPlanning: true }
            && sender is Control { DataContext: CalendarItemViewModel { IsPlanned: true } chip } c)
        {
            _dragTray = null;
            Begin(c, e, chip.GoalId, chip.StepId, chip.Title);
        }
    }

    private void Begin(Control source, PointerPressedEventArgs e, Guid goal, Guid? step, string title)
    {
        if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
            return;
        _dragGoal = goal;
        _dragStep = step;
        _dragStart = e.GetPosition(this);
        _dragMoved = false;
        GhostText.Text = title;
        e.Pointer.Capture(source);
        source.PointerMoved += OnDragMoved;
        source.PointerReleased += OnDragReleased;
        e.Handled = true;
    }

    private void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (_dragGoal is null)
            return;
        var at = e.GetPosition(this);
        if (!_dragMoved && Math.Abs(at.X - _dragStart.X) + Math.Abs(at.Y - _dragStart.Y) < 5)
            return;
        _dragMoved = true;
        Ghost.IsVisible = true;
        Canvas.SetLeft(Ghost, at.X + 12);
        Canvas.SetTop(Ghost, at.Y + 12);

        var cell = CellAt(at);
        if (!ReferenceEquals(cell, _hover))
        {
            _hover?.Classes.Remove("drop");
            _hover = cell;
            _hover?.Classes.Add("drop");
        }
    }

    private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control source)
        {
            source.PointerMoved -= OnDragMoved;
            source.PointerReleased -= OnDragReleased;
            e.Pointer.Capture(null);
        }
        Ghost.IsVisible = false;
        _hover?.Classes.Remove("drop");
        _hover = null;

        var goal = _dragGoal;
        var step = _dragStep;
        var tray = _dragTray;
        _dragGoal = null;
        _dragTray = null;
        if (goal is null || DataContext is not CalendarViewModel vm)
            return;

        var at = e.GetPosition(this);
        if (!_dragMoved)
        {
            tray?.Open(); // a click, not a drag
            return;
        }
        if (CellAt(at) is { DataContext: DayCellViewModel cell })
            vm.PlanOn(goal.Value, step, cell.Date);
        else if (Area(Side).Contains(at))
            vm.PlanOn(goal.Value, step, null); // dropped back on the side panel: unplan
    }

    private Control? CellAt(Point at)
    {
        if (CellList.ItemsPanelRoot is not { } panel)
            return null;
        foreach (var child in panel.Children)
            if (child.DataContext is DayCellViewModel && Area(child).Contains(at))
                return (child as ContentPresenter)?.Child as Control ?? child;
        return null;
    }

    private Rect Area(Control control) =>
        control.TranslatePoint(default, this) is { } origin ? new Rect(origin, control.Bounds.Size) : default;
}
