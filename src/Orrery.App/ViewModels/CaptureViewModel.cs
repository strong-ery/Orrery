using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Design;

namespace Orrery.App.ViewModels;

/// <summary>The quick-add box in the title bar: type a line, see what it would do, press Enter.</summary>
public partial class CaptureViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private CapturePreview _preview = new(CaptureKind.None, "", "", [], null);

    public CaptureViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<string> Chips { get; } = [];

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _problem = "";
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _hasProblem;
    [ObservableProperty] private bool _canCommit;

    public string Help =>
        "in Goal · after Step · due dec 10 · from fri · ~45m · !low · #passion · daily · 3h/week";

    partial void OnTextChanged(string value)
    {
        _preview = QuickCapture.Parse(_main.Data, value, MainViewModel.Today);
        Summary = _preview.Summary;
        Problem = _preview.Problem ?? "";
        HasProblem = _preview.Problem is not null;
        CanCommit = _preview.CanApply;
        Chips.Clear();
        foreach (var chip in _preview.Chips)
            Chips.Add(chip);
        IsOpen = value.Length > 0;
    }

    [RelayCommand]
    private void Commit()
    {
        if (!_preview.CanApply)
            return;
        var goal = QuickCapture.Apply(_main.Data, _preview, MainViewModel.Today);
        var step = _preview.Kind == CaptureKind.NewStep ? goal.Steps.LastOrDefault() : null;
        Text = "";
        _main.Changed();
        _main.Inspect(goal.Id, step?.Id);
    }

    [RelayCommand]
    private void Cancel() => Text = "";
}
