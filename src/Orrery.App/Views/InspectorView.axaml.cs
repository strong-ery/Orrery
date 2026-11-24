using Avalonia.Controls;
using Avalonia.Interactivity;
using Orrery.App.ViewModels;

namespace Orrery.App.Views;

public partial class InspectorView : UserControl
{
    public InspectorView() => InitializeComponent();

    /// <summary>Picking a goal in the "Start after" list: this goal now waits for it.</summary>
    private void OnPickGoal(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm && (sender as Button)?.DataContext is WaitOption { Id: { } id })
            vm.WaitFor(id);
        (sender as Button)?.Flyout?.Hide();
        StartAfter.Flyout?.Hide();
    }
}
