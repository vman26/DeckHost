using DeckHost.Core.Models;

namespace DeckHost.Core.Interfaces;

public interface IDeviceInputSink
{
    /// <summary>Queues an input event. Thread-safe and non-blocking.</summary>
    void ReportInput(DeviceInputEvent inputEvent);
}
