using DeckHost.Contracts.Enums;

namespace DeckHost.Contracts.Models
{
    /// <summary>A bound action plus the settings the user gave it.</summary>
    public sealed record ActionReference(
        string PluginId,
        string PluginName,
        string ActionId,
        string ActionName,
        InputCapabilities RequiredInputs,
        OutputCapabilities RequiredOutputs,
        ActionSettings? Settings = null)
    {
        public ActionSettings EffectiveSettings => Settings ?? ActionSettings.Empty;
    }
}
