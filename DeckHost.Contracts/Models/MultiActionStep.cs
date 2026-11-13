using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models
{
    public sealed record MultiActionStep(
        int Order,
        ActionReference Action)
    {
        /// <summary>How long to wait before this step runs, in milliseconds (0: right after the previous one).</summary>
        [JsonPropertyName("delayMs")]
        public int DelayMs { get; init; }

        /// <summary>
        /// Which set the step belongs to: for a Multi Action Switch <see cref="StepGroups.First"/> or
        /// <see cref="StepGroups.Second"/>, for Key Logic <see cref="StepGroups.Press"/>,
        /// <see cref="StepGroups.DoublePress"/> or <see cref="StepGroups.Hold"/>. Null for a Multi Action.
        /// </summary>
        [JsonPropertyName("group")]
        public string? Group { get; init; }
    }

    /// <summary>The step sets of a Multi Action Switch and of Key Logic.</summary>
    public static class StepGroups
    {
        public const string First = "first";
        public const string Second = "second";
        public const string Press = "press";
        public const string DoublePress = "double";
        public const string Hold = "hold";

        /// <summary>The longest wait a step can have.</summary>
        public const int MaxDelayMs = 60_000;

        /// <summary>The sets of a binding kind, in order (empty for a Multi Action).</summary>
        public static IReadOnlyList<string> For(ProfileActionKind kind) => kind switch
        {
            ProfileActionKind.MultiActionSwitch => [First, Second],
            ProfileActionKind.KeyLogic => [Press, DoublePress, Hold],
            _ => [],
        };

        /// <summary>A step's set, or the kind's first set when it has none (a converted Multi Action).</summary>
        public static string? Of(MultiActionStep step, ProfileActionKind kind) =>
            For(kind) is { Count: > 0 } groups ? (step.Group is { } group && groups.Contains(group) ? group : groups[0]) : null;
    }
}
