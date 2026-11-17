using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Workflows;

/// <summary>How the editor shows a kind of node and which parameters it has.</summary>
/// <param name="Color">The node's accent (ARGB).</param>
/// <param name="Icon">SVG path data on a 24×24 grid (<see cref="Contracts.Icons"/>).</param>
public sealed record WorkflowNodeType(
    string Kind,
    string Name,
    string Description,
    uint Color,
    string Icon,
    IReadOnlyList<ActionSettingDefinition> Settings);

/// <summary>The built-in node kinds, their parameters, and how a node is summed up in one line.</summary>
public static class WorkflowNodeTypes
{
    private static readonly NumberRange WaitRange = new(0, 10_000, 50) { Unit = "ms" };

    /// <summary>Whose variable a node means: shared by everything, this profile's, or this control's.</summary>
    private static ActionSettingDefinition ScopeSetting(SettingCondition? showWhen) =>
        new(WorkflowKeys.Scope, "Shared by", ActionSettingKind.Choice, WorkflowKeys.ScopeGlobal, "Every workflow (global), only the workflows of this profile, or only this control's workflow.", Options:
        [
            new(WorkflowKeys.ScopeGlobal, "every workflow"),
            new(WorkflowKeys.ScopeProfile, "this profile's workflows"),
            new(WorkflowKeys.ScopeControl, "this control only"),
        ])
        {
            ShowWhen = showWhen,
        };

    public static WorkflowNodeType Trigger { get; } = new(
        WorkflowNodeKinds.Trigger,
        "When",
        "Starts the workflow when this happens on the control.",
        0xFF22C55E,
        Contracts.Icons.Bolt,
        [
            new(WorkflowKeys.On, "Starts when", ActionSettingKind.Choice, WorkflowTriggers.Down, Options:
            [
                new(WorkflowTriggers.Down, "Pressed (right away)") { Requires = InputCapabilities.Press },
                new(WorkflowTriggers.Up, "Released") { Requires = InputCapabilities.Release },
                new(WorkflowTriggers.Tap, "Tapped (not held; waits for a double press if there is one)") { Requires = InputCapabilities.Release },
                new(WorkflowTriggers.DoublePress, "Double pressed") { Requires = InputCapabilities.Release },
                new(WorkflowTriggers.Hold, "Held down") { Requires = InputCapabilities.LongPress },
                new(WorkflowTriggers.RotateLeft, "Turned left") { Requires = InputCapabilities.Rotate },
                new(WorkflowTriggers.RotateRight, "Turned right") { Requires = InputCapabilities.Rotate },
                new(WorkflowTriggers.SwipeLeft, "Swiped left") { Requires = InputCapabilities.Swipe },
                new(WorkflowTriggers.SwipeRight, "Swiped right") { Requires = InputCapabilities.Swipe },
                new(WorkflowTriggers.ProfileShown, "The profile comes onto the device"),
                new(WorkflowTriggers.AppInFront, "An app comes to the front"),
                new(WorkflowTriggers.VariableChanged, "A variable changes"),
                new(WorkflowTriggers.Timer, "Every so many seconds (while the profile is shown)"),
            ]),
            new(WorkflowKeys.App, "App name contains", Placeholder: "e.g. obs", Description: "Where DeckHost can see the app in front (Windows, macOS, Linux on X11).")
            {
                ShowWhen = new(WorkflowKeys.On, WorkflowTriggers.AppInFront),
            },
            new(WorkflowKeys.Name, "Variable", Placeholder: "e.g. mode", Description: "Names ignore case.")
            {
                ShowWhen = new(WorkflowKeys.On, WorkflowTriggers.VariableChanged),
            },
            ScopeSetting(new(WorkflowKeys.On, WorkflowTriggers.VariableChanged)),
            new(WorkflowKeys.Value, "Changes to", Placeholder: "any value", Description: "Leave empty to start on every change.")
            {
                ShowWhen = new(WorkflowKeys.On, WorkflowTriggers.VariableChanged),
            },
            new(WorkflowKeys.Every, "Every", ActionSettingKind.Number, "60", Description: "The workflow starts again this many seconds after it last started, while its profile is shown on the device.")
            {
                Range = new(WorkflowKeys.MinEverySeconds, WorkflowKeys.MaxEverySeconds, 1) { Unit = "s" },
                ShowWhen = new(WorkflowKeys.On, WorkflowTriggers.Timer),
            },
        ]);

    public static WorkflowNodeType Action { get; } = new(
        WorkflowNodeKinds.Action,
        "Action",
        "Runs an action. Drop an action from the list onto the canvas to add one.",
        0xFF3B82F6,
        Contracts.Icons.Play,
        []);

