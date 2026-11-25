using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckHost.Core.StreamDeck;

/// <summary>
/// Runs Elgato Stream Deck SDK plugins the way Stream Deck does: each
/// plugin is started as its own process (native .exe or Node.js) with
/// <c>-port -pluginUUID -registerEvent -info</c> and talks JSON over a local WebSocket. DeckHost
/// translates its controls to Stream Deck "contexts" (keys = Keypad, knobs = Encoder) and draws
/// what the plugin sends with setTitle / setImage. Property inspectors open in the default
/// browser, served from the same local server.
/// </summary>
public sealed class StreamDeckRuntime : IDisposable, Plugins.IPluginStateTransfer
{
    private const string ApplicationVersion = "6.9.0";
    private const int MaxQueuedMessages = 256;

    private readonly string _dataDirectory;
    private readonly Action<string, string> _log;
    private readonly StreamDeckServer _server;
    private readonly ConcurrentDictionary<string, RunningPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, InstanceState> _instances = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Connection> _inspectors = new(StringComparer.Ordinal);
    private readonly Lock _settingsLock = new();
    private readonly JsonObject _instanceSettings;
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, byte[]?> _svgCache = new(StringComparer.OrdinalIgnoreCase);

    public StreamDeckRuntime(string dataDirectory, string logDirectory, Action<string, string> log)
    {
        _dataDirectory = dataDirectory;
        _logDirectory = logDirectory;
        _log = log;
        Directory.CreateDirectory(Path.Combine(dataDirectory, "settings"));
        _instanceSettings = ReadJson(InstanceSettingsPath) as JsonObject ?? [];
        _server = new StreamDeckServer(HandleWebSocketAsync, HandleGet);
    }

