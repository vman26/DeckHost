using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.State;

namespace DeckHost.Core.Plugins.Isolation;

/// <summary>Starts the process a plugin runs in (tests replace it with one that runs in this process).</summary>
public interface IPluginProcessLauncher
{
    /// <summary>Starts it; it connects to 127.0.0.1:<paramref name="port"/> and proves itself with <paramref name="token"/>.</summary>
    IPluginProcess Start(int port, string token, string pluginDirectory, string pluginId, Action<string, string> log);
}

/// <summary>A started plugin process.</summary>
public interface IPluginProcess : IDisposable
{
    int? ProcessId { get; }

    /// <summary>Ends it now (it no longer answers).</summary>
    void Kill();
}

/// <summary>Runs plugins as <c>DeckHost --plugin-host …</c>: the same program, without a window.</summary>
public sealed class SelfPluginLauncher(string executable) : IPluginProcessLauncher
{
    public IPluginProcess Start(int port, string token, string pluginDirectory, string pluginId, Action<string, string> log)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--plugin-host", port.ToString(System.Globalization.CultureInfo.InvariantCulture), token, pluginDirectory })
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start) ?? throw new PluginException("The plugin process didn't start.");
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log("Information", $"{pluginId}: {e.Data}"); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log("Warning", $"{pluginId}: {e.Data}"); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new Handle(process);
    }

    private sealed class Handle(Process process) : IPluginProcess
    {
        public int? ProcessId => process.HasExited ? null : process.Id;

        public void Kill()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        public void Dispose()
        {
            // Give it a moment to end by itself after the connection closed.
            if (!process.WaitForExit(1500))
            {
                Kill();
            }

            process.Dispose();
        }
    }
}

