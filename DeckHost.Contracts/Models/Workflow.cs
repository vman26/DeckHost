using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models;

/// <summary>
/// A visual workflow on a control (<see cref="ProfileActionKind.Workflow"/>): nodes connected by
/// links. A run starts at a trigger node whose input happened and follows the links from each
/// node's output ports; several links from one port run side by side.
/// </summary>
public sealed record Workflow
{
    [JsonPropertyName("nodes")]
    public IReadOnlyList<WorkflowNode> Nodes { get; init; } = [];

    [JsonPropertyName("links")]
    public IReadOnlyList<WorkflowLink> Links { get; init; } = [];

    /// <summary>What a trigger does while its previous run is still going (<see cref="WorkflowConcurrency"/>).</summary>
    [JsonPropertyName("whileRunning")]
    public string WhileRunning { get; init; } = WorkflowConcurrency.Parallel;

    public WorkflowNode? Find(string nodeId) => Nodes.FirstOrDefault(node => node.Id == nodeId);

    /// <summary>The links leaving a node's output port, in the order they were made.</summary>
    public IEnumerable<WorkflowLink> LinksFrom(string nodeId, string port) =>
        Links.Where(link => link.From == nodeId && link.Port == port);

    /// <summary>Whether a link from <paramref name="from"/> to <paramref name="to"/> would close a loop.</summary>
    public bool WouldCycle(string from, string to)
    {
        if (from == to)
        {
            return true;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([to]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == from)
            {
                return true;
            }

            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var link in Links.Where(link => link.From == current))
            {
                queue.Enqueue(link.To);
            }
        }

        return false;
    }

    // Records compare lists by reference; compare their contents, so an unchanged workflow is equal.
    public bool Equals(Workflow? other) =>
        other is not null &&
        WhileRunning == other.WhileRunning &&
        Nodes.SequenceEqual(other.Nodes) &&
        Links.SequenceEqual(other.Links);

    public override int GetHashCode() => HashCode.Combine(WhileRunning, Nodes.Count, Links.Count);
}

/// <summary>
/// One node of a <see cref="Workflow"/>. <paramref name="Kind"/> is one of <see cref="WorkflowNodeKinds"/>;
/// <paramref name="X"/>/<paramref name="Y"/> place it on the editor's canvas.
/// </summary>
public sealed record WorkflowNode(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("x")] double X = 0,
    [property: JsonPropertyName("y")] double Y = 0)
{
    /// <summary>For an <see cref="WorkflowNodeKinds.Action"/> node: the action it runs, with its settings.</summary>
    [JsonPropertyName("action")]
    public ActionReference? Action { get; init; }

    /// <summary>The node's own parameters (a wait's length, a condition), by <see cref="WorkflowKeys"/>.</summary>
    [JsonPropertyName("settings")]
    public ActionSettings? Settings { get; init; }

    /// <summary>A name the user gave the node (shown instead of the generated one).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonIgnore]
    public ActionSettings EffectiveSettings => Settings ?? ActionSettings.Empty;
}

/// <summary>A connection from output <paramref name="Port"/> of node <paramref name="From"/> to node <paramref name="To"/>.</summary>
public sealed record WorkflowLink(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("port")] string Port,
    [property: JsonPropertyName("to")] string To);

/// <summary>The kinds of workflow nodes.</summary>
public static class WorkflowNodeKinds
{
    /// <summary>Where a run starts: an input of the control (<see cref="WorkflowKeys.On"/>, <see cref="WorkflowTriggers"/>).</summary>
    public const string Trigger = "trigger";

    /// <summary>Runs an action (<see cref="WorkflowNode.Action"/>).</summary>
    public const string Action = "action";

    /// <summary>Waits (<see cref="WorkflowKeys.Milliseconds"/>), then continues.</summary>
    public const string Wait = "wait";

    /// <summary>Continues at Yes or No depending on a condition.</summary>
    public const string If = "if";

    /// <summary>Continues at First and Second in turn: the first run First, the next Second, and so on.</summary>
    public const string Toggle = "toggle";

    /// <summary>Runs its Each output a number of times, one after the other, then continues at Done.</summary>
    public const string Repeat = "repeat";

    /// <summary>Sets, adds to or flips a workflow variable.</summary>
    public const string SetVariable = "set-variable";

    /// <summary>Ends the run (waits still to come don't continue).</summary>
    public const string Stop = "stop";

    public static IReadOnlyList<string> All { get; } = [Trigger, Action, Wait, If, Toggle, Repeat, SetVariable, Stop];

    /// <summary>The output ports of a kind, in the order the editor shows them.</summary>
    public static IReadOnlyList<string> OutputsOf(string kind) => kind switch
    {
        If => [WorkflowPorts.Yes, WorkflowPorts.No],
        Toggle => [WorkflowPorts.First, WorkflowPorts.Second],
        Repeat => [WorkflowPorts.Each, WorkflowPorts.Done],
        Stop => [],
        _ => [WorkflowPorts.Next],
    };

    /// <summary>Whether links can lead into the kind (a trigger only starts runs).</summary>
    public static bool HasInput(string kind) => kind != Trigger;
}

public static class WorkflowPorts
{
    public const string Next = "next";
    public const string Yes = "yes";
    public const string No = "no";
    public const string First = "first";
    public const string Second = "second";
    public const string Each = "each";
    public const string Done = "done";

