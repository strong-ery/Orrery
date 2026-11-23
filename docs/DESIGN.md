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

## Goal > section > step

A goal holds sections, and sections hold steps. A section is just a step that other steps name as their
parent (`Step.ParentId`, or `IsGroup` for an empty one), so storage stays one flat list (`StepTree`).
Only the innermost steps are ever worked on or counted. A section is finished when everything in it is, its
estimate is the sum of its steps, and what it waits for, its steps wait for too. So "Unit 2 waits for Unit 1" is
one link, not twelve. Biology 101 in the sample data is four units, each with read / notes / practice.

## The orbit map

Goals page. Rings are *how soon it will want you*: Now, This week, This month, Later (`Horizons.Of`). The three
tinted sectors are the buckets, so a lopsided life shows as a lopsided map. A planet's size is its pressure and
the arc around it is progress. Moons are its sections: solid when finished, ringed when open, faint when waiting.
Dashed arcs with arrowheads are goal-level dependencies and light up when you point at either end. The core
shows how many things are on today's list. Positions depend only on bucket, ring and name, so nothing jumps
between visits; the only motion is a slow drift of the moons and a breathing glow on whatever is pressing.

## Waiting (dependency graph)

The third Goals view. Left to right: what has to finish sits left of what waits on it. Boxes are sections,
loose steps, or whole goals; only things that take part in a dependency are drawn (`FlowGraph`). Pointing at a
box lights its whole chain, up and down. Click opens it in the inspector.

## Nudges

`Nudges.Due` is deliberately stingy: nothing before 10:00 or after 21:30, only what is genuinely due or about
to slip (anything softer waits until 17:00), a cool-down per goal, at most four a day, and never during a focus
session. They appear as a small card in the screen corner. Closing the window puts Orrery in the tray so
nudges still arrive; "Quit" in the tray menu exits, and the same menu turns nudges off.

## Calendar

Month grid plus an agenda for the selected day and a "coming up" list (`CalendarModel`). It only shows what the
other parts already know: deadlines, the day each repeating goal next comes due (the end of its window, and not
at all if that window is already done), the day a held-back step opens up, and, for past days, a small brass dot
where something was worked on. Click anything to open it in the inspector.

## Keeping it calm

One accent (brass) carries meaning: today, progress, deadlines. Bucket colours are muted and appear only as a small
dot or a thin tab, never as coloured text. Danger red is gone from lists. Detail lives in the inspector and
tooltips, not on the page you scan. The goal designer asks plain questions with tiles instead of dropdowns, and
the right-hand panel restates the answers as sentences.

## Not built yet

* "Put off" on a step, then offering to split it after three defers (`Step.DeferCount` exists)
* Reordering steps, editing a step after adding it, moving a step between sections
* Real graphs on History (it has streak ribbons and last-7-days effort bars) and click-through from them
* An `.ico` for the exe (the tray and window icon are drawn at runtime from the same mark)
* A weekly review screen

## Visual theme

Same family as str-amp (near-black ground, translucent tinted surfaces, rounded corners, soft shadows, same
token names such as `AppAccentBrush` and `AppSubtleBackgroundBrush`) with a different palette: deep indigo
space and a brass accent, in place of str-amp's violet. Each bucket has its own colour (coral, violet, teal)
which is data on the category, not hard-coded in views.
