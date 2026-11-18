using System.Text.Json;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Profiles;

namespace DeckHost.Benchmark;

/// <summary>
/// DeckHost's data folder for a run, so the measurement doesn't depend on (or change) anyone's own
/// setup. On Linux a fresh HOME (and XDG_CONFIG_HOME); on Windows and macOS, where the data folder
/// ignores the environment, the real one, moved aside first and put back afterwards.
/// </summary>
internal sealed class Sandbox : IDisposable
{
    /// <summary>The device the simulator becomes (SimulatorDevicePlugin: simulator.&lt;model's device id&gt;).</summary>
    public const string DeviceId = "simulator.mirabox.n4pro";

    /// <summary>The simulator model to attach as.</summary>
    public const string ModelKey = "deckhost.device.mirabox/mirabox.n4pro";

    /// <summary>The key whose image changes with each press (the latency is measured on it).</summary>
    public const string CounterKey = "key.2";

    /// <summary>The key that flips between the two pages.</summary>
    public const string PageKey = "key.9";

    /// <summary>The knob with a counter (drawn on its touch zone).</summary>
    public const string Knob = "encoder.0";

    private readonly string _root;
    private readonly string? _backup;

    private Sandbox(string root, string dataDirectory, IReadOnlyDictionary<string, string> environment, string? backup)
    {
        _root = root;
        DataDirectory = dataDirectory;
        Environment = environment;
        _backup = backup;
    }

    /// <summary>The sandbox's own folder (on Windows only temporary files; the data folder is the real one).</summary>
    public string Root => _root;

    /// <summary>DeckHost's data folder (settings.json, profiles.json, simulator.json, logs).</summary>
    public string DataDirectory { get; }

    /// <summary>Variables for DeckHost's process.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <exception cref="InvalidOperationException">Windows, a DeckHost data folder exists and <paramref name="replaceData"/> is off.</exception>
    public static Sandbox Create(bool replaceData)
    {
        // Short on Linux and macOS: DeckHost's single-instance pipe lives in TMPDIR, and a socket path
        // may be at most 104 characters on macOS (whose own TMPDIR is already about 50).
        var root = OperatingSystem.IsWindows()
            ? Directory.CreateTempSubdirectory("deckhost-benchmark").FullName
            : Directory.CreateDirectory(Path.Combine("/tmp", "dhb-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            // Windows' known folders ignore the environment, and so does macOS's Application Support
            // (.NET asks Foundation, which doesn't read HOME): the real folder, moved aside.
            var data = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "DeckHost");
            string? backup = null;
            if (Directory.Exists(data))
            {
                if (!replaceData)
                {
                    throw new InvalidOperationException($"{data} exists. Run with --replace-data to move it aside during the benchmark (it is put back afterwards).");
                }

                backup = data + ".benchmark-backup";
                Directory.Move(data, backup);
            }

            // macOS: the single-instance pipe still goes to a short TMPDIR of our own.
            var environment = OperatingSystem.IsMacOS()
                ? new Dictionary<string, string> { ["TMPDIR"] = Path.Combine(root, "tmp") }
                : new Dictionary<string, string>();
            return new Sandbox(root, data, environment, backup);
        }

        var home = Path.Combine(root, "home");
        var config = Path.Combine(home, ".config");
        Directory.CreateDirectory(config);
        return new Sandbox(root, Path.Combine(config, "DeckHost"), new Dictionary<string, string>
        {
            ["HOME"] = home,
            ["XDG_CONFIG_HOME"] = config,
            ["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share"),
            ["XDG_CACHE_HOME"] = Path.Combine(home, ".cache"),
            ["TMPDIR"] = Path.Combine(root, "tmp"),
        }, null);
    }

    /// <summary>
    /// Settings for a measurement: the simulator on; update checks, plugin updates and the welcome tour
    /// off (they would measure the network and the tour); <paramref name="startInTray"/> for the tray run.
    /// </summary>
    public void WriteSettings(bool startInTray)
    {
        Directory.CreateDirectory(DataDirectory);
        if (Environment.TryGetValue("TMPDIR", out var temp))
        {
            Directory.CreateDirectory(temp);
        }

        var settings = new Dictionary<string, object>
        {
            ["CheckForUpdates"] = false,
            ["InstallUpdatesAutomatically"] = false,
            ["AutoUpdatePlugins"] = false,
            ["DeviceSimulator"] = true,
            ["WelcomeShown"] = true,
            ["StartMinimized"] = startInTray,
        };
        File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), JsonSerializer.Serialize(settings));
    }

    /// <summary>
    /// A profile like a real desk for the simulated N4 Pro: a clock, CPU and memory readouts, a counter
    /// on a key and on a knob, and a key that flips between two pages.
    /// </summary>
    public void WriteProfiles()
    {
        const string ProfileId = "benchmark";
        var page1 = new DeviceProfile(ProfileId, "Benchmark", DeviceId, Bindings(PageMode: "next"));
        var page2 = new DeviceProfile("benchmark-page-2", "Page 2", DeviceId, Bindings(PageMode: "previous"))
        {
            ParentId = ProfileId,
            Kind = ProfileKinds.Page,
        };
        new ProfileStore(Path.Combine(DataDirectory, "profiles.json"))
            .Save(new ProfileDocument(ProfileDocument.CurrentVersion, ProfileId, [page1, page2]));
    }

    public void Dispose()
    {
        if (_backup is not null)
        {
            if (Directory.Exists(DataDirectory))
            {
                Directory.Delete(DataDirectory, recursive: true);
            }

            Directory.Move(_backup, DataDirectory);
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A log still open by a process that hasn't quite ended; the temp folder is cleaned up later.
        }
    }

    private static List<ControlBinding> Bindings(string PageMode) =>
    [
        Bind("key.0", Builtin, "builtin.clock", "Clock"),
        Bind("key.1", SystemPlugin, "system.info", "System Info", ("metric", "cpu")),
        Bind(CounterKey, Builtin, "builtin.counter", "Counter"),
        Bind("key.3", SystemPlugin, "system.info", "System Info", ("metric", "memory")),
        Bind(PageKey, Builtin, "builtin.page", "Pages", ("mode", PageMode)),
        Bind(Knob, Builtin, "builtin.counter", "Counter"),
    ];

    private const string Builtin = "deckhost.plugin.builtin";
    private const string SystemPlugin = "deckhost.plugin.system";

    private static ControlBinding Bind(string control, string plugin, string action, string name, params (string Key, string Value)[] settings) => new(
        control,
        ProfileActionKind.Single,
        new ActionReference(plugin, plugin, action, name, InputCapabilities.None, OutputCapabilities.None,
            settings.Length == 0 ? null : new ActionSettings(settings.Select(static setting => KeyValuePair.Create(setting.Key, setting.Value)))),
        new ControlAppearance(),
        []);
}
