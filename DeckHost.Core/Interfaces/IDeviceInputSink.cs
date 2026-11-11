using DeckHost.Core.Models;

namespace DeckHost.Core.Interfaces;

public interface IDeviceInputSink
{
    bool DispatchInput(DeviceInputEvent inputEvent);
}
