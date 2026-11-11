using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;

namespace DeckHost.Core.Devices;

public sealed class DevicePluginContext : IDevicePluginContext
{
    private readonly IDeviceInputSink _inputSink;
    private readonly Action<string, string> _log;
    private readonly Action<DeviceConnectionStatus> _reportStatus;

    public DevicePluginContext(IDeviceInputSink inputSink, Action<string, string> log, Action<DeviceConnectionStatus> reportStatus)
    {
        ArgumentNullException.ThrowIfNull(inputSink);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(reportStatus);
        _inputSink = inputSink;
        _log = log;
        _reportStatus = reportStatus;
    }

    public void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value) =>
        _inputSink.ReportInput(new DeviceInputEvent(deviceId, controlId, eventType, value));

    public void ReportConnectionStatus(string deviceId, string state, string message) =>
        _reportStatus(new DeviceConnectionStatus(deviceId, state, message));

    public void Log(string level, string message) => _log(level, message);
}
