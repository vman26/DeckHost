using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;

namespace DeckHost.Contracts.Models
{
    /// <param name="EventType">The input that triggered the action.</param>
    /// <param name="Value">Event value: 1 press, 0 release, -1/+1 per rotation or swipe step.</param>
    public record ActionContext(
        string DeviceId,
        string ControlId,
        IVariableStore Variables,
        InputCapabilities EventType = InputCapabilities.None,
        int Value = 0)
    {
        /// <summary>The user's settings for this binding (or Multi Action step).</summary>
        public ActionSettings Settings { get; init; } = ActionSettings.Empty;

        public IActionHost Host { get; init; } = NullActionHost.Instance;
    }
}
