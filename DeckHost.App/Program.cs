using Avalonia;
using System;
using System.Threading;

namespace DeckHost.App;

sealed class Program
{
    private const string InstanceMutexName = @"Local\DeckHost.SingleInstance";
    private const string ActivateEventName = @"Local\DeckHost.Activate";

    /// <summary>Set when another launch asks this instance to show its window.</summary>
    internal static EventWaitHandle? ActivateRequested { get; private set; }

    /// <summary>True when Windows started DeckHost at sign-in (start minimized if enabled).</summary>
    internal static bool StartedAtLogin { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Only one DeckHost may own the device: a second instance would run every action twice
        // (both read the same key presses, and each starts its own plugin processes).
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
                activate.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            return 0;
        }

        StartedAtLogin = Array.Exists(args, static arg => string.Equals(arg, Platform.AppSettings.StartupArgument, StringComparison.OrdinalIgnoreCase));
        using var activateRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        ActivateRequested = activateRequested;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
