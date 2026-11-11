using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;

namespace DeckHost.Core.Devices;

public sealed class DevicePluginContext : IDevicePluginContext
{
    private readonly IDeviceInputSink _inputSink;

    public DevicePluginContext(IDeviceInputSink inputSink)
    {
        ArgumentNullException.ThrowIfNull(inputSink);
        _inputSink = inputSink;
    }

    public void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value)
    {
        _inputSink.DispatchInput(new DeviceInputEvent(deviceId, controlId, eventType, value));
    }
}
