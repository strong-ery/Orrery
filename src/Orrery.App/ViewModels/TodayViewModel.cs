using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

public partial class PlanRowViewModel : ObservableObject
{
    private readonly PlanItem _item;
    private readonly MainViewModel _main;

    public PlanRowViewModel(PlanItem item, Goal goal, MainViewModel main)
    {
        _item = item;
        _main = main;
        Title = item.Title;
        GoalTitle = item.StepId is null ? "" : item.GoalTitle;
        Reason = item.Reason;
        CategoryName = item.Category?.Name ?? "";
        Accent = Palette.For(item.Category);
        EnergyLabel = item.Energy switch { Energy.Low => "low energy", Energy.High => "high energy", _ => "medium energy" };
        EstimateLabel = item.EstimateMinutes is { } m ? $"~{m} min" : "";
        Meta = string.Join(" · ", new[] { EnergyLabel, EstimateLabel }.Where(s => s.Length > 0));

        ShowAmount = goal.Measure.Kind is MeasureKind.Amount or MeasureKind.Time;
        UnitLabel = goal.Measure.UnitLabel;
        var suggested = goal.Measure.Kind == MeasureKind.Time
            ? item.EstimateMinutes ?? 25
            : Math.Max(1, Math.Round(item.Demand.Remaining));
        AmountText = suggested.ToString("0.#", CultureInfo.InvariantCulture);
        ActionLabel = ShowAmount ? (item.StepId is null ? "Log" : "Done") : "Done";
        CanStart = !main.Focus.IsVisible;
    }

    public string Title { get; }
    public string GoalTitle { get; }
    public string Reason { get; }
    public string CategoryName { get; }
    public IBrush Accent { get; }
    public string EnergyLabel { get; }
    public string EstimateLabel { get; }
    public string Meta { get; }
    public bool HasGoalTitle => GoalTitle.Length > 0;
    public bool ShowAmount { get; }
    public string UnitLabel { get; }
    public string ActionLabel { get; }
    public bool CanStart { get; }

    [ObservableProperty]
    private string _amountText = "";

    [RelayCommand]
    private void Complete()
    {
        var amount = 1.0;
        if (ShowAmount && !double.TryParse(AmountText, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
            amount = 0;
        _main.Complete(_item, amount);
    }

    [RelayCommand]
    private void Start() => _main.StartFocus(_item.GoalId, _item.StepId);

    [RelayCommand]
    private void Inspect() => _main.Inspect(_item.GoalId, _item.StepId);
}

public sealed class StreakCardViewModel
{
    public StreakCardViewModel(Category category, StreakResult streak)
    {
        Name = category.Name;
        Accent = Palette.For(category);
        Current = streak.Current;
        Best = streak.Best;
        TodayMet = streak.TodayMet;
        Grace = streak.GraceAvailable
            ? "grace day ready"
            : $"grace used {streak.LastGraceUsed:MMM d}";
    }

    public string Name { get; }
    public IBrush Accent { get; }
    public int Current { get; }
    public int Best { get; }
    public bool TodayMet { get; }
    public string Grace { get; }
    public string Detail => $"best {Best} · {Grace}";
}

public partial class TodayViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _loading = true;

    public TodayViewModel(MainViewModel main)
    {
        _main = main;
        _bias = main.Data.Settings.Bias;
        _lowEnergy = main.Data.Settings.LowEnergy;
        _loading = false;
    }

    /// <summary>The single most useful thing to do next, shown large.</summary>
    [ObservableProperty]
    private PlanRowViewModel? _hero;

    public ObservableCollection<PlanRowViewModel> Rest { get; } = [];
    public ObservableCollection<StreakCardViewModel> Streaks { get; } = [];

    [ObservableProperty]
    private string _heading = "";

    [ObservableProperty]
    private string _subheading = "";

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasRest;

    [ObservableProperty]
    private double _dayProgress;

    [ObservableProperty]
    private bool _hasDayProgress;

    [ObservableProperty]
    private double _bias;

    [ObservableProperty]
    private bool _lowEnergy;

    public string BiasLabel => Bias switch
    {
        <= -0.66 => "Obligations first",
        <= -0.2 => "Leaning obligations",
        < 0.2 => "Balanced",
        < 0.66 => "Leaning passions",
        _ => "Passions first",
    };

    partial void OnBiasChanged(double value)
    {
        OnPropertyChanged(nameof(BiasLabel));
        Apply();
    }

    partial void OnLowEnergyChanged(bool value) => Apply();

    private void Apply()
    {
        if (_loading)
            return;
        _main.Data.Settings.Bias = Bias;
        _main.Data.Settings.LowEnergy = LowEnergy;
        _main.Changed();
    }

    public void Refresh()
    {
        var today = MainViewModel.Today;
        var data = _main.Data;
        Heading = today.ToString("dddd, MMM d", CultureInfo.CurrentCulture);

        var plan = Planner.Suggest(data, new PlanRequest(today, data.Settings.MaxToday, Bias, LowEnergy));
        var rows = plan
            .Select(item => new PlanRowViewModel(item, data.Goals.First(g => g.Id == item.GoalId), _main))
            .ToList();

        Hero = rows.FirstOrDefault();
        Rest.Clear();
        foreach (var row in rows.Skip(1))
            Rest.Add(row);
        HasRest = Rest.Count > 0;
        IsEmpty = rows.Count == 0;
        var done = data.Contributions.Where(c => c.Day == today).Select(c => c.GoalId).Distinct().Count();
        var total = done + rows.Count;
        DayProgress = total == 0 ? 0 : 100.0 * done / total;
        HasDayProgress = total > 0;
        Subheading = IsEmpty
            ? (done > 0 ? $"{done} done today. Nothing else is asking for you." : LowEnergy ? "Nothing low-energy is waiting. Rest counts." : "Nothing is asking for you today.")
            : $"{done} done · {rows.Count} to go{(LowEnergy ? " · low-energy mode" : "")}";

        Streaks.Clear();
        foreach (var category in data.Categories)
            Streaks.Add(new StreakCardViewModel(category, Core.Engine.Streaks.For(category, data, today)));
    }
}
