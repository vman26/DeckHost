using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.IO;

namespace DeckHost.App.Platform;

/// <summary>
/// NLog configuration, used through Microsoft.Extensions.Logging (<c>ILogger&lt;T&gt;</c> from DI).
/// Everything the runtime logs (every key press, image push, plugin start and error) goes to
/// <c>%AppData%/DeckHost/logs/deckhost-yyyy-MM-dd.log</c>, kept for 14 days. Stream Deck plugin
/// processes write their own output to <c>logs/plugins/&lt;uuid&gt;.log</c>.
/// </summary>
internal static class Logging
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "DeckHost",
        "logs");

    public static LoggingConfiguration CreateConfiguration()
    {
        Directory.CreateDirectory(LogDirectory);
        var configuration = new LoggingConfiguration();
        var file = new FileTarget("file")
        {
            FileName = Path.Combine(LogDirectory, "deckhost-${shortdate}.log"),
            Layout = "${date:format=HH\\:mm\\:ss.fff} ${level:uppercase=true:padding=-5} ${logger:shortName=true:padding=-20} ${message}${onexception:${newline}${exception:format=tostring}}",
            MaxArchiveDays = 14,
            KeepFileOpen = true,
            AutoFlush = true,
        };
        configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, file);
        return configuration;
    }
}
