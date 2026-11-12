using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models;

/// <summary>
/// The values a user entered for an action's settings (see <see cref="Interfaces.IDeckAction.Settings"/>).
/// Immutable, compares by value and serializes as a plain JSON object.
/// </summary>
[JsonConverter(typeof(ActionSettingsJsonConverter))]
public sealed class ActionSettings : IReadOnlyDictionary<string, string>, IEquatable<ActionSettings>
{
    private readonly SortedDictionary<string, string> _values;

    public static ActionSettings Empty { get; } = new([]);

    public ActionSettings(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrEmpty(key) && value is not null)
            {
                _values[key] = value;
            }
        }
    }

    public int Count => _values.Count;

    public IEnumerable<string> Keys => _values.Keys;

    public IEnumerable<string> Values => _values.Values;

    public string this[string key] => _values[key];

    /// <summary>The value, or <paramref name="fallback"/> when it is missing or blank.</summary>
    public string Get(string key, string fallback = "") =>
        _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>A number with decimals (stored with a '.'), for Number settings with a fractional step.</summary>
    public double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;

    public bool GetBool(string key, bool fallback = false) =>
        bool.TryParse(Get(key), out var value) ? value : fallback;

    public ActionSettings With(string key, string? value)
    {
        var copy = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        if (value is null)
        {
            copy.Remove(key);
        }
        else
        {
            copy[key] = value;
        }

        return new ActionSettings(copy);
    }

    /// <summary>
    /// The settings as the action sees them for one input (<see cref="ControlInputs"/>): each
    /// <see cref="ActionSettingDefinition.PerInput"/> setting gets its value for that input under
    /// its plain key. Order: the value stored for the input, the definition's default for the
    /// input, then the plain value as stored (and the action's own default).
    /// </summary>
    public ActionSettings ForInput(IEnumerable<ActionSettingDefinition> definitions, string? input)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (string.IsNullOrEmpty(input))
        {
            return this;
        }

        Dictionary<string, string>? copy = null;
        foreach (var definition in definitions)
        {
            if (!definition.PerInput)
            {
                continue;
            }

            string? value = null;
            if (_values.TryGetValue(ControlInputs.SettingKey(definition.Key, input), out var stored) && !string.IsNullOrWhiteSpace(stored))
            {
                value = stored;
            }
            else if (definition.InputDefaults is { } defaults && defaults.TryGetValue(input, out var inputDefault))
            {
                value = inputDefault;
            }

            if (value is not null)
            {
                copy ??= new Dictionary<string, string>(_values, StringComparer.Ordinal);
                copy[definition.Key] = value;
            }
        }

        return copy is null ? this : new ActionSettings(copy);
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => _values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ActionSettings? other) =>
        other is not null && (ReferenceEquals(this, other) || _values.SequenceEqual(other._values));

    public override bool Equals(object? obj) => Equals(obj as ActionSettings);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (key, value) in _values)
        {
            hash.Add(key);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ActionSettings? left, ActionSettings? right) => Equals(left, right);

    public static bool operator !=(ActionSettings? left, ActionSettings? right) => !Equals(left, right);

    private sealed class ActionSettingsJsonConverter : JsonConverter<ActionSettings>
    {
        public override ActionSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);
            return values is null || values.Count == 0 ? Empty : new ActionSettings(values);
        }

        public override void Write(Utf8JsonWriter writer, ActionSettings value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, settingValue) in value)
            {
                writer.WriteString(key, settingValue);
            }

            writer.WriteEndObject();
        }
    }
}
