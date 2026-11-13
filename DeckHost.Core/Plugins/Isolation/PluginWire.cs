using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Plugins.Isolation;

/// <summary>
/// The messages between DeckHost and a plugin running in its own process (docs ADR 0007): one JSON
/// object per line over a loopback socket. <see cref="Message.Type"/> says which fields are used.
/// </summary>
public sealed record Message(string Type)
{
    // ---- both ways
    public int? Id { get; init; }

    // ---- plugin → host
    public PluginDescriptor? Plugin { get; init; }
    public IReadOnlyList<DrawCommand>? Commands { get; init; }
    public string? Key { get; init; }
    public WireValue? Value { get; init; }
    public string? Call { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }
    public string? Level { get; init; }
    public string? Text { get; init; }

    // ---- host → plugin
    public string? Token { get; init; }
    public string? ActionId { get; init; }
    public WireContext? Context { get; init; }
    public IReadOnlyList<WireVariable>? Variables { get; init; }

    public const string Hello = "hello";
    public const string Ready = "ready";
    public const string Execute = "execute";
    public const string Render = "render";
    public const string Drawn = "drawn";
    public const string Appear = "appear";
    public const string Disappear = "disappear";
    public const string SettingsPage = "settingsPage";
    public const string Answer = "answer";
    public const string Publish = "publish";
    public const string HostCall = "host";
    public const string Log = "log";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Shutdown = "shutdown";
    public const string Failed = "failed";
}

/// <summary>What a plugin and its actions are, sent once when the process has loaded it.</summary>
public sealed record PluginDescriptor(string Id, string Name, string Version, string? Author, string? Description, IReadOnlyList<ActionDescriptor> Actions);

/// <summary>Everything about an action DeckHost reads without running it.</summary>
public sealed record ActionDescriptor(
    string Id,
    string Name,
    InputCapabilities RequiredInputs,
    OutputCapabilities RequiredOutputs,
    string? Description,
    IReadOnlyList<ActionSettingDefinition> Settings,
    bool ShowInPalette,
    string? ListIcon,
    bool CanBeMultiActionStep,
    double? RefreshIntervalMs,
    bool ShowsAppearanceOnly,
    bool HasSettingsPage);

/// <summary>An <see cref="ActionContext"/> or the facts of an <see cref="Contracts.Interfaces.IRenderContext"/>.</summary>
public sealed record WireContext(string DeviceId, string ControlId, string ProfileId, ActionSettings Settings)
{
    public InputCapabilities EventType { get; init; }
    public int Value { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsCompact { get; init; }
    public TextStyle? TextStyle { get; init; }
}

/// <summary>A variable's value on the wire: strings, numbers, booleans and times (others don't cross).</summary>
public sealed record WireValue(string Kind, string? Text)
{
    public static WireValue? From(object? value) => value switch
    {
        null => new WireValue("null", null),
        string text => new WireValue("s", text),
        bool flag => new WireValue("b", flag ? "1" : "0"),
        int number => new WireValue("i", number.ToString(CultureInfo.InvariantCulture)),
        long number => new WireValue("l", number.ToString(CultureInfo.InvariantCulture)),
        double number => new WireValue("d", number.ToString("R", CultureInfo.InvariantCulture)),
        float number => new WireValue("f", number.ToString("R", CultureInfo.InvariantCulture)),
        DateTimeOffset time => new WireValue("t", time.ToString("O", CultureInfo.InvariantCulture)),
        _ => null,
    };

    public object? ToObject() => Kind switch
    {
        "s" => Text,
        "b" => Text == "1",
        "i" => int.Parse(Text!, CultureInfo.InvariantCulture),
        "l" => long.Parse(Text!, CultureInfo.InvariantCulture),
        "d" => double.Parse(Text!, CultureInfo.InvariantCulture),
        "f" => float.Parse(Text!, CultureInfo.InvariantCulture),
        "t" => DateTimeOffset.Parse(Text!, CultureInfo.InvariantCulture),
        _ => null,
    };
}

public sealed record WireVariable(string Key, WireValue Value);

/// <summary>One drawing call of a plugin's Render, replayed by DeckHost on the real surface.</summary>
public sealed record DrawCommand(string Op)
{
    public string? Text { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public double R { get; init; }
    public float Size { get; init; }
    public float? DefaultSize { get; init; }
    public uint Color { get; init; }
    public uint Color2 { get; init; }
    public TextVerticalAlignment Vertical { get; init; }
    public bool Flag { get; init; }
    public byte[]? Data { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
}

/// <summary>Line framing and the JSON settings both sides use.</summary>
public static class Wire
{
    /// <summary>A message larger than this ends the connection (a plugin gone wrong).</summary>
    public const int MaxMessageBytes = 16 * 1024 * 1024;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode(Message message) => JsonSerializer.Serialize(message, Options);

    public static Message? Decode(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<Message>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads one line (one message); null at the end of the stream.</summary>
    public static async Task<Message?> ReadAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (line.Length > MaxMessageBytes)
            {
                throw new InvalidDataException("A message was too large.");
            }

            if (line.Length > 0 && Decode(line) is { } message)
            {
                return message;
            }
        }
    }

    public static StreamWriter Writer(Stream stream) => new(stream, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
}
