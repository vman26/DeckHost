using Avalonia;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
        // Velopack first: during install, update and uninstall it starts DeckHost with its own arguments,
        // runs the hook and ends the process here. Uninstalling removes the start-at-sign-in entry; only
        // Windows has an uninstaller that runs hooks (elsewhere the app is just removed).
        var velopack = Velopack.VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            velopack = velopack.OnBeforeUninstallFastCallback(_ => Platform.AppSettings.SetStartAtLogin(false));
        }

        velopack.Run();

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

        // "DeckHost --plugin-host <port> <token> <folder>": a DeckHost plugin running in its own process.
        // No window, no single-instance check: it connects back to DeckHost and ends with it.
        if (args.Length > 0 && string.Equals(args[0], "--plugin-host", StringComparison.OrdinalIgnoreCase))
        {
            return Core.Plugins.Isolation.PluginProcessServer.Run(args[1..]);
        }

        // "DeckHost --simulator [model]": the device simulator, a window DeckHost sees as a connected
        // device. Its own process (several can run), no single-instance check.
        if (args.Length > 0 && string.Equals(args[0], "--simulator", StringComparison.OrdinalIgnoreCase))
        {
            Simulator.SimulatorApp.StartModel = args.Length > 1 ? args[1] : null;
            return AppBuilder.Configure<Simulator.SimulatorApp>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }

        // "DeckHost --check-plugin <package.zip|folder>": checks a DeckHost plugin for its author and
        // loads it in a scratch folder (nothing is installed). Exit code 1 on errors.
        var check = Array.FindIndex(args, static arg => string.Equals(arg, "--check-plugin", StringComparison.OrdinalIgnoreCase));
        if (check >= 0)
        {
            if (OperatingSystem.IsWindows())
            {
                AttachConsole(-1);
            }

            if (check + 1 >= args.Length)
            {
                Console.WriteLine("Usage: DeckHost --check-plugin <package.zip or folder>");
                return 2;
            }

            return CheckPlugin(args[check + 1]);
        }

        // "DeckHost --after-exit <pid>": started by the previous DeckHost (Restart normally); waits for it to end first,
        // so the single-instance check below doesn't take this one for a second DeckHost.
        var afterExit = Array.FindIndex(args, static arg => string.Equals(arg, "--after-exit", StringComparison.OrdinalIgnoreCase));
        if (afterExit >= 0 && afterExit + 1 < args.Length && int.TryParse(args[afterExit + 1], out var previousProcess))
        {
            WaitForExit(previousProcess);
        }

        // After the move from the older Windows setup: remove the old install, before the mutex below
        // (its uninstaller won't run while a DeckHost holds it).
        Platform.UpdateInstaller.RemoveOldInstall(Platform.MigrationNote.Peek());

        // Only one DeckHost may own the device: a second instance would run every action twice
        // (both read the same key presses, and each starts its own plugin processes).
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalFirstInstance();
            return 0;
        }

        // After the move from the older Windows setup (whose uninstaller removed it), or when the
        // install moved: start at sign-in points at this DeckHost again.
        Platform.AppSettings.RepairStartAtLogin(Platform.MigrationNote.Take());

        // A crash leaves a report that the next start offers to send (nothing is sent by itself).
        Platform.CrashReporter.Install();

        // After crashes at start DeckHost comes up in safe mode (no web views, no extra plugins) instead of crashing again.
        var guard = new Platform.StartupGuard(Platform.CrashReporter.DataDirectory);
        Platform.CrashReporter.Guard = guard;
        var decision = guard.BeginRun(forceSafeMode: Array.Exists(args, static arg => string.Equals(arg, Platform.SafeMode.Argument, StringComparison.OrdinalIgnoreCase)));
        Platform.SafeMode.Set(decision.SafeMode, decision.Reason);

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

    /// <summary>Waits (at most half a minute) for another DeckHost process to end.</summary>
    private static void WaitForExit(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // It is gone already.
        }
        catch (InvalidOperationException)
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    /// <summary>Per user, so two people signed in at once each have their own DeckHost.</summary>
    private static string ActivationPipeName => ActivationPipeNameFor(Path.GetTempPath(), Environment.UserName);

    /// <summary>The longest socket path used: macOS allows 103 characters, Linux 107.</summary>
    internal const int MaxSocketPath = 100;

    /// <summary>
    /// The activation pipe's name. On Linux and macOS .NET makes a pipe a Unix socket at
    /// <c>&lt;temp&gt;/CoreFxPipe_&lt;name&gt;</c>, and a socket path has a length limit: with a long temporary folder the
    /// name is an absolute path in /tmp instead, which .NET uses as it is (both sides compute the same).
    /// </summary>
    internal static string ActivationPipeNameFor(string tempPath, string user)
    {
        var name = $"DeckHost.Activate.{user}";
        return Path.Combine(tempPath, "CoreFxPipe_" + name).Length <= MaxSocketPath ? name : "/tmp/" + name;
    }

    /// <summary>Asks the running DeckHost to show its window.</summary>
    /// <summary>Checks a plugin package and trial-loads it into a scratch catalog; prints the result.</summary>
    private static int CheckPlugin(string package)
    {
        var problems = Core.Plugins.PluginDiagnostics.Check(Path.GetFullPath(package));
        foreach (var problem in problems)
        {
            Console.WriteLine(problem);
        }

        if (problems.Any(static problem => problem.IsError))
        {
            return 1;
        }

        var root = Path.Combine(Path.GetTempPath(), "deckhost-check-plugin-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var catalog = new Core.Plugins.PluginCatalog(root, [], [], []);
            var source = Directory.Exists(package) ? ZipFolder(package, root) : package;
            var entry = catalog.Install(source);
            if (entry.Error is not null)
            {
                Console.WriteLine($"error: {entry.Error}");
                return 1;
            }

            Console.WriteLine($"ok: {entry.Name} {entry.Version} loads with {entry.ActionCount} action(s). {Core.Plugins.PluginPermissions.Describe(entry.Permissions)}");
            foreach (var action in catalog.EnabledApplicationPlugins.SelectMany(static plugin => plugin.Actions))
            {
                Console.WriteLine($"  {action.Id}: {action.Name}");
            }

            return 0;
        }
        catch (Core.Plugins.PluginException ex)
        {
            Console.WriteLine($"error: {ex.Message}");
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string ZipFolder(string folder, string scratch)
    {
        Directory.CreateDirectory(scratch);
        var zip = Path.Combine(scratch, "package.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);
        return zip;
    }

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
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or TimeoutException or IOException or UnauthorizedAccessException
            or ArgumentException or PlatformNotSupportedException)
        {
            // The other instance is starting or exiting, or has no pipe; nothing to show.
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
                catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
                {
                    // No pipe here (a socket path the system refuses, say): DeckHost runs on, only a second
                    // launch can't bring this window forward. An exception left to this thread ended DeckHost.
                    System.Diagnostics.Trace.WriteLine($"DeckHost: no activation pipe ({ex.Message}).");
                    return;
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
