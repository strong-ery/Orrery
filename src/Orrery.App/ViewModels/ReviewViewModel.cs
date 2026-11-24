using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;

namespace Orrery.App.ViewModels;

public sealed record ReviewLine(string Text, string Detail)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>
/// The weekly review: last week, the good and the bad, then a chance to put things in the week that has just begun.
/// </summary>
public sealed partial class ReviewViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ReviewViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<Stat> Stats { get; } = [];
    public ObservableCollection<ReviewLine> WentWell { get; } = [];
    public ObservableCollection<ReviewLine> Slipped { get; } = [];
    public ObservableCollection<ReviewLine> Ahead { get; } = [];

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _aheadTitle = "";
    [ObservableProperty] private bool _quietWeek;
    [ObservableProperty] private bool _nothingSlipped;
    [ObservableProperty] private bool _nothingAhead;

    public void Refresh()
    {
        var review = WeeklyReview.Build(_main.Data, MainViewModel.Today);
        var culture = CultureInfo.CurrentCulture;
        Title = $"{review.From.ToString("MMM d", culture)} to {review.To.ToString("MMM d", culture)}";
        AheadTitle = $"This week, {review.NextFrom.ToString("MMM d", culture)} to {review.NextTo.ToString("MMM d", culture)}";

        Stats.Clear();
        Stats.Add(new Stat($"{review.DaysShowedUp} / 7", "days with something done"));
        Stats.Add(new Stat(review.ThingsDone.ToString(CultureInfo.InvariantCulture), review.ThingsDone == 1 ? "thing done" : "things done"));
        Stats.Add(new Stat(review.Minutes >= 60 ? $"{review.Minutes / 60:0.#} h" : $"{review.Minutes:0} min", "of focused time"));

        Fill(WentWell, review.WentWell);
        Fill(Slipped, review.Slipped);
        Fill(Ahead, review.Ahead);
        QuietWeek = review.WasQuiet;
        NothingSlipped = review.Slipped.Count == 0;
        NothingAhead = review.Ahead.Count == 0;
    }

    private static void Fill(ObservableCollection<ReviewLine> into, IEnumerable<ReviewNote> notes)
    {
        into.Clear();
        foreach (var n in notes)
            into.Add(new ReviewLine(n.Text, n.Detail));
    }

    [RelayCommand]
    private void AddSomething() => _main.Capture.Open();

    [RelayCommand]
    private void Finish()
    {
        WeeklyReview.MarkSeen(_main.Data, MainViewModel.Today);
        _main.Changed();
        _main.ShowTodayCommand.Execute(null);
    }
}
