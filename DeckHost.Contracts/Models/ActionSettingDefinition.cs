namespace DeckHost.Contracts.Models;

public enum ActionSettingKind
{
    Text,
    /// <summary>Hidden while typing; still stored in plain text in the profiles file.</summary>
    Secret,
    Number,
    Toggle,
    Choice,
    FilePath,

    /// <summary>A key combination such as "Ctrl+Shift+M"; the editor can record it from a key press.</summary>
    Hotkey,
}

public sealed record ActionSettingOption(string Value, string Label);

/// <summary>
/// Describes one setting an action needs (an app to launch, a scene name, ...). The editor in
/// the Customize window is generated from these definitions.
/// </summary>
public sealed record ActionSettingDefinition(
    string Key,
    string Label,
    ActionSettingKind Kind = ActionSettingKind.Text,
    string DefaultValue = "",
    string? Description = null,
    IReadOnlyList<ActionSettingOption>? Options = null,
    string? Placeholder = null);
