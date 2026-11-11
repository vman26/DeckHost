using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeckHost.App.Platform;

/// <summary>A key combination such as <c>Ctrl+Shift+M</c>, as Windows virtual-key codes.</summary>
internal sealed record Hotkey(IReadOnlyList<ushort> Modifiers, ushort Key)
{
    private static readonly Dictionary<string, ushort> ModifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11,
        ["alt"] = 0x12,
        ["shift"] = 0x10,
        ["win"] = 0x5B, ["windows"] = 0x5B, ["meta"] = 0x5B, ["super"] = 0x5B,
    };

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["space"] = 0x20, ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["ins"] = 0x2D,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pgup"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["pause"] = 0x13, ["capslock"] = 0x14,
        ["playpause"] = 0xB3, ["nexttrack"] = 0xB0, ["prevtrack"] = 0xB1, ["stop"] = 0xB2,
        ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["mute"] = 0xAD,
        ["plus"] = 0xBB, ["minus"] = 0xBD, ["comma"] = 0xBC, ["period"] = 0xBE,
    };

    public static bool TryParse(string text, out Hotkey hotkey, out string error)
    {
        hotkey = null!;
        error = string.Empty;

        var modifiers = new List<ushort>();
        ushort? key = null;
        foreach (var rawPart in (text ?? string.Empty).Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ModifierKeys.TryGetValue(rawPart, out var modifier))
            {
                if (!modifiers.Contains(modifier))
                {
                    modifiers.Add(modifier);
                }

                continue;
            }

            if (key is not null)
            {
                error = $"Only one non-modifier key is allowed ('{rawPart}').";
                return false;
            }

            if (!TryParseKey(rawPart, out var parsed))
            {
                error = $"Unknown key '{rawPart}'.";
                return false;
            }

            key = parsed;
        }

        if (key is null)
        {
            error = "Enter a key, for example Ctrl+Shift+M.";
            return false;
        }

        hotkey = new Hotkey(modifiers, key.Value);
        return true;
    }

    private static bool TryParseKey(string part, out ushort key)
    {
        key = 0;
        if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
        {
            key = char.ToUpperInvariant(part[0]);
            return true;
        }

        if (part.Length is 2 or 3 &&
            (part[0] == 'F' || part[0] == 'f') &&
            int.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var function) &&
            function is >= 1 and <= 24)
        {
            key = (ushort)(0x70 + function - 1);
            return true;
        }

        return NamedKeys.TryGetValue(part, out key);
    }
}
