using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces
{
    public interface IDeckAction
    {
        string Id { get; }
        string Name { get; }
        InputCapabilities RequiredInputs { get; }
        OutputCapabilities RequiredOutputs { get; }

        void Execute(ActionContext context);
        void Render(IRenderContext context);
    }
}
