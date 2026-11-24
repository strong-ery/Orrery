using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Orrery.App.Views;

/// <summary>
/// A small month calendar for picking one date: arrows to change month, today ringed, the chosen day filled in gold,
/// and a Clear / Today pair underneath. Meant to sit inside a flyout.
/// </summary>
public sealed class MiniCalendar : Border
{
    public static readonly StyledProperty<DateOnly?> SelectedDateProperty =
        AvaloniaProperty.Register<MiniCalendar, DateOnly?>(nameof(SelectedDate), defaultBindingMode: BindingMode.TwoWay);

    public DateOnly? SelectedDate { get => GetValue(SelectedDateProperty); set => SetValue(SelectedDateProperty, value); }

    private DateOnly _month;
    private readonly TextBlock _title = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, FontSize = 13 };
    private readonly UniformGrid _days = new() { Columns = 7 };

    public MiniCalendar()
    {
        Padding = new Thickness(6);
        Width = 262;
        _month = First(SelectedDate ?? DateOnly.FromDateTime(DateTime.Today));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        header.Children.Add(Arrow(-1, 90));
        header.Children.Add(_title);
        Grid.SetColumn(_title, 1);
        var next = Arrow(1, -90);
        header.Children.Add(next);
        Grid.SetColumn(next, 2);

        var names = new UniformGrid { Columns = 7 };
        var first = Orrery.Core.Engine.WeekStart.First;
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)first + i) % 7);
            names.Children.Add(new TextBlock
            {
                Text = CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(day),
                FontSize = 11, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4),
            });
        }

        var clear = new Button { Content = "Clear", Classes = { "ghost" } };
        clear.Click += (_, _) => Pick(null);
        var today = new Button { Content = "Today", Classes = { "ghost" } };
        today.Click += (_, _) => Pick(DateOnly.FromDateTime(DateTime.Today));
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        footer.Children.Add(clear);
        footer.Children.Add(today);
        Grid.SetColumn(today, 2);

        Child = new StackPanel { Spacing = 4, Children = { header, names, _days, footer } };
        Rebuild();
    }

    private static DateOnly First(DateOnly d) => new(d.Year, d.Month, 1);

    private Button Arrow(int by, double turn)
    {
        var button = new Button
        {
            Classes = { "icon" },
            Content = new PathIcon { Data = (Geometry)Application.Current!.FindResource("IconDown")!, RenderTransform = new RotateTransform(turn) },
        };
        button.Click += (_, _) =>
        {
            _month = _month.AddMonths(by);
            Rebuild();
        };
        return button;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedDateProperty)
        {
            if (SelectedDate is { } d)
                _month = First(d);
            Rebuild();
        }
    }

    private void Pick(DateOnly? day)
    {
        SelectedDate = day;
        // The picker lives in a flyout; choosing a day is the end of the interaction.
        if (TopLevel.GetTopLevel(this) is PopupRoot { Parent: Popup popup })
            popup.IsOpen = false;
    }

    private void Rebuild()
    {
        _title.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        _days.Children.Clear();
        var firstDay = Orrery.Core.Engine.WeekStart.First;
        var lead = ((int)_month.DayOfWeek - (int)firstDay + 7) % 7;
        var start = _month.AddDays(-lead);
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var button = new Button { Content = day.Day.ToString(CultureInfo.InvariantCulture), Classes = { "mini-day" } };
            button.Classes.Set("dim", day.Month != _month.Month);
            button.Classes.Set("today", day == today);
            button.Classes.Set("sel", day == SelectedDate);
            button.Click += (_, _) => Pick(day);
            _days.Children.Add(button);
        }
    }
}
