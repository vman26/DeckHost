using DeckHost.Contracts.Interfaces;

namespace DeckHost.Contracts.Models
{
    public record ActionContext(
        string DeviceId, 
        string ControlId, 
        IVariableStore Variables);
}
