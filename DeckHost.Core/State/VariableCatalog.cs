using System.Globalization;
using DeckHost.Contracts;
using DeckHost.Core.Workflows;

namespace DeckHost.Core.State;

/// <summary>Where a variable lives.</summary>
public enum VariableScope
{
    /// <summary>Published by DeckHost itself (<c>system.cpu</c>, the app in front).</summary>
    System,

    /// <summary>A user variable every workflow shares.</summary>
    Global,

    /// <summary>A user variable only one profile's workflows share.</summary>
    Profile,

    /// <summary>A user variable that belongs to one control's workflow.</summary>
    Control,
}

/// <summary>A variable as the Variables page and the pickers list it.</summary>
/// <param name="Key">The variable store key (<c>vars.name</c>).</param>
/// <param name="Name">What the user sees: the name, with the profile or control for those scopes.</param>
/// <param name="Scope">Where the variable lives.</param>
/// <param name="Type">What the value looks like (see <see cref="VariableTypes"/>).</param>
/// <param name="Value">The current value; empty when nothing has been published yet.</param>
public sealed record VariableInfo(string Key, string Name, VariableScope Scope, string Type, string Value)
{
    /// <summary>Only user variables can be created, changed and removed.</summary>
    public bool IsUser => Scope != VariableScope.System;
}

/// <summary>The kinds of value a variable can hold, worked out from the text.</summary>
public static class VariableTypes
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Boolean = "True/false";
    public const string Color = "Color";
    public const string Date = "Date";
    public const string Empty = "Empty";

    /// <summary>The types a person can pick when creating a variable.</summary>
    public static IReadOnlyList<string> Choices { get; } = [Text, Number, Boolean, Color, Date];

    /// <summary>What <paramref name="value"/> looks like.</summary>
    public static string Of(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Empty;
        }

        var text = value.Trim();
        if (bool.TryParse(text, out _))
        {
            return Boolean;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return Number;
        }

        if (IsColor(text))
        {
            return Color;
        }

        return IsDate(text) ? Date : Text;
    }

    /// <summary>Whether <paramref name="value"/> can be stored as <paramref name="type"/> (an empty value clears, so it always can).</summary>
    public static bool Fits(string type, string? value) =>
        string.IsNullOrEmpty(value) || type switch
        {
            Number => Of(value) == Number,
            Boolean => Of(value) == Boolean,
            Color => Of(value) == Color,
            Date => Of(value) == Date,
            _ => true,
        };

    private static bool IsColor(string text) =>
        text.Length is 7 or 9 && text[0] == '#' && text.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static bool IsDate(string text) =>
        text.Length >= 8 && char.IsDigit(text[0]) &&
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
}

/// <summary>Turns variable store entries into the list the app shows.</summary>
public static class VariableCatalog
{
    /// <summary>The variables DeckHost publishes itself; listed even before they have a value.</summary>
    public static IReadOnlyList<string> SystemKeys { get; } = ["system.cpu", "system.memory", VariableKeys.ForegroundApp];

    /// <summary>Describes one store entry.</summary>
    public static VariableInfo Describe(string key, string? value)
    {
        var text = value ?? string.Empty;
        return new VariableInfo(key, WorkflowScopes.Describe(key), ScopeOf(key), VariableTypes.Of(text), text);
    }

    /// <summary>The scope a key belongs to.</summary>
    public static VariableScope ScopeOf(string key) =>
        key.StartsWith("vars.profile|", StringComparison.Ordinal) ? VariableScope.Profile
        : key.StartsWith("vars.control|", StringComparison.Ordinal) ? VariableScope.Control
        : key.StartsWith("vars.", StringComparison.Ordinal) ? VariableScope.Global
        : VariableScope.System;
}
