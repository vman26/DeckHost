using Avalonia;
using System;
using System.IO;
using System.IO.Pipes;
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
        // "DeckHost --check-native": is the device support installed correctly? No window, no device changes.
        if (Array.Exists(args, static arg => string.Equals(arg, "--check-native", StringComparison.OrdinalIgnoreCase)))
        {
            // DeckHost is a GUI program on Windows: write to the terminal it was started from.
            if (OperatingSystem.IsWindows())
            {
                AttachConsole(-1);
            }

            var (ok, report) = Device.MiraBox.MiraBoxDevicePlugin.CheckNativeBridge();
            Console.WriteLine(report);
            return ok ? 0 : 1;
        }

        // Only one DeckHost may own the device: a second instance would run every action twice
        // (both read the same key presses, and each starts its own plugin processes).
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalFirstInstance();
            return 0;
        }

        // A crash leaves a report that the next start offers to send (nothing is sent by itself).
        Platform.CrashReporter.Install();

        StartedAtLogin = Array.Exists(args, static arg => string.Equals(arg, Platform.AppSettings.StartupArgument, StringComparison.OrdinalIgnoreCase));

        // Windows: a named event. Elsewhere named events don't exist; a named pipe (a Unix socket) sets a local one.
        using var activateRequested = OperatingSystem.IsWindows()
            ? new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName)
            : new EventWaitHandle(false, EventResetMode.AutoReset);
        ActivateRequested = activateRequested;
        if (!OperatingSystem.IsWindows())
        {
            ListenOnActivationPipe(activateRequested);
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    /// <summary>Per user, so two people signed in at once each have their own DeckHost.</summary>
    private static string ActivationPipeName => $"DeckHost.Activate.{Environment.UserName}";

    /// <summary>Asks the running DeckHost to show its window.</summary>
    private static void SignalFirstInstance()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
                activate.Set();
                return;
            }

            using var pipe = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
            pipe.Connect(TimeSpan.FromSeconds(2));
            pipe.WriteByte(1);
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The other instance is starting or exiting; nothing to show.
        }
    }

    private static void ListenOnActivationPipe(EventWaitHandle activate)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(ActivationPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    pipe.WaitForConnection();
                    if (pipe.ReadByte() >= 0)
                    {
                        activate.Set();
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
            }
        })
        {
            IsBackground = true,
            Name = "DeckHost activation pipe",
        };
        thread.Start();
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
