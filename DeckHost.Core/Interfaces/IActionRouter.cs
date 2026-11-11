using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;

namespace DeckHost.Core.Interfaces
{
    /// <summary>Not thread-safe: the host only calls it from its worker.</summary>
    public interface IActionRouter
    {
        /// <summary>Action keys (<c>pluginId:actionId</c>) that more than one plugin registered; only the first is used.</summary>
        IReadOnlyList<string> DuplicateActionKeys { get; }

        /// <summary>Replaces the set of enabled application plugins.</summary>
        void SetApplicationPlugins(IEnumerable<IApplicationPlugin> applicationPlugins);

        IDeckAction? FindAction(ActionReference reference);

        /// <summary>Runs the action(s) bound to the input's control. Returns true when at least one ran.</summary>
        bool Dispatch(DeviceProfile profile, DeviceInputEvent inputEvent, IActionHost host);
    }
}
