using System.Runtime.InteropServices;

namespace DeckHost.Device.MiraBox;

public sealed class MiraBoxController : IDisposable
{
    private readonly MiraBoxNative.MiraBoxEventCallback _callbackDelegate;
    private bool _started;
    private bool _disposed;

    public MiraBoxController()
    {
        _callbackDelegate = OnMiraBoxEvent;
    }

    public event EventHandler<MiraBoxInputEventArgs>? InputReceived;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_started)
        {
            return;
        }

        var result = MiraBoxNative.Initialize();
        if (result != 0)
        {
            throw new ExternalException($"Failed to initialize MiraBox bridge. Native error code: {result}.", result);
        }

        MiraBoxNative.SetCallback(_callbackDelegate);
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        MiraBoxNative.Shutdown();
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnMiraBoxEvent(int eventType, int keyId, int x, int y)
    {
        var managedEventType = Enum.IsDefined(typeof(MiraBoxEventType), eventType)
            ? (MiraBoxEventType)eventType
            : MiraBoxEventType.Unknown;

        InputReceived?.Invoke(this, new MiraBoxInputEventArgs(managedEventType, keyId, x, y));
    }
}
