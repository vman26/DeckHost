using DeckHost.Contracts.Enums;

namespace DeckHost.Contracts.Models
{
    public sealed record ActionReference(
        string PluginId,
        string PluginName,
        string ActionId,
        string ActionName,
        InputCapabilities RequiredInputs,
        OutputCapabilities RequiredOutputs);
}
