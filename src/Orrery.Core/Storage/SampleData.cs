using Orrery.Core.Design;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.Core.Storage;

/// <summary>
/// A first-run example set, built through the same designer the app exposes, so it doubles as a demonstration
/// that very different goals come from the same few answers.
/// </summary>
public static class SampleData
{
    public static OrreryData Create(DateOnly today)
    {
        var data = OrreryData.CreateEmpty();

        var laundry = Add(data, today, new DesignAnswers
        {
            Title = "Do laundry", Role = CategoryRole.Routine, Measure = MeasureKind.Done, HasFinishLine = false,
            Rhythm = RhythmKind.Repeat, EveryDays = 7, Energy = Energy.Low,
        });
        laundry.Created = today.AddDays(-3);

        var prism = Add(data, today, new DesignAnswers
        {
            Title = "Finish Prism", Role = CategoryRole.Passion, Measure = MeasureKind.Time, HasFinishLine = false,
            Rhythm = RhythmKind.Cadence, PerWeek = 240,
        });
        prism.Created = today;
        prism.Steps =
        [
            new Step { Title = "Sketch the project file format", Energy = Energy.High, EstimateMinutes = 60 },
            new Step { Title = "Rename the sample assets", Energy = Energy.Low, EstimateMinutes = 15 },
            new Step { Title = "Wire up the BYML viewer", Energy = Energy.Medium, EstimateMinutes = 45 },
        ];

        var school = Add(data, today, new DesignAnswers
        {
            Title = "Catch up on schoolwork", Role = CategoryRole.Obligation, Measure = MeasureKind.Amount, Unit = "%",
            Target = 75, HasFinishLine = true, Deadline = today.AddDays(21),
        });
        school.Created = today.AddDays(-1);
        data.Contributions.Add(new Contribution { GoalId = school.Id, Day = today.AddDays(-1), Amount = 38 });

        var bio = Add(data, today, new DesignAnswers
        {
            Title = "Biology 101", Role = CategoryRole.Obligation, Measure = MeasureKind.Steps, HasFinishLine = true,
            Deadline = today.AddDays(28), Energy = Energy.Medium,
        });
        bio.Steps = Enumerable.Range(1, 4)
            .Select(n => new Step { Title = $"Unit {n}", Energy = Energy.Medium, EstimateMinutes = 90 })
            .ToList();
        DependencyGraph.Chain(bio.Steps);

        return data;
    }

    private static Goal Add(OrreryData data, DateOnly today, DesignAnswers answers)
    {
        var result = GoalDesigner.Build(data, answers, today);
        var goal = result.Goal ?? throw new InvalidOperationException(string.Join(" ", result.Issues.Select(i => i.Message)));
        data.Goals.Add(goal);
        return goal;
    }
}
