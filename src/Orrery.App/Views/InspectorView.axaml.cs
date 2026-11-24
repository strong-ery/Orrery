using Avalonia.Controls;
using Orrery.App.ViewModels;

namespace Orrery.App.Views;

public partial class InspectorView : UserControl
{
    public InspectorView() => InitializeComponent();

    /// <summary>The goals this one could start after, built fresh each time the menu opens. Picking one adds it straight away.</summary>
    private void OnStartAfterOpening(object? sender, EventArgs e)
    {
        if (DataContext is not InspectorViewModel vm || sender is not MenuFlyout menu)
            return;
        menu.Items.Clear();
        foreach (var option in vm.DependencyCandidates)
        {
            var item = new MenuItem { Header = option.Label };
            var id = option.Id;
            item.Click += (_, _) =>
            {
                if (id is { } goal)
                    vm.WaitFor(goal);
            };
            menu.Items.Add(item);
        }
    }
}
