using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

/// <summary>
/// The "I'm working on this now" bar. Start from anywhere (Today, the inspector, a step), stop, and say what
/// came of it. The time lands on the goal, so what you did and when you did it come from one action.
/// </summary>
public partial class FocusViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer;
    private int _wrapMinutes;

    public FocusViewModel(MainViewModel main)
    {
        _main = main;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateElapsed();

        // A session that was running when the app closed picks up where it left off.
        if (main.Data.Settings.Focus is { } saved && main.FindGoal(saved.GoalId) is not null)
            Begin(saved);
        else
            main.Data.Settings.Focus = null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(CanStart))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(CanStart))]
    private bool _isWrapUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    private bool _isPaused;

    [ObservableProperty] private string _title = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoalTitle))]
    private string _goalTitle = "";

    public bool HasGoalTitle => GoalTitle.Length > 0;
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private double _ring;
    [ObservableProperty] private string _paceText = "";
    private int? _estimateMinutes;
    [ObservableProperty] private string _tag = "";
    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private bool _showAmount;
    [ObservableProperty] private string _amountUnit = "";
    [ObservableProperty] private bool _showFinish;
    [ObservableProperty] private string _finishLabel = "";
    [ObservableProperty] private bool _finished;
    [ObservableProperty] private string _wrapText = "";

    public bool IsVisible => IsActive || IsWrapUp;
    public bool CanStart => !IsVisible;
    public string PauseLabel => IsPaused ? "Resume" : "Pause";

    private FocusState? State => _main.Data.Settings.Focus;

    public void Start(Guid goalId, Guid? stepId)
    {
        if (IsVisible || _main.FindGoal(goalId) is null)
            return;
        var state = new FocusState { GoalId = goalId, StepId = stepId, StartedAt = DateTime.Now };
        _main.Data.Settings.Focus = state;
        Begin(state);
        Save();
    }

    private void Begin(FocusState state)
    {
        var goal = _main.FindGoal(state.GoalId)!;
        var step = state.StepId is { } id ? goal.Steps.FirstOrDefault(s => s.Id == id) : null;
        Title = step?.Title ?? goal.Title;
        GoalTitle = step is null ? "" : step.ParentId is not null && goal.ParentOf(step) is { } sec ? $"{goal.Title} › {sec.Title}" : goal.Title;
        Tag = state.Tag;
        _estimateMinutes = step?.EstimateMinutes;
        IsPaused = state.PausedAt is not null;
        IsWrapUp = false;
        IsActive = true;
        UpdateElapsed();
        _timer.Start();
    }

    private void UpdateElapsed()
    {
        if (State is not { } state)
            return;
        var t = state.Elapsed(DateTime.Now);
        ElapsedText = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";

        // With an estimate the ring fills toward it. Without one it turns once every 25 minutes, a gentle
        // marker of time passing rather than a target.
        if (_estimateMinutes is { } est && est > 0)
        {
            Ring = Math.Min(100, t.TotalMinutes / est * 100);
            var left = est - (int)t.TotalMinutes;
            PaceText = left > 0 ? $"about {left} min left of ~{est}" : $"{-left} min over the ~{est} min estimate";
        }
        else
        {
            Ring = t.TotalSeconds % 1500 / 1500 * 100;
            PaceText = t.TotalMinutes >= 25 ? $"{(int)(t.TotalMinutes / 25)} × 25 min so far" : "no estimate on this one";
        }
    }

    partial void OnTagChanged(string value)
    {
        if (State is { } s && s.Tag != value)
        {
            s.Tag = value;
            Save();
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (State is not { } s)
            return;
        if (s.PausedAt is { } at)
        {
            s.PausedSeconds += (DateTime.Now - at).TotalSeconds;
            s.PausedAt = null;
            IsPaused = false;
        }
        else
        {
            s.PausedAt = DateTime.Now;
            IsPaused = true;
        }
        UpdateElapsed();
        Save();
    }

    [RelayCommand]
    private void Stop()
    {
        if (State is not { } s)
            return;
        _timer.Stop();
        _wrapMinutes = Math.Max(1, (int)Math.Round(s.Elapsed(DateTime.Now).TotalMinutes));

        var goal = _main.FindGoal(s.GoalId)!;
        var kind = goal.Measure.Kind;
        ShowAmount = kind == MeasureKind.Amount;
        AmountUnit = goal.Measure.UnitLabel;
        AmountText = "";
        ShowFinish = s.StepId is not null || kind == MeasureKind.Done;
        FinishLabel = s.StepId is not null ? "I finished this step" : "I finished it";
        Finished = false;
        WrapText = $"{_wrapMinutes} min on {Title}";
        IsActive = false;
        IsWrapUp = true;
    }

    [RelayCommand]
    private void SaveSession()
    {
        if (State is not { } s)
            return;
        var goal = _main.FindGoal(s.GoalId)!;
        var kind = goal.Measure.Kind;

        double amount = kind switch
        {
            MeasureKind.Time => _wrapMinutes,
            MeasureKind.Amount => double.TryParse(AmountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0,
            _ => Finished ? 1 : 0,
        };

        if (Finished && s.StepId is { } stepId && goal.Steps.FirstOrDefault(x => x.Id == stepId) is { } step)
            step.Done = true;

        _main.Data.Contributions.Add(new Contribution
        {
            GoalId = goal.Id,
            StepId = s.StepId,
            Day = MainViewModel.Today,
            Amount = amount,
            Minutes = _wrapMinutes,
            Focus = Tag,
        });
        Clear();
        _main.Changed();
    }

    [RelayCommand]
    private void Discard()
    {
        Clear();
        _main.Changed();
    }

    /// <summary>Drops a running session without logging it, for when its goal has been deleted.</summary>
    public void Abandon() => Clear();

    private void Clear()
    {
        _timer.Stop();
        _main.Data.Settings.Focus = null;
        IsActive = false;
        IsWrapUp = false;
        IsPaused = false;
        Tag = "";
    }

    private void Save() => Core.Storage.JsonStore.Save(_main.Data);
}
