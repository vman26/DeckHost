namespace DeckHost.Core.StreamDeck;

/// <summary>
/// Runs HTML-based Stream Deck plugins: their CodePath is a web page that the Stream Deck app
/// loads in a hidden browser. The app layer provides this (WebView2 on Windows), because Core has
/// no UI or browser of its own.
/// </summary>
public interface IHtmlPluginHost
{
    /// <summary>
    /// Loads <paramref name="page"/> in a hidden browser and runs <paramref name="startScript"/> once
    /// it has loaded (it calls the page's connectElgatoStreamDeckSocket). Disposing the result closes it.
    /// Called from any thread; implementations marshal to their UI thread.
    /// </summary>
    IDisposable Start(string pluginUuid, Uri page, string startScript, Action<string, string> log);
}
