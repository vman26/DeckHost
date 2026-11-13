using System.Net.Sockets;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.State;

namespace DeckHost.Core.Plugins.Isolation;

/// <summary>
/// The plugin's side of running in its own process (<c>DeckHost --plugin-host &lt;port&gt; &lt;token&gt; &lt;folder&gt;</c>):
/// loads the plugin package, connects back to DeckHost, describes the plugin's actions and then
/// runs what DeckHost asks, one message at a time. <c>Render</c> is recorded and sent back as drawing
/// commands; variables the plugin publishes and calls to <see cref="IActionHost"/> go to DeckHost.
/// The process ends when DeckHost closes the connection.
/// </summary>
public sealed class PluginProcessServer : IDisposable
{
    private readonly Lock _writeLock = new();
    private readonly VariableStore _variables = new();
    private StreamWriter? _writer;
    private IApplicationPlugin? _plugin;
    private PluginLoadContext? _context;
    private Dictionary<string, IDeckAction> _actions = new(StringComparer.Ordinal);

    /// <summary>Runs the plugin until DeckHost disconnects; returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[0], out var port))
        {
            Console.Error.WriteLine("Usage: --plugin-host <port> <token> <plugin folder>");
            return 2;
        }

        using var server = new PluginProcessServer();
        return server.RunAsync(port, args[1], args[2], CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<int> RunAsync(int port, string token, string directory, CancellationToken cancellationToken)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        _writer = Wire.Writer(stream);
        Send(new Message(Message.Hello) { Token = token });

        try
        {
            var manifest = PluginManifest.Parse(await File.ReadAllTextAsync(Path.Combine(directory, PluginManifest.FileName), cancellationToken).ConfigureAwait(false));
            (_plugin, _context) = PluginCatalog.LoadInProcess(directory, manifest);
            _actions = _plugin.Actions.GroupBy(static action => action.Id, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
            Send(new Message(Message.Ready) { Plugin = Describe(_plugin) });
        }
        catch (Exception ex) when (ex is PluginException or IOException or UnauthorizedAccessException)
        {
            Send(new Message(Message.Failed) { Text = ex.Message });
            return 1;
        }

        try
        {
            while (await Wire.ReadAsync(reader, cancellationToken).ConfigureAwait(false) is { } message)
            {
                if (message.Type == Message.Shutdown)
                {
                    break;
                }

                Handle(message);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
        {
            // DeckHost went away.
        }

        return 0;
    }

    internal static PluginDescriptor Describe(IApplicationPlugin plugin) => new(
        plugin.Id,
        plugin.Name,
        plugin.Version,
        plugin.Author,
        plugin.Description,
        [.. plugin.Actions.Select(static action => new ActionDescriptor(
            action.Id,
            action.Name,
            action.RequiredInputs,
            action.RequiredOutputs,
            action.Description,
            action.Settings,
            action.ShowInPalette,
            action.ListIcon,
            action.CanBeMultiActionStep,
            action.RefreshInterval?.TotalMilliseconds,
            action.ShowsAppearanceOnly,
            action.HasSettingsPage))]);

    private void Handle(Message message)
    {
        if (message.Type == Message.Ping)
        {
            Send(new Message(Message.Pong) { Id = message.Id });
            return;
        }

        if (message.ActionId is not { } actionId || message.Context is not { } wire || !_actions.TryGetValue(actionId, out var action))
        {
            return;
        }

        foreach (var variable in message.Variables ?? [])
        {
            _variables.Publish(variable.Key, variable.Value.ToObject());
        }

        try
        {
            switch (message.Type)
            {
                case Message.Execute:
                    action.Execute(ContextFor(wire));
                    break;
                case Message.Appear:
                    action.OnAppear(ContextFor(wire));
                    break;
                case Message.Disappear:
                    action.OnDisappear(ContextFor(wire));
                    break;
                case Message.Render:
                    var recorder = new RecordingRenderContext(wire, new ForwardingStore(this));
                    action.Render(recorder);
                    Send(new Message(Message.Drawn) { Id = message.Id, Commands = recorder.Commands });
                    break;
                case Message.SettingsPage:
                    Send(new Message(Message.Answer) { Id = message.Id, Text = action.GetSettingsPageUri(ContextFor(wire))?.ToString() });
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An action that throws is reported, like in DeckHost itself; the process keeps running.
            Send(new Message(Message.Log) { Level = "Error", Text = $"{action.Name} ({message.Type}) failed: {ex}" });
            if (message.Type == Message.Render)
            {
                Send(new Message(Message.Drawn) { Id = message.Id, Commands = [] });
            }
        }
    }

    private ActionContext ContextFor(WireContext wire) => new(wire.DeviceId, wire.ControlId, new ForwardingStore(this), wire.EventType, wire.Value)
    {
        Settings = wire.Settings,
        ProfileId = wire.ProfileId,
        Host = new ForwardingHost(this),
    };

    internal void Send(Message message)
    {
        var line = Wire.Encode(message);
        lock (_writeLock)
        {
            if (_writer is null)
            {
                return;
            }

            try
            {
                _writer.Write(line);
                _writer.Write('\n');
                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // DeckHost is gone; the read loop ends too.
            }
        }
    }

    public void Dispose()
    {
        try
        {
            (_plugin as IDisposable)?.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        _context?.Unload();
        _variables.Dispose();
        lock (_writeLock)
        {
            _writer = null;
        }
    }

    /// <summary>The plugin's variables: kept here, and sent to DeckHost when the plugin publishes.</summary>
    private sealed class ForwardingStore(PluginProcessServer server) : IVariableStore
    {
        public IObservable<T> Observe<T>(string key) => server._variables.Observe<T>(key);

        public void Publish<T>(string key, T value)
        {
            server._variables.Publish(key, value);
            if (WireValue.From(value) is { } wire)
            {
                server.Send(new Message(Message.Publish) { Key = key, Value = wire });
            }
        }

        public bool TryGetValue<T>(string key, out T value) => server._variables.TryGetValue(key, out value);
    }

    /// <summary>The plugin's <see cref="IActionHost"/>: every call goes to DeckHost.</summary>
    private sealed class ForwardingHost(PluginProcessServer server) : IActionHost
    {
        public void RequestRender(string deviceId) => Call("requestRender", deviceId);

        public void ActivateProfile(string deviceId, string profileIdOrName) => Call("activateProfile", deviceId, profileIdOrName);

        public void ActivateRelativeProfile(string deviceId, int offset) => Call("activateRelativeProfile", deviceId, offset.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void ActivateRelativePage(string deviceId, int offset) => Call("activateRelativePage", deviceId, offset.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void ActivatePage(string deviceId, int number) => Call("activatePage", deviceId, number.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void OpenFolder(string deviceId, string folderId) => Call("openFolder", deviceId, folderId);

        public void CloseFolder(string deviceId) => Call("closeFolder", deviceId);

        private void Call(string call, params string[] arguments) => server.Send(new Message(Message.HostCall) { Call = call, Arguments = arguments });
    }
}
