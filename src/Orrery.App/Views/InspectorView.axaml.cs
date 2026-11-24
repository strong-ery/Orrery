using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Orrery.App.ViewModels;
using Orrery.Core.Models;

namespace Orrery.App.Views;

public partial class InspectorView : UserControl
{
    public InspectorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is InspectorViewModel vm)
                vm.FocusAdd += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => NewStepBox.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        };
    }

    // Dragging steps: the top of a row puts it before, the bottom after, the middle inside (making a group of it).
    private InspectorStepViewModel? _held;
    private Point _from;
    private bool _dragging;
    private InspectorStepViewModel? _over;
    private DropPlace _place;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        StepList.AddHandler(PointerPressedEvent, OnStepPressed, RoutingStrategies.Tunnel, true);
        StepList.AddHandler(PointerMovedEvent, OnStepMoved, RoutingStrategies.Bubble, true);
        StepList.AddHandler(PointerReleasedEvent, OnStepReleased, RoutingStrategies.Bubble, true);
        StepList.PointerCaptureLost += (_, _) => EndDrag(false);
    }

    private IEnumerable<(Control Row, InspectorStepViewModel Vm)> Rows() =>
        (StepList.ItemsPanelRoot?.Children ?? [])
            .Where(c => c.DataContext is InspectorStepViewModel)
            .Select(c => ((Control)c, (InspectorStepViewModel)c.DataContext!));

    private void OnStepPressed(object? sender, PointerPressedEventArgs e)
    {
        _held = null;
        if (!e.GetCurrentPoint(StepList).Properties.IsLeftButtonPressed || e.Source is not Visual src)
            return;
        // Typing in an open editor must never start a drag.
        if (src.GetSelfAndVisualAncestors().Any(v => v is TextBox or ComboBox))
            return;
        var row = src.GetSelfAndVisualAncestors().OfType<ContentPresenter>().FirstOrDefault(p => p.DataContext is InspectorStepViewModel);
        if (row?.DataContext is InspectorStepViewModel { IsEditing: false, IsAddRow: false } vm)
        {
            _held = vm;
            _from = e.GetPosition(StepList);
        }
    }

    private void OnStepMoved(object? sender, PointerEventArgs e)
    {
        if (_held is null || DataContext is not InspectorViewModel inspector)
            return;
        var at = e.GetPosition(StepList);
        if (!_dragging)
        {
            if (Math.Abs(at.Y - _from.Y) < 7 && Math.Abs(at.X - _from.X) < 7)
                return;
            _dragging = true;
            e.Pointer.Capture(StepList);
            StepList.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        // Keep the panel scrolling when the pointer nears its top or bottom edge.
        if (this.FindDescendantOfType<ScrollViewer>() is { } scroll)
        {
            var y = e.GetPosition(scroll).Y;
            if (y < 40)
                scroll.Offset = scroll.Offset.WithY(Math.Max(0, scroll.Offset.Y - 14));
            else if (y > scroll.Bounds.Height - 40)
                scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y + 14);
        }

        _over = null;
        foreach (var (row, vm) in Rows())
        {
            vm.DropPlace = null;
            var top = row.TranslatePoint(new Point(0, 0), StepList)?.Y ?? 0;
            if (at.Y < top || at.Y >= top + row.Bounds.Height || vm == _held || vm.IsEditing || vm.IsAddRow || !inspector.CanDrop(_held.StepId, vm.StepId))
                continue;
            var f = (at.Y - top) / Math.Max(1, row.Bounds.Height);
            _place = f < 0.28 ? DropPlace.Before : f > 0.72 ? DropPlace.After : DropPlace.Inside;
            vm.DropPlace = _place;
            _over = vm;
        }
    }

    private void OnStepReleased(object? sender, PointerReleasedEventArgs e) => EndDrag(true);

    private void EndDrag(bool drop)
    {
        var (held, over, place, was) = (_held, _over, _place, _dragging);
        _held = null;
        _over = null;
        _dragging = false;
        StepList.Cursor = Cursor.Default;
        foreach (var (_, vm) in Rows())
            vm.DropPlace = null;
        if (drop && was && held is not null && over is not null && DataContext is InspectorViewModel inspector)
            inspector.MoveStep(held.StepId, over.StepId, place);
    }

    /// <summary>Picking a goal in the "Start after" list: this goal now waits for it.</summary>
    private void OnPickGoal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm && (sender as Button)?.DataContext is WaitOption { Id: { } id })
            vm.WaitFor(id);
        StartAfter.Flyout?.Hide();
    }
}