/// <summary>
/// DeckHost's side of a plugin in its own process (docs ADR 0007): starts the process, gives DeckHost
/// stand-ins for the plugin and its actions, and restarts the process when it crashes or stops
/// answering (a few times, then the plugin stays stopped with a message on the Plugins page).
/// Execute and the like are sent without waiting. Render can't wait for another process on DeckHost's
/// worker, so it draws the action's last picture and asks for a new one when what it would draw from
/// changed; when the picture arrives, the device is redrawn.
/// </summary>
public sealed class PluginProcessHost : IDisposable
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan[] RestartDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(10);

    private readonly PluginManifest _manifest;
    private readonly string _directory;
    private readonly IPluginProcessLauncher _launcher;
    private readonly Action<string, string> _log;
    private readonly Lock _sync = new();
    private readonly Lock _writeLock = new();
    private readonly ConcurrentDictionary<int, RenderRequest> _renders = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string?>> _answers = new();
    private readonly Dictionary<(string DeviceId, string ControlId, string ProfileId, string ActionId), Picture> _pictures = [];
    private readonly Dictionary<(string ActionId, string DeviceId, string ControlId, string ProfileId), WireContext> _appeared = [];
    private Connection? _connection;
    private IsolatedApplicationPlugin? _plugin;
    private IActionHost? _actionHost;
    private IVariableStore? _hostVariables;
    private int _nextId;
    private int _restarts;
    private bool _disposed;
    private Timer? _pingTimer;
    private long _lastPong;

    public PluginProcessHost(PluginManifest manifest, string directory, IPluginProcessLauncher launcher, Action<string, string>? log = null)
    {
        _manifest = manifest;
        _directory = directory;
        _launcher = launcher;
        _log = log ?? (static (_, _) => { });
    }

    /// <summary>Raised (on a background thread) when the process stopped, restarted or gave up.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>"Own process (pid 1234)", "Restarting after a crash…", "Stopped: …".</summary>
    public string Status { get; private set; } = "Starting…";

    /// <summary>Why the plugin stopped for good; null while it runs.</summary>
    public string? Problem { get; private set; }

    public int Restarts => _restarts;

    public int? ProcessId => _connection?.Process.ProcessId;

    /// <summary>Starts the process and waits until it has loaded the plugin; throws <see cref="PluginException"/> when it can't.</summary>
    public IApplicationPlugin Start()
    {
        var descriptor = Connect();
        _plugin = new IsolatedApplicationPlugin(this, descriptor);
        _pingTimer = new Timer(_ => Ping(), null, PingInterval, PingInterval);
        return _plugin;
    }

    private PluginDescriptor Connect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var process = _launcher.Start(port, token, _directory, _manifest.Id, _log);
        TcpClient? client = null;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            if (!accept.Wait(StartTimeout))
            {
                throw new PluginException($"{_manifest.Name} didn't start within {StartTimeout.TotalSeconds:0} seconds.");
            }

            client = accept.Result;
            client.NoDelay = true;
            var stream = client.GetStream();
            var reader = new StreamReader(stream);
            var hello = ReadWithin(reader, StartTimeout);
            if (hello is not { Type: Message.Hello } || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(hello.Token ?? string.Empty), System.Text.Encoding.UTF8.GetBytes(token)))
            {
                throw new PluginException("The plugin process didn't identify itself.");
            }

            var ready = ReadWithin(reader, StartTimeout);
            if (ready is { Type: Message.Failed })
            {
                throw new PluginException(ready.Text ?? "The plugin failed to load.");
            }

            if (ready is not { Type: Message.Ready, Plugin: { } descriptor })
            {
                throw new PluginException($"{_manifest.Name} didn't load within {StartTimeout.TotalSeconds:0} seconds.");
            }

            var connection = new Connection(process, client, reader, Wire.Writer(stream));
            lock (_sync)
            {
                _connection = connection;
            }

            Interlocked.Exchange(ref _lastPong, Environment.TickCount64);
            Status = process.ProcessId is { } pid ? $"Runs in its own process (pid {pid})" : "Runs in its own process";
            _ = Task.Run(() => ReadLoopAsync(connection));
            return descriptor;
        }
        catch (Exception ex) when (ex is not PluginException)
        {
            client?.Dispose();
            process.Dispose();
            throw new PluginException($"Could not start {_manifest.Name} in its own process: {ex.GetBaseException().Message}", ex);
        }
        catch
        {
            client?.Dispose();
            process.Kill();
            process.Dispose();
            throw;
        }
    }

    private static Message? ReadWithin(StreamReader reader, TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try
        {
            return Wire.ReadAsync(reader, cancel.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task ReadLoopAsync(Connection connection)
    {
        try
        {
            while (await Wire.ReadAsync(connection.Reader, CancellationToken.None).ConfigureAwait(false) is { } message)
            {
                Handle(message);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException or SocketException)
        {
        }

        OnDisconnected(connection);
    }

    private void Handle(Message message)
    {
        switch (message.Type)
        {
            case Message.Drawn when message.Id is { } id && _renders.TryRemove(id, out var request):
                IActionHost? host;
                lock (_sync)
                {
                    if (_pictures.TryGetValue(request.Key, out var picture) && picture.Pending == id)
                    {
                        _pictures[request.Key] = picture with { Commands = message.Commands ?? [], Pending = null };
                    }

                    host = _actionHost;
                }

                host?.RequestRender(request.Key.DeviceId, request.Key.ControlId);
                break;
            case Message.Answer when message.Id is { } id && _answers.TryRemove(id, out var answer):
                answer.TrySetResult(message.Text);
                break;
            case Message.Publish when message.Key is { } key && message.Value is { } value:
                _hostVariables?.Publish(key, value.ToObject());
                break;
            case Message.HostCall when message.Call is { } call:
                CallHost(call, message.Arguments ?? []);
                break;
            case Message.Log:
                _log(message.Level ?? "Information", $"{_manifest.Name}: {message.Text}");
                break;
            case Message.Pong:
                Interlocked.Exchange(ref _lastPong, Environment.TickCount64);
                break;
        }
    }

    private void CallHost(string call, IReadOnlyList<string> arguments)
    {
        var host = _actionHost;
        if (host is null || arguments.Count == 0)
        {
            return;
        }

        var deviceId = arguments[0];
        string Argument(int index) => arguments.Count > index ? arguments[index] : string.Empty;
        int Number(int index) => int.TryParse(Argument(index), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        switch (call)
        {
            case "requestRender":
                // Something changed in the plugin: every picture of this device is asked for again.
                lock (_sync)
                {
                    foreach (var key in _pictures.Keys.Where(key => key.DeviceId == deviceId).ToList())
                    {
                        _pictures[key] = _pictures[key] with { Stale = true };
                    }
                }

                host.RequestRender(deviceId);
                break;
            case "activateProfile":
                host.ActivateProfile(deviceId, Argument(1));
                break;
            case "activateRelativeProfile":
                host.ActivateRelativeProfile(deviceId, Number(1));
                break;
            case "activateRelativePage":
                host.ActivateRelativePage(deviceId, Number(1));
                break;
            case "activatePage":
                host.ActivatePage(deviceId, Number(1));
                break;
            case "openFolder":
                host.OpenFolder(deviceId, Argument(1));
                break;
            case "closeFolder":
                host.CloseFolder(deviceId);
                break;
        }
    }

    // ---------------------------------------------------------------- calls from the stand-ins

    internal void Send(Message message)
    {
        Connection? connection;
        lock (_sync)
        {
            connection = _connection;
        }

        if (connection is null)
        {
            return;
        }

        var line = Wire.Encode(message);
        lock (_writeLock)
        {
            try
            {
                connection.Writer.Write(line);
                connection.Writer.Write('\n');
                connection.Writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                // The read loop notices and restarts the process.
            }
        }
    }

    internal void Remember(ActionContext context)
    {
        _actionHost = context.Host;
        _hostVariables ??= context.Variables;
    }

    internal void Call(string type, string actionId, ActionContext context)
    {
        Remember(context);
        var wire = Describe(context);
        lock (_sync)
        {
            var key = (actionId, context.DeviceId, context.ControlId, context.ProfileId);
            if (type == Message.Appear)
            {
                _appeared[key] = wire;
            }
            else if (type == Message.Disappear)
            {
                _appeared.Remove(key);
            }
        }

        Send(new Message(type) { ActionId = actionId, Context = wire, Variables = Snapshot(context.Variables, context.DeviceId, context.ControlId) });
    }

    internal Uri? SettingsPage(string actionId, ActionContext context)
    {
        Remember(context);
        var id = Interlocked.Increment(ref _nextId);
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answers[id] = answer;
        Send(new Message(Message.SettingsPage) { Id = id, ActionId = actionId, Context = Describe(context) });
        var text = answer.Task.Wait(TimeSpan.FromSeconds(2)) ? answer.Task.Result : null;
        _answers.TryRemove(id, out _);
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>Draws the action's last picture, and asks for a new one when what it draws from changed.</summary>
    internal void Render(ActionDescriptor action, IRenderContext context)
    {
        _hostVariables ??= context.Variables;
        var variables = Snapshot(context.Variables, context.DeviceId, context.ControlId);
        var wire = new WireContext(context.DeviceId, context.ControlId, context.ProfileId, context.Settings)
        {
            Width = context.Width,
            Height = context.Height,
            IsCompact = context.IsCompact,
            TextStyle = context.TextStyle,
        };
        var fingerprint = JsonSerializer.Serialize(wire, Wire.Options) + JsonSerializer.Serialize(variables, Wire.Options);
        var key = (context.DeviceId, context.ControlId, context.ProfileId, action.Id);
        var now = Environment.TickCount64;
        IReadOnlyList<DrawCommand> commands;
        int? request = null;
        lock (_sync)
        {
            _pictures.TryGetValue(key, out var picture);
            picture ??= new Picture(null, [], null, 0, Stale: true);
            var refresh = action.RefreshIntervalMs is { } interval && now - picture.AskedAt >= interval * 0.9;
            var pendingTooLong = picture.Pending is not null && now - picture.AskedAt > 2000;
            if ((picture.Pending is null || pendingTooLong) && (picture.Stale || refresh || picture.Fingerprint != fingerprint) && _connection is not null)
            {
                request = Interlocked.Increment(ref _nextId);
                picture = picture with { Fingerprint = fingerprint, Pending = request, AskedAt = now, Stale = false };
            }

            _pictures[key] = picture;
            commands = picture.Commands;
        }

        if (request is { } id)
        {
            _renders[id] = new RenderRequest(key);
            Send(new Message(Message.Render) { Id = id, ActionId = action.Id, Context = wire, Variables = variables });
        }

        DisplayList.Replay(context, commands);
    }

    private static WireContext Describe(ActionContext context) =>
        new(context.DeviceId, context.ControlId, context.ProfileId, context.Settings) { EventType = context.EventType, Value = context.Value };

    /// <summary>The control's variables (its text, image, errors, …) for the plugin's copy of the store.</summary>
    private static IReadOnlyList<WireVariable> Snapshot(IVariableStore store, string deviceId, string controlId) =>
        store is VariableStore real
            ? [.. real.Snapshot(VariableKeys.Control(deviceId, controlId))
                .Select(static pair => WireValue.From(pair.Value) is { } value ? new WireVariable(pair.Key, value) : null)
                .OfType<WireVariable>()
                .OrderBy(static variable => variable.Key, StringComparer.Ordinal)]
            : [];

    // ---------------------------------------------------------------- crashes and restarts

    private void Ping()
    {
        if (_disposed)
        {
            return;
        }

        Connection? connection;
        lock (_sync)
        {
            connection = _connection;
        }

        if (connection is null)
        {
            return;
        }

        if (Environment.TickCount64 - Interlocked.Read(ref _lastPong) > PingTimeout.TotalMilliseconds)
        {
            _log("Warning", $"{_manifest.Name} stopped answering; restarting it.");
            connection.Process.Kill();
            connection.Client.Dispose();
            return;
        }

        Send(new Message(Message.Ping) { Id = Interlocked.Increment(ref _nextId) });
    }

    private void OnDisconnected(Connection connection)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_connection, connection))
            {
                return;
            }

            _connection = null;
            foreach (var key in _pictures.Keys.ToList())
            {
                _pictures[key] = _pictures[key] with { Pending = null, Stale = true };
            }
        }

        connection.Dispose();
        _renders.Clear();
        foreach (var answer in _answers.Values)
        {
            answer.TrySetResult(null);
        }

        ScheduleRestart();
    }

    private void ScheduleRestart()
    {
        if (_disposed)
        {
            return;
        }

        if (_restarts >= RestartDelays.Length)
        {
            Problem = $"{_manifest.Name} stopped {_restarts + 1} times, so DeckHost stopped restarting it. Disable and enable it to try again; the log has the details.";
            Status = "Stopped";
            _log("Error", Problem);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var delay = RestartDelays[_restarts++];
        Status = $"Stopped unexpectedly; restarting ({_restarts} of {RestartDelays.Length})…";
        _log("Warning", $"{_manifest.Name}'s process ended unexpectedly; restarting in {delay.TotalSeconds:0} s.");
        StatusChanged?.Invoke(this, EventArgs.Empty);
        _ = Task.Delay(delay).ContinueWith(_ => Restart(), TaskScheduler.Default);
    }

    private void Restart()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Connect();
            Status += $", restarted {_restarts}×";
            List<(string ActionId, WireContext Context)> appeared;
            lock (_sync)
            {
                appeared = [.. _appeared.Select(static pair => (pair.Key.ActionId, pair.Value))];
            }

            // The new process starts from nothing: tell it what's on the devices, and redraw them.
            foreach (var (actionId, context) in appeared)
            {
                Send(new Message(Message.Appear) { ActionId = actionId, Context = context });
            }

            foreach (var deviceId in appeared.Select(static item => item.Context.DeviceId).Distinct())
            {
                _actionHost?.RequestRender(deviceId);
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (PluginException ex)
        {
            _log("Error", ex.Message);
            ScheduleRestart();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pingTimer?.Dispose();
        Send(new Message(Message.Shutdown));
        Connection? connection;
        lock (_sync)
        {
            connection = _connection;
            _connection = null;
        }

        connection?.Dispose();
    }

    private sealed record RenderRequest((string DeviceId, string ControlId, string ProfileId, string ActionId) Key);

    /// <summary>An action's last picture on a control and what it was drawn from.</summary>
    private sealed record Picture(string? Fingerprint, IReadOnlyList<DrawCommand> Commands, int? Pending, long AskedAt, bool Stale);

    private sealed class Connection(IPluginProcess process, TcpClient client, StreamReader reader, StreamWriter writer) : IDisposable
    {
        public IPluginProcess Process => process;

        public TcpClient Client => client;

        public StreamReader Reader => reader;

        public StreamWriter Writer => writer;

        public void Dispose()
        {
            client.Dispose();
            process.Dispose();
        }
    }
}

/// <summary>The plugin as DeckHost sees it: its actions are <see cref="IsolatedDeckAction"/> stand-ins.</summary>
public sealed class IsolatedApplicationPlugin : IApplicationPlugin
{
    internal IsolatedApplicationPlugin(PluginProcessHost host, PluginDescriptor descriptor)
    {
        Descriptor = descriptor;
        Actions = [.. descriptor.Actions.Select(action => new IsolatedDeckAction(host, action))];
    }

    public PluginDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Name => Descriptor.Name;

    public string Version => Descriptor.Version;

    public string? Author => Descriptor.Author;

    public string? Description => Descriptor.Description;

    public IReadOnlyCollection<IDeckAction> Actions { get; }
}

/// <summary>An action of a plugin in its own process: calls go there, pictures come back.</summary>
public sealed class IsolatedDeckAction(PluginProcessHost host, ActionDescriptor descriptor) : IDeckAction
{
    public string Id => descriptor.Id;

    public string Name => descriptor.Name;

    public InputCapabilities RequiredInputs => descriptor.RequiredInputs;

    public OutputCapabilities RequiredOutputs => descriptor.RequiredOutputs;

    public string? Description => descriptor.Description;

    public IReadOnlyList<ActionSettingDefinition> Settings => descriptor.Settings;

    public bool ShowInPalette => descriptor.ShowInPalette;

    public string? ListIcon => descriptor.ListIcon;

    public bool CanBeMultiActionStep => descriptor.CanBeMultiActionStep;

    public TimeSpan? RefreshInterval => descriptor.RefreshIntervalMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null;

    public bool ShowsAppearanceOnly => descriptor.ShowsAppearanceOnly;

    public bool HasSettingsPage => descriptor.HasSettingsPage;

    public Uri? GetSettingsPageUri(ActionContext context) => host.SettingsPage(Id, context);

    public void OnAppear(ActionContext context) => host.Call(Message.Appear, Id, context);

    public void OnDisappear(ActionContext context) => host.Call(Message.Disappear, Id, context);

    public void Execute(ActionContext context) => host.Call(Message.Execute, Id, context);

    public void Render(IRenderContext context) => host.Render(descriptor, context);
}