    public static string Describe(string port) => port switch
    {
        Next => "Then",
        Yes => "Yes",
        No => "No",
        First => "1st",
        Second => "2nd",
        Each => "Each time",
        Done => "Done",
        _ => port,
    };
}

/// <summary>The inputs a trigger node can start on.</summary>
public static class WorkflowTriggers
{
    /// <summary>The key goes down (right away).</summary>
    public const string Down = "down";

    /// <summary>The key comes up.</summary>
    public const string Up = "up";

    /// <summary>A short press: on release, not after a hold, and not when a second press follows (with a Double trigger).</summary>
    public const string Tap = "tap";

    public const string DoublePress = "double";

    /// <summary>Held down (the device's long press).</summary>
    public const string Hold = "hold";

    public const string RotateLeft = ControlInputs.RotateLeft;
    public const string RotateRight = ControlInputs.RotateRight;
    public const string SwipeLeft = ControlInputs.SwipeLeft;
    public const string SwipeRight = ControlInputs.SwipeRight;

    /// <summary>The triggers a raw input fires at once (tap, double press and hold need timing: the host decides those).</summary>
    public static string? Immediate(Enums.InputCapabilities eventType, int value) => eventType switch
    {
        Enums.InputCapabilities.Press => Down,
        Enums.InputCapabilities.Release => Up,
        Enums.InputCapabilities.Rotate or Enums.InputCapabilities.Swipe => ControlInputs.FromEvent(eventType, value),
        _ => null,
    };

    public static string Describe(string trigger) => trigger switch
    {
        Down => "Pressed",
        Up => "Released",
        Tap => "Tapped",
        DoublePress => "Double pressed",
        Hold => "Held down",
        RotateLeft => "Turned left",
        RotateRight => "Turned right",
        SwipeLeft => "Swiped left",
        SwipeRight => "Swiped right",
        _ => trigger,
    };
}

/// <summary>What a trigger does while its previous run is still going (waits).</summary>
public static class WorkflowConcurrency
{
    /// <summary>Start another run next to it.</summary>
    public const string Parallel = "parallel";

    /// <summary>Ignore the input until the run is finished.</summary>
    public const string Ignore = "ignore";

    /// <summary>Stop the run and start again.</summary>
    public const string Restart = "restart";

    /// <summary>Stop the run (the input works as a stop button).</summary>
    public const string Stop = "stop";
}

/// <summary>Setting keys of workflow nodes (<see cref="WorkflowNode.Settings"/>) and their values.</summary>
public static class WorkflowKeys
{
    /// <summary>Trigger: which input (<see cref="WorkflowTriggers"/>).</summary>
    public const string On = "on";

    /// <summary>Wait: how long, in milliseconds.</summary>
    public const string Milliseconds = "ms";

    /// <summary>Repeat: how many times (<see cref="ModeCount"/>).</summary>
    public const string Count = "count";

    /// <summary>Repeat: how the rounds are decided (<see cref="ModeCount"/>, <see cref="ModeVariable"/>, <see cref="ModeWhile"/>, <see cref="ModeUntil"/>).</summary>
    public const string Mode = "mode";

    /// <summary>A fixed number of rounds (<see cref="Count"/>); also what a Repeat node without a mode does.</summary>
    public const string ModeCount = "count";

    /// <summary>As many rounds as a variable's value (<see cref="Times"/>).</summary>
    public const string ModeVariable = "variable";

    /// <summary>Rounds as long as a variable condition holds (<see cref="Name"/>, <see cref="Operation"/>, <see cref="Value"/>), checked before each round.</summary>
    public const string ModeWhile = "while";

    /// <summary>Rounds until a variable condition holds, checked before each round.</summary>
    public const string ModeUntil = "until";

    /// <summary>Repeat (<see cref="ModeVariable"/>): the variable that holds the number of rounds.</summary>
    public const string Times = "times";

    /// <summary>Repeat: how long to wait between rounds, in milliseconds (0: none).</summary>
    public const string Interval = "interval";

    /// <summary>The most rounds a while / until Repeat runs (a condition that never changes can't run forever).</summary>
    public const int MaxConditionRounds = 10_000;

    /// <summary>If: what to check (<see cref="CheckVariable"/>, <see cref="CheckApp"/>, <see cref="CheckTime"/>).</summary>
    public const string Check = "check";

    /// <summary>If / Set variable: the variable's name.</summary>
    public const string Name = "name";

    /// <summary>If: the comparison; Set variable: the operation.</summary>
    public const string Operation = "op";

    public const string Value = "value";

    /// <summary>If (app): part of the focused app's name.</summary>
    public const string App = "app";

    /// <summary>If (time): from and to, "HH:mm" (may wrap past midnight).</summary>
    public const string From = "from";
    public const string To = "to";

    public const string CheckVariable = "variable";
    public const string CheckApp = "app";
    public const string CheckTime = "time";

    public const string IsEqual = "equals";
    public const string IsNotEqual = "not-equals";
    public const string IsGreater = "greater";
    public const string IsLess = "less";
    public const string Contains = "contains";
    public const string IsSet = "set";
    public const string IsEmpty = "empty";

    public const string Set = "set";
    public const string Add = "add";
    public const string Subtract = "subtract";
    public const string Flip = "flip";
    public const string Clear = "clear";

    /// <summary>The longest wait a node can have (ten minutes).</summary>
    public const int MaxWaitMs = 600_000;

    /// <summary>The most times a Repeat node runs.</summary>
    public const int MaxRepeat = 1000;
}
