using Avalonia.Controls;

namespace Orrery.App.Views;

public partial class CalendarView : UserControl
{
    private bool _stacked;

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
            Grid.SetColumn(Agenda, 0);
            Grid.SetRow(Agenda, 1);
            Grid.SetRowSpan(Agenda, 1);
            Grid.SetRowSpan(Month, 1);
            Agenda.MaxHeight = 230;
            Agenda.Margin = new Avalonia.Thickness(0, 14, 0, 0);
        }
        else
        {
            Root.ColumnDefinitions[1].Width = new GridLength(300);
            Root.ColumnSpacing = 28;
            Grid.SetColumn(Agenda, 1);
            Grid.SetRow(Agenda, 0);
            Grid.SetRowSpan(Agenda, 2);
            Grid.SetRowSpan(Month, 2);
            Agenda.MaxHeight = double.PositiveInfinity;
            Agenda.Margin = default;
        }
    }
}