    /// <summary>What an action node does when its action did not run (shown next to the action's own settings).</summary>
    public static IReadOnlyList<ActionSettingDefinition> ActionFailureSettings { get; } =
    [
        new(WorkflowKeys.Retries, "Try again when it did not run", ActionSettingKind.Number, "0",
            Description: "How many more times to try. Then the run goes on at the node's \"On error\" output if it has a link, else at \"Then\".")
        {
            Range = new(0, WorkflowKeys.MaxRetries, 1) { Unit = "times" },
        },
        new(WorkflowKeys.RetryDelay, "Wait between tries", ActionSettingKind.Number, "1000")
        {
            Range = WaitRange,
        },
    ];

    public static WorkflowNodeType Wait { get; } = new(
        WorkflowNodeKinds.Wait,
        "Wait",
        "Waits, then continues.",
        0xFFF59E0B,
        Contracts.Icons.Timer,
        [new(WorkflowKeys.Milliseconds, "Wait", ActionSettingKind.Number, "500") { Range = WaitRange }]);

    public static WorkflowNodeType If { get; } = new(
        WorkflowNodeKinds.If,
        "If",
        "Continues at Yes or No.",
        0xFFA855F7,
        Contracts.Icons.Branch,
        [
            new(WorkflowKeys.Check, "Check", ActionSettingKind.Choice, WorkflowKeys.CheckVariable, Options:
            [
                new(WorkflowKeys.CheckVariable, "A variable"),
                new(WorkflowKeys.CheckApp, "The app in front"),
                new(WorkflowKeys.CheckTime, "The time of day"),
            ]),
            new(WorkflowKeys.Name, "Variable", Placeholder: "e.g. mode", Description: "Set it with a Set variable node; names ignore case.")
            {
                ShowWhen = new(WorkflowKeys.Check, WorkflowKeys.CheckVariable),
            },
            ScopeSetting(new(WorkflowKeys.Check, WorkflowKeys.CheckVariable)),
            new(WorkflowKeys.Operation, "Is", ActionSettingKind.Choice, WorkflowKeys.IsEqual, Options:
            [
                new(WorkflowKeys.IsEqual, "equal to"),
                new(WorkflowKeys.IsNotEqual, "not equal to"),
                new(WorkflowKeys.IsGreater, "greater than"),
                new(WorkflowKeys.IsLess, "less than"),
                new(WorkflowKeys.Contains, "containing"),
                new(WorkflowKeys.IsSet, "set (not empty)"),
                new(WorkflowKeys.IsEmpty, "empty"),
            ])
            {
                ShowWhen = new(WorkflowKeys.Check, WorkflowKeys.CheckVariable),
            },
            new(WorkflowKeys.Value, "Value", Placeholder: "text or number")
            {
                ShowWhen = new(WorkflowKeys.Check, WorkflowKeys.CheckVariable),
            },
            new(WorkflowKeys.App, "App name contains", Placeholder: "e.g. obs", Description: "Where DeckHost can see the app in front (Windows, macOS, Linux on X11).")
            {
                ShowWhen = new(WorkflowKeys.Check, WorkflowKeys.CheckApp),
            },
            new(WorkflowKeys.From, "From", Placeholder: "09:00")
            {
                ShowWhen = new(WorkflowKeys.Check, WorkflowKeys.CheckTime),
            },
            new(WorkflowKeys.To, "Until", Placeholder: "17:30", Description: "24-hour times; a range past midnight (22:00–06:00) works too.")
            {
                ShowWhen = new(WorkflowKeys.Check, WorkflowKeys.CheckTime),
            },
        ]);

    public static WorkflowNodeType Toggle { get; } = new(
        WorkflowNodeKinds.Toggle,
        "Toggle",
        "Takes 1st and 2nd in turn: on, then off.",
        0xFF14B8A6,
        Contracts.Icons.Toggle,
        []);

