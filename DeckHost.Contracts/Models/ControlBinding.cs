using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models
{
    public sealed record ControlBinding(
        string ControlId,
        ProfileActionKind ActionKind,
        ActionReference Action,
        ControlAppearance Appearance,
        IReadOnlyList<MultiActionStep> Steps)
    {
        /// <summary>
        /// For <see cref="ProfileActionKind.Workflow"/>: the nodes and links. An init property, so
        /// the record's constructor stays binary-compatible.
        /// </summary>
        [JsonPropertyName("workflow")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Workflow? Workflow { get; init; }

        /// <summary>
        /// Every action the binding runs besides its own: its steps, or its workflow's action nodes
        /// (in node order).
        /// </summary>
        [JsonIgnore]
        public IEnumerable<ActionReference> StepActions =>
            ActionKind == ProfileActionKind.Workflow
                ? (Workflow?.Nodes ?? []).Select(static node => node.Action).OfType<ActionReference>()
                : Steps.OrderBy(static step => step.Order).Select(static step => step.Action);
    }
}
