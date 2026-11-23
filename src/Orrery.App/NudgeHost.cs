using Avalonia.Threading;
using Orrery.App.ViewModels;
using Orrery.App.Views;
using Orrery.Core.Engine;
using Orrery.Core.Storage;

namespace Orrery.App;

/// <summary>Checks once a minute whether anything is worth a nudge, and shows at most one at a time.</summary>
public sealed class NudgeHost
{
    private readonly MainViewModel _main;
    private readonly Action<Guid, Guid?> _open;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private ToastWindow? _showing;

    public NudgeHost(MainViewModel main, Action<Guid, Guid?> open)
    {
        _main = main;
        _open = open;
        _timer.Tick += (_, _) => Check();
    }

    public void Start() => _timer.Start();

    public void Check()
    {
        // Never interrupt a focus session or stack a second card on the first.
        if (_main.Focus.IsVisible || _showing is not null)
            return;
        if (Nudges.Due(_main.Data, DateTime.Now).FirstOrDefault() is not { } nudge)
            return;

        _main.Data.NudgeLogFor(nudge.GoalId, DateTime.Now);
        JsonStore.Save(_main.Data);
        Show(nudge.Urgent ? "Due today" : "Still on today's list", nudge.Message, () => _open(nudge.GoalId, nudge.StepId));
    }

    public void Show(string heading, string message, Action? open, string openLabel = "Open")
    {
        var toast = new ToastWindow(heading, message, open, openLabel);
        toast.Closed += (_, _) => _showing = null;
        _showing = toast;
        toast.Show();
        if (_main.Data.Settings.NotifySound)
            Chime.Play(_main.Data.Settings);
    }
}