    public static WorkflowNodeType Repeat { get; } = new(
        WorkflowNodeKinds.Repeat,
        "Repeat",
        "Runs Each time round after round, then Done: a number of times, as many as a variable says, or while / until a variable condition holds, with an optional wait between rounds.",
        0xFFEC4899,
        Contracts.Icons.Repeat,
        [
            new(WorkflowKeys.Mode, "Repeat", ActionSettingKind.Choice, WorkflowKeys.ModeCount, Options:
            [
                new(WorkflowKeys.ModeCount, "a number of times"),
                new(WorkflowKeys.ModeVariable, "as many times as a variable says"),
                new(WorkflowKeys.ModeWhile, "while a variable condition is true"),
                new(WorkflowKeys.ModeUntil, "until a variable condition is true"),
            ]),
            new(WorkflowKeys.Count, "Times", ActionSettingKind.Number, "2") { Range = new NumberRange(1, 100), ShowWhen = new(WorkflowKeys.Mode, WorkflowKeys.ModeCount) },
            new(WorkflowKeys.Times, "Variable", Placeholder: "e.g. rounds", Description: "Its value (a whole number, up to 1000) is how many times. Set it with a Set variable node.")
            {
                ShowWhen = new(WorkflowKeys.Mode, WorkflowKeys.ModeVariable),
            },
            new(WorkflowKeys.Name, "Variable", Placeholder: "e.g. mode", Description: "Checked before every round; names ignore case. Stops after 10 000 rounds at most.")
            {
                ShowWhen = new(WorkflowKeys.Mode, WorkflowKeys.ModeWhile, WorkflowKeys.ModeUntil),
            },
            ScopeSetting(new(WorkflowKeys.Mode, WorkflowKeys.ModeVariable, WorkflowKeys.ModeWhile, WorkflowKeys.ModeUntil)),
            new(WorkflowKeys.Operation, "Is", ActionSettingKind.Choice, WorkflowKeys.IsEqual, Options:
            [
                new(WorkflowKeys.IsEqual, "equal to"),
                new(WorkflowKeys.IsNotEqual, "not equal to"),
                new(WorkflowKeys.IsGreater, "greater than"),
                new(WorkflowKeys.IsLess, "less than"),
                new(WorkflowKeys.Contains, "containing"),
                new(WorkflowKeys.IsSet, "set (not empty)"),
                new(WorkflowKeys.IsEmpty, "empty"),
            ])
            {
                ShowWhen = new(WorkflowKeys.Mode, WorkflowKeys.ModeWhile, WorkflowKeys.ModeUntil),
            },
            new(WorkflowKeys.Value, "Value", Placeholder: "text or number")
            {
                ShowWhen = new(WorkflowKeys.Mode, WorkflowKeys.ModeWhile, WorkflowKeys.ModeUntil),
            },
            new(WorkflowKeys.Interval, "Wait between rounds", ActionSettingKind.Number, "0", Description: "0 runs the rounds right after each other. For while / until, the wait comes before the condition is checked again.") { Range = WaitRange },
        ]);

    public static WorkflowNodeType SetVariable { get; } = new(
        WorkflowNodeKinds.SetVariable,
        "Set variable",
        "Stores a value that If nodes (and the Show Variable action) can read.",
        0xFF64748B,
        Contracts.Icons.Variable,
        [
            new(WorkflowKeys.Name, "Variable", Placeholder: "e.g. mode"),
            ScopeSetting(null),
            new(WorkflowKeys.Operation, "Do", ActionSettingKind.Choice, WorkflowKeys.Set, Options:
            [
                new(WorkflowKeys.Set, "Set to"),
                new(WorkflowKeys.Add, "Add"),
                new(WorkflowKeys.Subtract, "Subtract"),
                new(WorkflowKeys.Flip, "Flip true / false"),
                new(WorkflowKeys.Clear, "Clear"),
            ]),
            new(WorkflowKeys.Value, "Value", Placeholder: "text or number (add: 1)")
            {
                ShowWhen = new(WorkflowKeys.Operation, WorkflowKeys.Set, WorkflowKeys.Add, WorkflowKeys.Subtract),
            },
        ]);

    public static WorkflowNodeType Stop { get; } = new(
        WorkflowNodeKinds.Stop,
        "Stop",
        "Ends the run, also its branches that are still waiting.",
        0xFFEF4444,
        Contracts.Icons.Stop,
        []);

    /// <summary>All kinds, in the order the editor's palette lists them.</summary>
    public static IReadOnlyList<WorkflowNodeType> All { get; } = [Trigger, Action, Wait, If, Toggle, Repeat, SetVariable, Stop];

    public static WorkflowNodeType Of(string kind) => All.FirstOrDefault(type => type.Kind == kind) ?? Action;

