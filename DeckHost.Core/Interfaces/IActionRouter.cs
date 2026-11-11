using DeckHost.Contracts.Models;
using DeckHost.Core.Models;

namespace DeckHost.Core.Interfaces
{
    public interface IActionRouter
    {
        bool Dispatch(DeviceProfile profile, DeviceInputEvent inputEvent);
    }
}