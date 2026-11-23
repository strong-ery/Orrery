# Orrery: design notes

Orrery tracks long-term and short-term goals in one place, keeps what you *have* to do apart from what you
*want* to do, and turns both into a short list for today. It is local-first (one JSON file, no account, no
network) and Windows-desktop first (Avalonia).

## The core idea: goals are answers, not types

There is no "Task", "Quota" or "Project" class. A goal is a point in a small space defined by five answers
(see `DesignAnswers` in `Orrery.Core/Design/GoalDesigner.cs`):

| Question | Options | Effect |
|---|---|---|
| Which bucket? | Obligations / Passions / Routines | Balancing and streaks |
| Does it finish? | finish line / never | Whether it has a percentage, whether anything can wait on it |
| How is progress measured? | Done / Steps / Amount (any unit) / Time | What a contribution is |
| Does it repeat? | no / every N days / amount per week | Where its daily demand comes from |
| Is there a deadline? | date or none | Pace needed per day |

Everything the app does reads a goal through one function, `Demands.For(goal, log, day)`, which returns what
that goal asks of you *today, in its own unit*. The planner, the nudges, the streaks and the graphs read
demands and never ask what kind of goal they came from. A new kind of goal is a new combination of answers,
not new code.

Three examples that shaped it:

* **Do laundry**: Routines, Done, repeats every 7 days. Wanted all week, enforced on the last day of the window.
* **Finish Prism**: Passions, Time, never finishes, 240 min a week. Asks nothing early in the week, then
  starts asking once the week is running out. Progress is shown as time, never a percentage.
* **Catch up to 75 %**: Obligations, Amount in %, finishes, has a deadline. Asks for `remaining / days left`
  every day, so skipping a day makes tomorrow's ask bigger.

## Today's list

`Planner.Suggest` scores each goal's *next available step* (or the goal itself when it has none) by pressure
(0 to 1) plus a small bonus for how many steps it unblocks. Two dials: **bias** (obligations ↔ passions) and
**low-energy mode** (only Low steps, from every goal, passions included). Anything with pressure ≥ 0.85 is on
the list regardless of bias. A passion keeps a protected seat unless bias is strongly toward obligations.

## Dependencies

Steps depend on steps (across goals), and goals depend on goals ("all of that first"). They share one graph
(`DependencyGraph`) so cycles are caught wherever they hide. Nothing can wait on a goal that never finishes.
Blocked steps never appear on Today; the step holding the most others up gets a bonus. A step can also be
blocked until a date. `DependencyGraph.Chain` is the "do these in order" shortcut (the bio-class units).

## Streaks

Per bucket, not per goal. A day is *Met* when everything the bucket asked that day was done, *Missed* if
something enforced wasn't, and *Rest* if nothing was asked (rest days neither count nor break). One miss in
any rolling 7 days is forgiven.

## How the pieces connect

Everything you can do to a goal happens in one place, and every list opens that place.

* **Inspector** (right panel). Click a goal or step on Today, in the orbit map or in the list and the same
  panel opens: progress, start working, log progress, steps with what each waits for, goal-level "waits for",
  edit, archive. You never change page to act on something.
* **Focus bar** (bottom). "Start" on anything runs a timer. Stop, say what came of it (amount made, step
  finished), and one action records the time *and* the progress. A running session survives closing the app.
  Time is stored on every contribution (`Contribution.Minutes`) whatever the goal is measured in.
* **Quick add** (title bar, Ctrl+K). One line becomes a step or a goal, and a preview says exactly what will
  happen before Enter. `Unit 5 in Biology after Unit 4 ~90m` adds a step under Biology that waits for Unit 4.
  `Renew passport due dec 20` makes a goal. A line it can't place is reported, never guessed at. Tokens:
  `in X` or `X:` (which goal), `after X`, `due/by DATE`, `from DATE`, `~45m`, `!low`/`!high`,
  `#passion`/`#routine`, `daily`, `weekly`, `every 3 days`, `3h/week`. Lives in `QuickCapture` and is tested.
* **Today** leads with one thing, large, then the rest as quiet lines. Not a wall of equal cards.

## The orbit map

Goals page, with a one-click switch to a plain list (same goals, same grouping). Rings are *how soon it will
want you*: Now, This week, This month, Later (`Horizons.Of`). The three sectors are the buckets, so a lopsided
life shows as a lopsided map. A body's size is its pressure and the arc around it is progress. Positions depend
only on bucket, ring and name, so nothing moves between visits. Labels avoid each other and hide when they
can't fit, and hovering any body shows what it needs. The point is to see long-term and short-term in one view
and click straight into the thing, not to look at it.

## Not built yet

* Nudges: tray icon plus toast notifications when a day's demand is behind pace (the data is there:
  `Demand.Remaining` and `Enforced`)
* "Put off" on a step, then offering to split it after three defers (`Step.DeferCount` exists)
* A dependency graph view, and editing step-to-step dependencies other than via "after" and "Do in order"
* Real graphs on History (it has streak ribbons and last-7-days effort bars) and click-through from them
* Logo as an `.ico` for the exe and tray (the mark exists as `OrreryMark.axaml`)
* A weekly review screen
* Reordering steps, editing a step after adding it

## Visual theme

Same family as str-amp (near-black ground, translucent tinted surfaces, rounded corners, soft shadows, same
token names such as `AppAccentBrush` and `AppSubtleBackgroundBrush`) with a different palette: deep indigo
space and a brass accent, in place of str-amp's violet. Each bucket has its own colour (coral, violet, teal)
which is data on the category, not hard-coded in views.