    /// <summary>A node in one line ("Wait 500 ms", "If mode equal to 2").</summary>
    public static string Summarize(WorkflowNode node)
    {
        var settings = node.EffectiveSettings;
        if (!string.IsNullOrWhiteSpace(node.Title))
        {
            return node.Title!;
        }

        return node.Kind switch
        {
            WorkflowNodeKinds.Trigger => SummarizeTrigger(settings),
            WorkflowNodeKinds.Action => node.Action?.ActionName ?? "Choose an action",
            WorkflowNodeKinds.Wait => $"Wait {WorkflowEngine.FormatDuration(Math.Clamp(settings.GetInt(WorkflowKeys.Milliseconds, 500), 0, WorkflowKeys.MaxWaitMs))}",
            WorkflowNodeKinds.If => settings.Get(WorkflowKeys.Check, WorkflowKeys.CheckVariable) switch
            {
                WorkflowKeys.CheckApp => $"If {Or(settings.Get(WorkflowKeys.App), "…")} is in front",
                WorkflowKeys.CheckTime => $"If between {Or(settings.Get(WorkflowKeys.From), "…")} and {Or(settings.Get(WorkflowKeys.To), "…")}",
                _ => $"If {Or(settings.Get(WorkflowKeys.Name), "…")} {DescribeComparison(settings)}",
            },
            WorkflowNodeKinds.Toggle => "Toggle",
            WorkflowNodeKinds.Repeat => SummarizeRepeat(settings),
            WorkflowNodeKinds.SetVariable => settings.Get(WorkflowKeys.Operation, WorkflowKeys.Set) switch
            {
                WorkflowKeys.Add => $"{Or(settings.Get(WorkflowKeys.Name), "…")} + {Or(settings.Get(WorkflowKeys.Value), "1")}",
                WorkflowKeys.Subtract => $"{Or(settings.Get(WorkflowKeys.Name), "…")} − {Or(settings.Get(WorkflowKeys.Value), "1")}",
                WorkflowKeys.Flip => $"Flip {Or(settings.Get(WorkflowKeys.Name), "…")}",
                WorkflowKeys.Clear => $"Clear {Or(settings.Get(WorkflowKeys.Name), "…")}",
                _ => $"{Or(settings.Get(WorkflowKeys.Name), "…")} = {Or(settings.Get(WorkflowKeys.Value), "\"\"")}",
            },
            WorkflowNodeKinds.Stop => "Stop",
            _ => node.Kind,
        };
    }

    private static string SummarizeTrigger(ActionSettings settings)
    {
        var on = settings.Get(WorkflowKeys.On, WorkflowTriggers.Down);
        return on switch
        {
            WorkflowTriggers.ProfileShown => "When the profile is shown",
            WorkflowTriggers.AppInFront => $"When {Or(settings.Get(WorkflowKeys.App), "an app")} comes to the front",
            WorkflowTriggers.VariableChanged => settings.Get(WorkflowKeys.Value).Length == 0
                ? $"When {Or(settings.Get(WorkflowKeys.Name), "…")} changes"
                : $"When {Or(settings.Get(WorkflowKeys.Name), "…")} becomes {settings.Get(WorkflowKeys.Value).Trim()}",
            WorkflowTriggers.Timer => $"Every {WorkflowEngine.FormatDuration(Math.Clamp(settings.GetInt(WorkflowKeys.Every, 60), WorkflowKeys.MinEverySeconds, WorkflowKeys.MaxEverySeconds) * 1000)}",
            _ => $"When {WorkflowTriggers.Describe(on).ToLowerInvariant()}",
        };
    }

    private static string SummarizeRepeat(ActionSettings settings)
    {
        var every = Math.Clamp(settings.GetInt(WorkflowKeys.Interval, 0), 0, WorkflowKeys.MaxWaitMs) is > 0 and var ms ? $", every {WorkflowEngine.FormatDuration(ms)}" : string.Empty;
        return settings.Get(WorkflowKeys.Mode, WorkflowKeys.ModeCount) switch
        {
            WorkflowKeys.ModeVariable => $"Repeat {Or(settings.Get(WorkflowKeys.Times), "…")}×{every}",
            WorkflowKeys.ModeWhile => $"Repeat while {Or(settings.Get(WorkflowKeys.Name), "…")} {DescribeComparison(settings)}{every}",
            WorkflowKeys.ModeUntil => $"Repeat until {Or(settings.Get(WorkflowKeys.Name), "…")} {DescribeComparison(settings)}{every}",
            _ => $"Repeat {Math.Clamp(settings.GetInt(WorkflowKeys.Count, 2), 1, WorkflowKeys.MaxRepeat)}×{every}",
        };
    }

    private static string DescribeComparison(ActionSettings settings)
    {
        var value = Or(settings.Get(WorkflowKeys.Value), "\"\"");
        return settings.Get(WorkflowKeys.Operation, WorkflowKeys.IsEqual) switch
        {
            WorkflowKeys.IsNotEqual => $"≠ {value}",
            WorkflowKeys.IsGreater => $"> {value}",
            WorkflowKeys.IsLess => $"< {value}",
            WorkflowKeys.Contains => $"contains {value}",
            WorkflowKeys.IsSet => "is set",
            WorkflowKeys.IsEmpty => "is empty",
            _ => $"= {value}",
        };
    }

    private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