    /// <summary>
    /// Waits before restarting a plugin whose process ended unexpectedly, in order; after the last one
    /// the plugin stays stopped (<see cref="ProblemOf"/>). A process that ran for
    /// <see cref="StableRunTime"/> starts the count again.
    /// </summary>
    public IReadOnlyList<TimeSpan> RestartDelays { get; set; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

    public TimeSpan StableRunTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Raised (on a background thread, with the plugin's UUID) when a plugin stopped, restarted or gave up.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>An OpenAction device plugin (UUID) sent a device message: registerDevice, keyDown, … (the whole message).</summary>
    public event Action<string, string, JsonObject>? DevicePluginMessage;

    /// <summary>A device plugin connected (its devices may register now).</summary>
    public event Action<string>? DevicePluginConnected;

    /// <summary>A device plugin's connection ended: its devices are no longer there.</summary>
    public event Action<string>? DevicePluginDisconnected;

    /// <summary>A device plugin is no longer connected (stopped, ended or closed its socket): its devices are gone.</summary>
    private void NotifyDevicePluginGone(RunningPlugin running)
    {
        if (running.Manifest.IsDevicePlugin)
        {
            DevicePluginDisconnected?.Invoke(running.Manifest.Uuid);
        }
    }

    /// <summary>Sends a message to an OpenAction device plugin (setImage, setBrightness); queued until it connects.</summary>
    public void SendToDevicePlugin(string pluginUuid, JsonObject message) => Send(pluginUuid, message);

    /// <summary>"Runs in its own process (pid 1234)", "Restarting…"; null for a plugin that isn't running.</summary>
    public string? StatusOf(string pluginUuid) => _plugins.TryGetValue(pluginUuid, out var running) ? running.Status : null;

    /// <summary>The name in the plugin's manifest; null for a plugin that isn't started.</summary>
    public string? NameOf(string pluginUuid) => _plugins.TryGetValue(pluginUuid, out var running) ? running.Manifest.Name : null;

    /// <summary>Why the plugin stopped for good; null while it runs (or is being restarted).</summary>
    public string? ProblemOf(string pluginUuid) => _plugins.TryGetValue(pluginUuid, out var running) ? running.Problem : null;

    /// <summary>Devices reported to plugins in the -info argument and deviceDidConnect.</summary>
    public Func<IReadOnlyCollection<DeviceDefinition>> DevicesProvider { get; set; } = static () => [];

    public int Port => _server.Port;

    private string InstanceSettingsPath => Path.Combine(_dataDirectory, "settings", "instances.json");

    public StreamDeckApplicationPlugin CreatePlugin(StreamDeckManifest manifest) => new(this, manifest);

    /// <summary>Starts the plugin's process. Throws when it can't run on this system.</summary>
    public void Start(StreamDeckManifest manifest)
    {
        if (manifest.UnsupportedReason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        var running = _plugins.GetOrAdd(manifest.Uuid, _ => new RunningPlugin(manifest));
        if (running.Process is { HasExited: false })
        {
            return;
        }

        if (manifest.IsHtml)
        {
            StartHtml(manifest, running);
            return;
        }

        var programPath = Path.GetFullPath(Path.Combine(manifest.Directory, manifest.CodePath!));
        var extension = Path.GetExtension(programPath).ToLowerInvariant();
        var isNode = extension is ".js" or ".mjs" or ".cjs";
        var startInfo = new ProcessStartInfo(isNode ? "node" : programPath)
        {
            WorkingDirectory = manifest.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (isNode)
        {
            startInfo.ArgumentList.Add(programPath);
        }

        var port = Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var arguments = running.PositionalArguments
            ? new[] { port, manifest.Uuid, "registerPlugin", "en" }
            : ["-port", port, "-pluginUUID", manifest.Uuid, "-registerEvent", "registerPlugin", "-info", BuildInfo(manifest).ToJsonString()];
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("The plugin process did not start.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (isNode)
        {
            throw new InvalidOperationException("This plugin needs Node.js 20 or newer on the PATH (nodejs.org).", ex);
        }

        var logPath = Path.Combine(_logDirectory, manifest.Uuid + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        KeepPreviousLog(logPath);
        var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
        WriteProcessLog(logWriter, $"--- {manifest.Name} {manifest.Version} started {DateTime.Now:yyyy-MM-dd HH:mm:ss} (pid {process.Id}{(running.Restarts > 0 ? $", restart {running.Restarts}" : string.Empty)})");
        process.OutputDataReceived += (_, e) => WriteProcessLog(logWriter, e.Data);
        process.ErrorDataReceived += (_, e) => WriteProcessLog(logWriter, e.Data);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            _log(running.Stopping ? "Info" : "Warning", $"Stream Deck plugin {manifest.Name} stopped (exit code {SafeExitCode(process)}). Its log: {logPath}");
            lock (logWriter)
            {
                logWriter.Dispose();
            }

            OnProcessExited(running, process);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        running.Process = process;
        running.Stopping = false;
        running.StartedAt = Environment.TickCount64;
        running.Problem = null;
        running.Status = $"Runs in its own process (pid {process.Id})" + (running.Restarts > 0 ? $", restarted {running.Restarts}×" : string.Empty);
        _log("Info", $"Started Stream Deck plugin {manifest.Name} ({(isNode ? "Node.js" : Path.GetFileName(programPath))}).");
    }

    /// <summary>
    /// A plugin's process ended. Unless DeckHost stopped it, it's started again after a moment: the
    /// messages meant for the old process are dropped, and once the new one registers it's told again
    /// what's on the devices (willAppear), so its keys come back.
    /// </summary>
    private void OnProcessExited(RunningPlugin running, Process process)
    {
        if (running.Stopping || !ReferenceEquals(running.Process, process) || !_plugins.TryGetValue(running.Manifest.Uuid, out var current) || !ReferenceEquals(current, running))
        {
            return;
        }

        running.Connection = null;
        NotifyDevicePluginGone(running);
        running.Reappear = true;
        if (!running.Registered)
        {
            if (!running.TriedPositional)
            {
                // Ended without ever connecting: maybe it reads its arguments by position (MiraBox's
                // older StreamDock template). One try that way, right away; messages stay queued.
                running.TriedPositional = true;
                running.PositionalArguments = true;
                _log("Info", $"Stream Deck plugin {running.Manifest.Name} ended without connecting; trying the arguments the way older StreamDock plugins read them.");
                _ = Task.Run(() => Restart(running));
                return;
            }

            // That didn't help either: back to Stream Deck's arguments for the usual restarts.
            running.PositionalArguments = false;
        }

        running.Pending.Clear();
        if (Environment.TickCount64 - running.StartedAt >= StableRunTime.TotalMilliseconds)
        {
            running.Restarts = 0;
        }

        ScheduleRestart(running);
    }

    private void ScheduleRestart(RunningPlugin running)
    {
        var name = running.Manifest.Name;
        if (running.Restarts >= RestartDelays.Count)
        {
            running.Problem = $"{name} stopped {running.Restarts + 1} times in a row, so DeckHost stopped restarting it. Disable and enable it to try again; its log is in the plugin logs folder.";
            running.Status = "Stopped";
            _log("Error", running.Problem);
            StatusChanged?.Invoke(running.Manifest.Uuid);
            return;
        }

        var delay = RestartDelays[running.Restarts++];
        running.Status = $"Stopped unexpectedly; restarting ({running.Restarts} of {RestartDelays.Count})…";
        _log("Warning", $"Restarting Stream Deck plugin {name} in {delay.TotalSeconds:0.#} s.");
        StatusChanged?.Invoke(running.Manifest.Uuid);
        _ = Task.Delay(delay).ContinueWith(_ => Restart(running), TaskScheduler.Default);
    }

    private void Restart(RunningPlugin running)
    {
        if (running.Stopping || !_plugins.TryGetValue(running.Manifest.Uuid, out var current) || !ReferenceEquals(current, running))
        {
            return;
        }

        try
        {
            Start(running.Manifest);
            StatusChanged?.Invoke(running.Manifest.Uuid);
        }
        catch (InvalidOperationException ex)
        {
            _log("Error", $"Could not restart Stream Deck plugin {running.Manifest.Name}: {ex.Message}");
            ScheduleRestart(running);
        }
    }

    /// <summary>Runs an HTML plugin in the app's hidden browser, the way the Stream Deck app does.</summary>
    private void StartHtml(StreamDeckManifest manifest, RunningPlugin running)
    {
        if (running.Html is not null)
        {
            return;
        }

        if (HtmlPluginHost is not { } host)
        {
            throw new InvalidOperationException(OperatingSystem.IsWindows()
                ? "HTML-based plugins need the Microsoft Edge WebView2 runtime, which isn't available here."
                : "This plugin is a web page (an HTML plugin); DeckHost runs those only on Windows for now.");
        }

        var page = new Uri($"http://127.0.0.1:{Port}/plugins/{Uri.EscapeDataString(manifest.Uuid)}/{manifest.CodePath!.Replace(Path.DirectorySeparatorChar, '/').TrimStart('/')}");
        var info = System.Text.Json.JsonSerializer.Serialize(BuildInfo(manifest).ToJsonString());
        var uuid = System.Text.Json.JsonSerializer.Serialize(manifest.Uuid);
        var script = $$"""
            (function () {
              if (window.__deckhostConnected) { return; }
              var connect = window.connectElgatoStreamDeckSocket || window.connectSocket;
              if (typeof connect !== 'function') { console.error('DeckHost: the plugin page has no connectElgatoStreamDeckSocket function.'); return; }
              window.__deckhostConnected = true;
              connect({{Port}}, {{uuid}}, 'registerPlugin', {{info}});
            })();
            """;
        running.Html = host.Start(manifest.Uuid, page, script, _log);
        running.Stopping = false;
        running.Status = "Runs in DeckHost's browser (its own browser process)";
        _log("Info", $"Started Stream Deck plugin {manifest.Name} (HTML, in DeckHost's browser).");
    }

    /// <summary>Runs HTML-based plugins; set by the app (WebView2). Without it those plugins can't start.</summary>
    public IHtmlPluginHost? HtmlPluginHost { get; set; }

    public void Stop(string pluginUuid)
    {
        if (!_plugins.TryRemove(pluginUuid, out var running))
        {
            return;
        }

        running.Stopping = true;
        if (running.Html is { } html)
        {
            running.Html = null;
            try
            {
                html.Dispose();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
            }

            _log("Info", $"Stream Deck plugin {running.Manifest.Name} stopped.");
        }

        try
        {
            if (running.Process is { HasExited: false } process)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        running.Connection?.Close();
        running.Connection = null;
        NotifyDevicePluginGone(running);
        foreach (var key in _instances.Where(pair => string.Equals(pair.Value.PluginUuid, pluginUuid, StringComparison.OrdinalIgnoreCase)).Select(static pair => pair.Key).ToList())
        {
            _instances.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        // All at once: stopping one after another waits for each process in turn, which added up
        // to more than Windows gives an application that is closing at shutdown.
        var running = _plugins.Values.ToList();
        foreach (var plugin in running.Where(static plugin => plugin.Html is not null))
        {
            Stop(plugin.Manifest.Uuid); // Browser pages belong to the thread that made them.
        }

        Parallel.ForEach(running.Where(static plugin => plugin.Html is null), plugin => Stop(plugin.Manifest.Uuid));
        _server.Dispose();
    }

    // ---------------------------------------------------------------- calls from the action adapter

    internal void Appear(StreamDeckManifest manifest, StreamDeckActionManifest action, ActionContext context)
    {
        var state = GetOrCreateInstance(manifest, action, context.DeviceId, context.ProfileId, context.ControlId, context.Host);
        state.Visible = true;
        Send(manifest.Uuid, Envelope("willAppear", state));
    }

    internal void Disappear(StreamDeckManifest manifest, StreamDeckActionManifest action, ActionContext context)
    {
        if (_instances.TryGetValue(ContextId(context.DeviceId, context.ProfileId, context.ControlId, action.Uuid), out var state))
        {
            state.Visible = false;
            Send(manifest.Uuid, Envelope("willDisappear", state));
        }
    }

    internal void Execute(StreamDeckManifest manifest, StreamDeckActionManifest action, ActionContext context)
    {
        var state = GetOrCreateInstance(manifest, action, context.DeviceId, context.ProfileId, context.ControlId, context.Host);
        var encoder = IsDial(state.Controller);
        _log("Debug", $"{manifest.Name}: {context.EventType} on {context.ControlId} → {action.Uuid} (state {state.State}).");
        switch (context.EventType)
        {
            case InputCapabilities.Press when context.ControlId.StartsWith("touch.", StringComparison.Ordinal):
                // Touch zones only report a tap: send the full key press.
                Send(manifest.Uuid, Envelope("keyDown", state));
                Send(manifest.Uuid, Envelope("keyUp", state));
                break;
            case InputCapabilities.Press:
                Send(manifest.Uuid, Envelope(encoder ? "dialDown" : "keyDown", state));
                break;
            case InputCapabilities.Release:
                Send(manifest.Uuid, Envelope(encoder ? "dialUp" : "keyUp", state));
                break;
            case InputCapabilities.Rotate:
                Send(manifest.Uuid, Envelope("dialRotate", state, new JsonObject { ["ticks"] = context.Value, ["pressed"] = false }));
                break;
        }
    }

    internal void Render(StreamDeckManifest manifest, StreamDeckActionManifest action, IRenderContext context)
    {
        _instances.TryGetValue(ContextId(context.DeviceId, context.ProfileId, context.ControlId, action.Uuid), out var state);
        var stateIndex = Math.Clamp(state?.State ?? 0, 0, Math.Max(0, action.States.Count - 1));
        var manifestState = action.States.Count > 0 ? action.States[stateIndex] : null;

        // Custom text from Customize wins over the plugin's title, like a title override in Stream Deck.
        var ownTitle = context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), out var ownText) &&
                       !string.IsNullOrWhiteSpace(ownText)
            ? ownText
            : null;

        if (state is { Feedback: { } feedback } && IsDial(state.Controller))
        {
            // A dial action: what it shows on its touch-strip segment on a Stream Deck +, drawn where
            // DeckHost shows the knob (the touch zone above it).
            context.DrawReading(ownTitle ?? feedback.Title ?? state.Title ?? manifestState?.Title ?? action.Name, feedback.Value ?? string.Empty, feedback.Indicator);
        }
        else
        {
            // The user's own image (Customize) replaces the plugin's image. A strip is too low for one.
            var hasOwnImage = context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceImage), out var ownImage) &&
                              !string.IsNullOrWhiteSpace(ownImage);
            if (!hasOwnImage && !context.IsCompact)
            {
                // Drawn for a Stream Deck, where an unlit key is black: that black becomes transparent,
                // so the control's and the profile's background show around the plugin's picture.
                if (state?.Image is { } custom)
                {
                    context.DrawEncodedImage(custom, clearEdgeBlack: true);
                }
                else if (DefaultImage(manifest, manifestState?.Image ?? action.Icon) is { } defaultImage)
                {
                    context.DrawEncodedImage(defaultImage, clearEdgeBlack: true);
                }
            }

            var title = ownTitle ?? state?.Title ?? manifestState?.Title ?? (context.IsCompact ? action.Name : null);
            if (!string.IsNullOrWhiteSpace(title))
            {
                context.DrawLabel(title, TextVerticalAlignment.Bottom, Math.Max(10f, context.Height * 0.14f));
            }
        }

        if (state?.Flash is { } flash && flash.Until > DateTime.UtcNow)
        {
            context.FillRoundedRect(context.Width * 0.25, context.Height * 0.25, context.Width * 0.5, context.Height * 0.5, context.Height * 0.12, flash.Ok ? 0xE016A34Au : 0xE0DC2626u);
            context.DrawCenteredText(flash.Ok ? "OK" : "!", context.Width / 2d, context.Height / 2d, context.Height * 0.22f);
        }
    }

    /// <summary>Local address of the action's property inspector page (shown in DeckHost's own browser view).</summary>
    internal Uri? GetPropertyInspectorUri(StreamDeckManifest manifest, StreamDeckActionManifest action, ActionContext context)
    {
        if (action.PropertyInspectorPath is null)
        {
            return null;
        }

        var state = GetOrCreateInstance(manifest, action, context.DeviceId, context.ProfileId, context.ControlId, context.Host);
        return new Uri($"http://127.0.0.1:{Port}/pi/{Uri.EscapeDataString(state.Context)}");
    }

    /// <summary>
    /// Copies every plugin setting of one profile to another, so a duplicated profile behaves
    /// exactly like the original.
    /// </summary>
    public void CopyProfileSettings(string sourceProfileId, string targetProfileId)
    {
        lock (_settingsLock)
        {
            foreach (var (context, settings) in _instanceSettings.ToList())
            {
                var parts = context.Split(ContextSeparator);
                if (parts.Length == 4 && parts[1] == sourceProfileId)
                {
                    _instanceSettings[string.Join(ContextSeparator, parts[0], targetProfileId, parts[2], parts[3])] = settings?.DeepClone();
                }
            }

            WriteJson(InstanceSettingsPath, _instanceSettings);
        }
    }

    /// <summary>Moves (or copies) the plugin settings of a control, when its binding is dragged to another control.</summary>
    public void MoveControlSettings(string deviceId, string profileId, string sourceControlId, string targetControlId, bool copy)
    {
        lock (_settingsLock)
        {
            foreach (var (context, settings) in _instanceSettings.ToList())
            {
                var parts = context.Split(ContextSeparator);
                if (parts.Length == 4 && parts[0] == deviceId && parts[1] == profileId && parts[2] == sourceControlId)
                {
                    _instanceSettings[string.Join(ContextSeparator, deviceId, profileId, targetControlId, parts[3])] = settings?.DeepClone();
                    if (!copy)
                    {
                        _instanceSettings.Remove(context);
                    }
                }
            }

            WriteJson(InstanceSettingsPath, _instanceSettings);
        }
    }

    /// <summary>The plugin's default image (PNG/JPG/GIF, or SVG rendered to PNG), cached.</summary>
    private byte[]? DefaultImage(StreamDeckManifest manifest, string? relativePath)
    {
        if (manifest.ResolveImage(relativePath) is not { } path)
        {
            return null;
        }

        return _svgCache.GetOrAdd(path, static file =>
        {
            try
            {
                var bytes = File.ReadAllBytes(file);
                return file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? Rendering.SvgRasterizer.ToPng(bytes) : bytes;
            }
            catch (IOException)
            {
                return null;
            }
        });
    }

    // ---------------------------------------------------------------- WebSocket protocol

    private async Task HandleWebSocketAsync(WebSocket socket)
    {
        var connection = new Connection(socket);
        string? pluginUuid = null;
        string? inspectorContext = null;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await connection.ReceiveAsync().ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                var eventName = message["event"]?.GetValue<string>();
                if (eventName == "registerPlugin" && message["uuid"]?.GetValue<string>() is { } uuid && _plugins.TryGetValue(uuid, out var running))
                {
                    pluginUuid = uuid;
                    running.Connection = connection;
                    running.Registered = true;
                    _log("Info", $"Stream Deck plugin {running.Manifest.Name} connected.");
                    if (running.Manifest.IsDevicePlugin)
                    {
                        DevicePluginConnected?.Invoke(uuid);
                    }

                    // A device plugin drives its own devices; the other devices are none of its business.
                    foreach (var device in running.Manifest.IsDevicePlugin ? [] : DevicesProvider())
                    {
                        await connection.SendAsync(DeviceDidConnect(device)).ConfigureAwait(false);
                    }

                    while (running.Pending.TryDequeue(out var pending))
                    {
                        await connection.SendAsync(pending).ConfigureAwait(false);
                    }

                    if (running.Reappear)
                    {
                        // A restarted plugin starts from nothing: what it had on the devices appears again.
                        running.Reappear = false;
                        foreach (var instance in _instances.Values.Where(instance => instance.Visible && string.Equals(instance.PluginUuid, uuid, StringComparison.OrdinalIgnoreCase)).ToList())
                        {
                            await connection.SendAsync(Envelope("willAppear", instance)).ConfigureAwait(false);
                            instance.RequestRender();
                        }
                    }

                    continue;
                }

                if (eventName == "registerPropertyInspector" && message["uuid"]?.GetValue<string>() is { } context)
                {
                    inspectorContext = context;
                    _inspectors[context] = connection;
                    if (_instances.TryGetValue(context, out var instance))
                    {
                        Send(instance.PluginUuid, Envelope("propertyInspectorDidAppear", instance, includePayload: false));
                    }

                    continue;
                }

                if (pluginUuid is not null)
                {
                    HandlePluginMessage(pluginUuid, eventName, message);
                }
                else if (inspectorContext is not null)
                {
                    HandleInspectorMessage(inspectorContext, eventName, message);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or JsonException or OperationCanceledException or InvalidOperationException)
        {
        }
        finally
        {
            if (inspectorContext is not null && _inspectors.TryRemove(inspectorContext, out _) && _instances.TryGetValue(inspectorContext, out var instance))
            {
                Send(instance.PluginUuid, Envelope("propertyInspectorDidDisappear", instance, includePayload: false));
            }

            if (pluginUuid is not null && _plugins.TryGetValue(pluginUuid, out var running) && ReferenceEquals(running.Connection, connection))
            {
                running.Connection = null;
                NotifyDevicePluginGone(running);
            }
        }
    }

    private void HandlePluginMessage(string pluginUuid, string? eventName, JsonObject message)
    {
        var context = message["context"]?.GetValue<string>() ?? string.Empty;
        var payload = message["payload"] as JsonObject;
        _instances.TryGetValue(context, out var state);

        switch (eventName)
        {
            case "setTitle" when state is not null:
                state.Title = payload?["title"]?.GetValue<string>();
                state.RequestRender();
                break;
            case "setImage" when state is not null:
                state.Image = DecodeImage(payload?["image"]?.GetValue<string>());
                state.RequestRender();
                break;
            case "setFeedback" when state is not null:
                state.Feedback = DialFeedback.Merge(state.Feedback, payload);
                state.RequestRender();
                break;
            case "setState" when state is not null:
                state.State = payload?["state"]?.GetValue<int>() ?? 0;
                state.Image = null;
                state.Title = null;
                state.RequestRender();
                break;
            case "showOk" or "showAlert" when state is not null:
                state.Flash = (DateTime.UtcNow.AddSeconds(1.5), eventName == "showOk");
                state.RequestRender();
                _ = Task.Delay(1600).ContinueWith(_ => state.RequestRender(), TaskScheduler.Default);
                break;
            case "setSettings" when state is not null:
                SaveInstanceSettings(context, payload ?? []);
                SendToInspector(context, Envelope("didReceiveSettings", state));
                break;
            case "getSettings" when state is not null:
                Send(pluginUuid, Envelope("didReceiveSettings", state));
                break;
            case "setGlobalSettings":
                SaveGlobalSettings(pluginUuid, payload ?? []);
                foreach (var inspector in _instances.Values.Where(instance => instance.PluginUuid == pluginUuid))
                {
                    SendToInspector(inspector.Context, GlobalSettingsMessage(pluginUuid));
                }

                break;
            case "getGlobalSettings":
                Send(pluginUuid, GlobalSettingsMessage(pluginUuid));
                break;
            case "sendToPropertyInspector":
                SendToInspector(context, new JsonObject
                {
                    ["event"] = "sendToPropertyInspector",
                    ["action"] = state?.ActionUuid,
                    ["context"] = context,
                    ["payload"] = payload?.DeepClone(),
                });
                break;
            case "openUrl":
                OpenUrl(payload?["url"]?.GetValue<string>());
                break;
            case "registerDevice" or "deregisterDevice" or "rerenderImages" or "keyDown" or "keyUp" or "encoderChange" or "encoderDown" or "encoderUp"
                when _plugins.TryGetValue(pluginUuid, out var devicePlugin) && devicePlugin.Manifest.IsDevicePlugin:
                DevicePluginMessage?.Invoke(pluginUuid, eventName, message);
                break;
            case "logMessage":
                _log("Debug", $"[{pluginUuid}] {payload?["message"]?.GetValue<string>()}");
                break;
        }
    }

    private void HandleInspectorMessage(string context, string? eventName, JsonObject message)
    {
        if (!_instances.TryGetValue(context, out var state))
        {
            return;
        }

        var payload = message["payload"] as JsonObject;
        switch (eventName)
        {
            case "sendToPlugin":
                Send(state.PluginUuid, new JsonObject
                {
                    ["event"] = "sendToPlugin",
                    ["action"] = state.ActionUuid,
                    ["context"] = context,
                    ["payload"] = payload?.DeepClone(),
                });
                break;
            case "setSettings":
                SaveInstanceSettings(context, payload ?? []);
                Send(state.PluginUuid, Envelope("didReceiveSettings", state));
                break;
            case "getSettings":
                SendToInspector(context, Envelope("didReceiveSettings", state));
                break;
            case "setGlobalSettings":
                SaveGlobalSettings(state.PluginUuid, payload ?? []);
                Send(state.PluginUuid, GlobalSettingsMessage(state.PluginUuid));
                break;
            case "getGlobalSettings":
                SendToInspector(context, GlobalSettingsMessage(state.PluginUuid));
                break;
            case "openUrl":
                OpenUrl(payload?["url"]?.GetValue<string>());
                break;
        }
    }

    private void Send(string pluginUuid, JsonObject message)
    {
        if (!_plugins.TryGetValue(pluginUuid, out var running))
        {
            return;
        }

        if (running.Connection is { } connection)
        {
            _ = connection.SendAsync(message);
        }
        else if (running.Pending.Count < MaxQueuedMessages)
        {
            // The plugin hasn't connected yet: deliver once it registers.
            running.Pending.Enqueue(message);
        }
    }

    private void SendToInspector(string context, JsonObject message)
    {
        if (_inspectors.TryGetValue(context, out var connection))
        {
            _ = connection.SendAsync(message);
        }
    }

    // ---------------------------------------------------------------- helpers

    private InstanceState GetOrCreateInstance(StreamDeckManifest manifest, StreamDeckActionManifest action, string deviceId, string profileId, string controlId, IActionHost host)
    {
        var context = ContextId(deviceId, profileId, controlId, action.Uuid);
        var state = _instances.GetOrAdd(context, _ =>
        {
            var (row, column, controller) = Coordinates(controlId, action);
            return new InstanceState(context, manifest.Uuid, action.Uuid, deviceId, controlId, controller, row, column);
        });
        state.Host = host;
        return state;
    }

    // Device and control ids contain dots, so the parts are separated with '|'.
    private const char ContextSeparator = '|';

    private static string ContextId(string deviceId, string profileId, string controlId, string actionUuid) =>
        string.Join(ContextSeparator, deviceId, string.IsNullOrEmpty(profileId) ? "default" : profileId, controlId, actionUuid);

    private static (int Row, int Column, string Controller) Coordinates(string controlId, StreamDeckActionManifest action)
    {
        var dot = controlId.LastIndexOf('.');
        _ = int.TryParse(controlId.AsSpan(dot + 1), out var index);
        if (controlId.StartsWith("encoder.", StringComparison.Ordinal))
        {
            return (0, index, action.SupportsEncoder ? action.EncoderController : "Keypad");
        }

        return controlId.StartsWith("touch.", StringComparison.Ordinal) ? (2, index, "Keypad") : (index / 5, index % 5, "Keypad");
    }

    private JsonObject Envelope(string eventName, InstanceState state, JsonObject? extra = null, bool includePayload = true)
    {
        var message = new JsonObject
        {
            ["event"] = eventName,
            ["action"] = state.ActionUuid,
            ["context"] = state.Context,
            ["device"] = state.DeviceId,
        };

        if (includePayload)
        {
            var payload = new JsonObject
            {
                ["settings"] = LoadInstanceSettings(state.Context),
                ["coordinates"] = new JsonObject { ["column"] = state.Column, ["row"] = state.Row },
                ["controller"] = state.Controller,
                ["state"] = state.State,
                ["isInMultiAction"] = false,
            };

            foreach (var (key, value) in extra ?? [])
            {
                payload[key] = value?.DeepClone();
            }

            message["payload"] = payload;
        }

        return message;
    }

    private JsonObject BuildInfo(StreamDeckManifest manifest) => new()
    {
        ["application"] = new JsonObject
        {
            ["font"] = "Segoe UI",
            ["language"] = "en",
            ["platform"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux",
            ["platformVersion"] = Environment.OSVersion.Version.ToString(),
            ["version"] = ApplicationVersion,
        },
        ["plugin"] = new JsonObject { ["uuid"] = manifest.Uuid, ["version"] = manifest.Version },
        ["devicePixelRatio"] = 2,
        ["colors"] = new JsonObject
        {
            ["buttonPressedBackgroundColor"] = "#303030FF",
            ["buttonPressedBorderColor"] = "#646464FF",
            ["buttonPressedTextColor"] = "#969696FF",
            ["disabledColor"] = "#F7821B59",
            ["highlightColor"] = "#F7821BFF",
            ["mouseDownColor"] = "#CF6304FF",
        },
        // A device plugin gets none: the devices of others aren't its own (it would treat them as connected).
        ["devices"] = new JsonArray([.. manifest.IsDevicePlugin ? [] : DevicesProvider().Select(static device => (JsonNode)DeviceInfo(device))]),
    };

    private static JsonObject DeviceInfo(DeviceDefinition device) => new()
    {
        ["id"] = device.Id,
        ["name"] = device.Name,
        ["size"] = new JsonObject { ["columns"] = 5, ["rows"] = 2 },
        ["type"] = 7, // Stream Deck + (keys, dials and a touch strip), the closest match to the N4 Pro.
    };

    private static JsonObject DeviceDidConnect(DeviceDefinition device) => new()
    {
        ["event"] = "deviceDidConnect",
        ["device"] = device.Id,
        ["deviceInfo"] = DeviceInfo(device),
    };

    private JsonObject GlobalSettingsMessage(string pluginUuid) => new()
    {
        ["event"] = "didReceiveGlobalSettings",
        ["payload"] = new JsonObject { ["settings"] = LoadGlobalSettings(pluginUuid) },
    };

    /// <summary>
    /// The plugin's global settings as saved. Read under the same lock as <see cref="SaveGlobalSettings"/>:
    /// the plugin's and its settings page's connections run in parallel, and a read during a write used to
    /// get an empty file, so the plugin was sent <c>{}</c> and lost its settings (the Discord plugin its
    /// Client ID and Secret).
    /// </summary>
    internal JsonNode LoadGlobalSettings(string pluginUuid)
    {
        var path = GlobalSettingsPath(pluginUuid);
        lock (_settingsLock)
        {
            if (!File.Exists(path))
            {
                return new JsonObject();
            }

            if (ReadJson(path) is { } settings)
            {
                return settings;
            }
        }

        _log("Warning", $"The saved settings of Stream Deck plugin {pluginUuid} can't be read ({path}); it gets empty settings.");
        return new JsonObject();
    }

    private string GlobalSettingsPath(string pluginUuid) => Path.Combine(_dataDirectory, "settings", pluginUuid + ".global.json");

    private JsonNode LoadInstanceSettings(string context)
    {
        lock (_settingsLock)
        {
            return _instanceSettings[context]?.DeepClone() ?? new JsonObject();
        }
    }

    private void SaveInstanceSettings(string context, JsonObject settings)
    {
        lock (_settingsLock)
        {
            _instanceSettings[context] = settings.DeepClone();
            WriteJson(InstanceSettingsPath, _instanceSettings);
        }
    }

    internal void SaveGlobalSettings(string pluginUuid, JsonObject settings)
    {
        lock (_settingsLock)
        {
            WriteJson(GlobalSettingsPath(pluginUuid), settings);
        }
    }

    private static JsonNode? ReadJson(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the file whole or not at all: into <c>&lt;path&gt;.tmp</c> first, then over the old one, so a
    /// reader or a crash never sees it half written.
    /// </summary>
    internal static void WriteJson(string path, JsonNode node)
    {
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Decodes a setImage payload: a data: URL (base64 or URL-encoded) with PNG/JPG/GIF/WebP or SVG,
    /// or raw SVG markup. SVG is rendered to PNG.
    /// </summary>
    internal static byte[]? DecodeImage(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        byte[] data;
        if (image.TrimStart().StartsWith('<'))
        {
            data = Encoding.UTF8.GetBytes(image);
        }
        else
        {
            var comma = image.IndexOf(',');
            if (!image.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || comma < 0)
            {
                return null;
            }

            var header = image[..comma];
            var body = image[(comma + 1)..];
            try
            {
                data = header.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(body)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(body));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return Rendering.SvgRasterizer.IsSvg(data) ? Rendering.SvgRasterizer.ToPng(data) : data;
    }

    private void OpenUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>
    /// Keeps the log of the plugin's previous run as <c>&lt;uuid&gt;.previous.log</c> before a new run starts
    /// its log: after a crash and restart, what the plugin printed before it ended is still there.
    /// </summary>
    internal static void KeepPreviousLog(string logPath)
    {
        try
        {
            if (File.Exists(logPath))
            {
                File.Move(logPath, PreviousLogPath(logPath), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still open somewhere (an editor): the new run writes over it, as before.
        }
    }

    /// <summary>Where <see cref="KeepPreviousLog"/> puts the previous run's log.</summary>
    internal static string PreviousLogPath(string logPath) => Path.ChangeExtension(logPath, ".previous.log");

    private static void WriteProcessLog(StreamWriter writer, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (writer)
        {
            try
            {
                writer.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    /// <summary>Static files for property inspectors, and the page that connects one to the socket.</summary>
    private (byte[] Body, string ContentType)? HandleGet(string path)
    {
        path = path.Split('?')[0];
        if (path.StartsWith("/pi/", StringComparison.Ordinal))
        {
            var context = path["/pi/".Length..];
            if (!_instances.TryGetValue(context, out var state) || !_plugins.TryGetValue(state.PluginUuid, out var running))
            {
                return null;
            }

            var action = running.Manifest.Actions.FirstOrDefault(candidate => candidate.Uuid == state.ActionUuid);
            if (action?.PropertyInspectorPath is not { } inspector)
            {
                return null;
            }

            var actionInfo = Envelope("actionInfo", state);
            actionInfo.Remove("event");
            var page = $$"""
                <!doctype html><html><head><meta charset="utf-8"><title>{{System.Net.WebUtility.HtmlEncode(running.Manifest.Name)}} – {{System.Net.WebUtility.HtmlEncode(action.Name)}}</title>
                <style>html,body{margin:0;height:100%;background:#2D2D2D;font-family:Segoe UI,sans-serif}iframe{border:0;width:100%;height:100%}</style></head>
                <body><iframe id="pi" src="/plugins/{{Uri.EscapeDataString(running.Manifest.Uuid)}}/{{inspector.Replace('\\', '/')}}"></iframe>
                <script>
                document.getElementById('pi').addEventListener('load', function () {
                  var w = this.contentWindow;
                  var connect = w.connectElgatoStreamDeckSocket || w.connectOpenActionSocket || w.connectSocket;
                  if (connect) connect({{Port}}, {{JsonSerializer.Serialize(state.Context)}}, 'registerPropertyInspector', {{JsonSerializer.Serialize(BuildInfo(running.Manifest).ToJsonString())}}, {{JsonSerializer.Serialize(actionInfo.ToJsonString())}});
                });
                </script></body></html>
                """;
            return (Encoding.UTF8.GetBytes(page), "text/html; charset=utf-8");
        }

        if (path.StartsWith("/plugins/", StringComparison.Ordinal))
        {
            var rest = path["/plugins/".Length..];
            var slash = rest.IndexOf('/');
            if (slash <= 0 || !_plugins.TryGetValue(rest[..slash], out var running))
            {
                return null;
            }

            var root = Path.GetFullPath(running.Manifest.Directory);
            var file = Path.GetFullPath(Path.Combine(root, rest[(slash + 1)..].Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            {
                return null;
            }

            return (File.ReadAllBytes(file), ContentType(file));
        }

        return null;
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };

    /// <summary>A dial's controller name: Stream Deck's Encoder or MiraBox's Knob.</summary>
    private static bool IsDial(string controller) => controller is "Encoder" or "Knob";

    private sealed class RunningPlugin(StreamDeckManifest manifest)
    {
        /// <summary>
        /// Start with the arguments by position (port, uuid, register event, language), as MiraBox's
        /// older StreamDock Node.js template reads them, instead of Stream Deck's named ones.
        /// </summary>
        public bool PositionalArguments { get; set; }

        /// <summary>Tried <see cref="PositionalArguments"/> once after the plugin ended without registering.</summary>
        public bool TriedPositional { get; set; }

        /// <summary>The plugin connected and registered at least once (its arguments are right).</summary>
        public bool Registered { get; set; }

        public StreamDeckManifest Manifest { get; } = manifest;

        public Process? Process { get; set; }

        /// <summary>The hidden browser page of an HTML plugin.</summary>
        public IDisposable? Html { get; set; }

        public Connection? Connection { get; set; }

        public bool Stopping { get; set; }

        public ConcurrentQueue<JsonObject> Pending { get; } = new();

        /// <summary>Restarts since the process last ran long enough (<see cref="StableRunTime"/>).</summary>
        public int Restarts { get; set; }

        public long StartedAt { get; set; }

        /// <summary>Send willAppear for the visible instances when the (restarted) plugin registers.</summary>
        public bool Reappear { get; set; }

        public string? Status { get; set; }

        public string? Problem { get; set; }
    }

    private sealed class InstanceState(string context, string pluginUuid, string actionUuid, string deviceId, string controlId, string controller, int row, int column)
    {
        public string Context { get; } = context;

        public string PluginUuid { get; } = pluginUuid;

        public string ActionUuid { get; } = actionUuid;

        public string DeviceId { get; } = deviceId;

        public string ControlId { get; } = controlId;

        public string Controller { get; } = controller;

        public int Row { get; } = row;

        public int Column { get; } = column;

        public IActionHost Host { get; set; } = NullActionHost.Instance;

        public bool Visible { get; set; }

        public string? Title { get; set; }

        public byte[]? Image { get; set; }

        /// <summary>What a dial action sent with setFeedback; null until it does.</summary>
        public DialFeedback? Feedback { get; set; }

        public int State { get; set; }

        public (DateTime Until, bool Ok)? Flash { get; set; }

        public void RequestRender() => Host.RequestRender(DeviceId, ControlId);
    }

    private sealed class Connection(WebSocket socket)
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public async Task SendAsync(JsonObject message)
        {
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<JsonObject?> ReceiveAsync()
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var result = await socket.ReceiveAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                buffer.Write(chunk, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return JsonNode.Parse(buffer.ToArray()) as JsonObject;
                }
            }
        }

        public void Close()
        {
            try
            {
                socket.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
