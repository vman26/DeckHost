using DeckHost.Contracts.Enums;

namespace DeckHost.Contracts.Interfaces
{
    public interface IDevicePluginContext
    {
        void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value);
    }
}
