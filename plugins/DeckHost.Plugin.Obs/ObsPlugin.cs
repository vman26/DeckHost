using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Text.Json.Nodes;

namespace DeckHost.Plugin.Obs;

/// <summary>Entry point DeckHost loads from the package (public, parameterless constructor).</summary>
public sealed class ObsPlugin : IApplicationPlugin, IDisposable
{
    private readonly ObsConnectionPool _connections = new();

    public ObsPlugin()
    {
        Actions =
        [
            new ObsAction(_connections, "obs.scene", "Switch Scene", "Switches OBS to a scene.",
                [new("scene", "Scene name", Placeholder: "Exactly as named in OBS")],
                settings => settings.Get("scene", "Scene"),
                settings => ("SetCurrentProgramScene", new JsonObject { ["sceneName"] = Require(settings, "scene", "Enter a scene name.") })),
            new ObsAction(_connections, "obs.mute", "Toggle Mute", "Mutes or unmutes an audio source, such as your microphone.",
                [new("input", "Audio source", Placeholder: "Mic/Aux")],
                settings => settings.Get("input", "Mute"),
                settings => ("ToggleInputMute", new JsonObject { ["inputName"] = Require(settings, "input", "Enter an audio source name.") })),
            new ObsAction(_connections, "obs.stream", "Toggle Streaming", "Starts or stops the stream.",
                [],
                _ => "Stream",
                _ => ("ToggleStream", null)),
            new ObsAction(_connections, "obs.record", "Toggle Recording", "Starts or stops recording.",
                [],
                _ => "Record",
                _ => ("ToggleRecord", null)),
            new ObsAction(_connections, "obs.replay", "Save Replay", "Saves the replay buffer (it must be running in OBS).",
                [],
                _ => "Replay",
                _ => ("SaveReplayBuffer", null)),
        ];
    }

    public string Id => "deckhost.plugin.obs";

    public string Name => "OBS Studio";

    public string Version => "1.0.0";

    public string? Author => "DeckHost";

    public string? Description => "Controls OBS Studio through obs-websocket.";

    public IReadOnlyCollection<IDeckAction> Actions { get; }

    public void Dispose() => _connections.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));

    private static string Require(ActionSettings settings, string key, string message)
    {
        var value = settings.Get(key);
        return string.IsNullOrWhiteSpace(value) ? throw new ObsException(message) : value;
    }
}

/// <summary>
/// An OBS request bound to a control. Each action has the connection settings so different
/// controls can talk to different OBS instances; actions with the same settings share one connection.
/// </summary>
internal sealed class ObsAction(
    ObsConnectionPool connections,
    string id,
    string name,
    string description,
    IReadOnlyList<ActionSettingDefinition> actionSettings,
    Func<ActionSettings, string> title,
    Func<ActionSettings, (string RequestType, JsonObject? Data)> buildRequest) : IDeckAction
{
    private const string HostKey = "host";
    private const string PortKey = "port";
    private const string PasswordKey = "password";

    public string Id { get; } = id;

    public string Name { get; } = name;

    public string? Description { get; } = description;

    public InputCapabilities RequiredInputs => InputCapabilities.Press;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.None;

    public IReadOnlyList<ActionSettingDefinition> Settings { get; } =
    [
        .. actionSettings,
        new(HostKey, "OBS computer", DefaultValue: "localhost", Description: "localhost when OBS runs on this PC."),
        new(PortKey, "Port", ActionSettingKind.Number, "4455"),
        new(PasswordKey, "Password", ActionSettingKind.Secret, Description: "From OBS: Tools > WebSocket Server Settings > Show Connect Info."),
    ];

    public void Execute(ActionContext context)
    {
        if (context.EventType == InputCapabilities.Release)
        {
            return;
        }

        var statusKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.Status);
        var errorKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError);
        var settings = context.Settings;
        var endpoint = new ObsEndpoint(settings.Get(HostKey, "localhost"), settings.GetInt(PortKey, 4455), settings.Get(PasswordKey));

        // Network I/O: never on the host worker.
        _ = Task.Run(async () =>
        {
            try
            {
                var (requestType, data) = buildRequest(settings);
                await connections.RequestAsync(endpoint, requestType, data, CancellationToken.None).ConfigureAwait(false);
                context.Variables.Publish(errorKey, string.Empty);
                context.Variables.Publish(statusKey, DateTimeOffset.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex) when (ex is ObsException or ObjectDisposedException)
            {
                context.Variables.Publish(errorKey, ex.Message.Length > 40 ? ex.Message[..40] + "…" : ex.Message);
            }

            context.Host.RequestRender(context.DeviceId);
        });
    }

    public void Render(IRenderContext context)
    {
        var variables = context.Variables;
        variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), out var customText);
        variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceImage), out var image);
        var heading = !string.IsNullOrWhiteSpace(customText) ? customText : string.IsNullOrWhiteSpace(image) ? title(context.Settings) : string.Empty;

        var titleSize = Math.Max(12f, context.Height * 0.17f);
        var lineSize = Math.Max(10f, context.Height * 0.12f);
        context.DrawCenteredText(heading, context.Width / 2d, context.Height * 0.42d, titleSize);

        if (variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError), out var error) && !string.IsNullOrWhiteSpace(error))
        {
            context.DrawCenteredText(error, context.Width / 2d, context.Height * 0.76d, lineSize, 0xFFFCA5A5);
        }
        else
        {
            context.DrawCenteredText("OBS", context.Width / 2d, context.Height * 0.76d, lineSize, 0xFF93C5FD);
        }
    }
}
