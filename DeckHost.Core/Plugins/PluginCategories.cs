namespace DeckHost.Core.Plugins;

/// <summary>
/// The store catalogs don't carry categories, so DeckHost derives one from a plugin's name,
/// description and id. A category given by the catalog or the plugin's manifest always wins.
/// </summary>
public static class PluginCategories
{
    public const string Other = "Other";

    // First match wins, so the more specific groups come first.
    private static readonly (string Category, string[] Keywords)[] Rules =
    [
        ("Streaming", ["obs", "streamlabs", "twitch", "youtube", "stream", "kick", "restream", "vmix", "wirecast", "xsplit", "scene", "broadcast", "chat bot", "streamer.bot"]),
        ("Communication", ["discord", "teams", "zoom", "slack", "skype", "telegram", "whatsapp", "mute", "meeting", "webex", "mumble", "teamspeak"]),
        ("Audio", ["audio", "volume", "sound", "voicemeeter", "wave link", "wavelink", "microphone", "mic ", "soundpad", "speaker", "mixer", "goxlr", "eq "]),
        ("Music", ["spotify", "music", "apple music", "deezer", "tidal", "playlist", "song", "winamp", "foobar"]),
        ("Lighting & smart home", ["hue", "light", "lamp", "home assistant", "homeassistant", "smart home", "nanoleaf", "govee", "lifx", "wled", "rgb", "key light", "philips", "tuya", "tasmota", "ikea"]),
        ("Gaming", ["game", "gaming", "steam", "xbox", "playstation", "flight", "simulator", "msfs", "iracing", "elite dangerous", "star citizen", "minecraft", "valorant", "league"]),
        ("Video & cameras", ["camera", "cam link", "camlink", "webcam", "video", "capture", "atem", "blackmagic", "ptz", "4k"]),
        ("Productivity", ["calendar", "mail", "outlook", "notion", "todo", "timer", "clock", "pomodoro", "powerpoint", "keynote", "office", "excel", "word", "presentation", "clipboard", "text", "note", "translate"]),
        ("Developer tools", ["git", "github", "gitlab", "jenkins", "docker", "visual studio", "vscode", "code", "terminal", "ssh", "api", "webhook", "http", "mqtt", "websocket"]),
        ("System", ["system", "cpu", "memory", "ram", "gpu", "disk", "battery", "network", "monitor", "windows", "macos", "linux", "launcher", "launch", "app", "shortcut", "hotkey", "keyboard", "mouse", "window", "power", "shutdown", "process"]),
        ("Social & web", ["twitter", "facebook", "instagram", "reddit", "browser", "chrome", "website", "web", "rss", "weather", "crypto", "stock"]),
    ];

    /// <summary>The given category if any, else one derived from the plugin's text, else <see cref="Other"/>.</summary>
    public static string Resolve(string? category, string name, string? description, string id)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            return category.Trim();
        }

        var text = $" {name} {description} {id.Replace('.', ' ').Replace('-', ' ')} ".ToLowerInvariant();
        foreach (var (candidate, keywords) in Rules)
        {
            if (keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }

        return Other;
    }
}
