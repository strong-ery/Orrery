using Orrery.Core.Design;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.Core.Storage;

/// <summary>
/// An example set for tests, built through the same designer the app exposes, so it doubles as a demonstration
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
        var format = new Step { Title = "Project file format" };
        var viewer = new Step { Title = "BYML viewer" };
        prism.Steps =
        [
            format,
            new Step { Title = "Sketch the layout", ParentId = format.Id, Energy = Energy.High, EstimateMinutes = 60 },
            new Step { Title = "Rename the sample assets", ParentId = format.Id, Energy = Energy.Low, EstimateMinutes = 15 },
            viewer,
            new Step { Title = "Parse the header", ParentId = viewer.Id, Energy = Energy.Medium, EstimateMinutes = 45 },
            new Step { Title = "Show nodes in a tree", ParentId = viewer.Id, Energy = Energy.Medium, EstimateMinutes = 60 },
        ];
        DependencyGraph.Chain(new List<Step> { format, viewer });

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
        // Each unit is a section of small steps, and each unit waits for the one before it.
        var units = new List<Step>();
        for (var n = 1; n <= 4; n++)
        {
            var unit = new Step { Title = $"Unit {n}", Energy = Energy.Medium };
            units.Add(unit);
            bio.Steps.Add(unit);
            bio.Steps.Add(new Step { Title = "Read the chapter", ParentId = unit.Id, Energy = Energy.Medium, EstimateMinutes = 40 });
            bio.Steps.Add(new Step { Title = "Write up notes", ParentId = unit.Id, Energy = Energy.Low, EstimateMinutes = 20 });
            bio.Steps.Add(new Step { Title = "Practice questions", ParentId = unit.Id, Energy = Energy.High, EstimateMinutes = 30 });
        }
        DependencyGraph.Chain(units);

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
