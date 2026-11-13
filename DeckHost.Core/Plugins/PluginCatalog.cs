using DeckHost.Contracts.Interfaces;
using DeckHost.Core.StreamDeck;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace DeckHost.Core.Plugins;

public enum PluginKind
{
    Device,
    Application,
}

/// <summary>What the Plugin Manager shows for one plugin.</summary>
/// <param name="Source">Plugin format: "DeckHost" or "Stream Deck" (Elgato SDK plugins).</param>
/// <param name="Origin">Where it was installed from: a store source, "File" or "Built into DeckHost".</param>
public sealed record PluginEntry(
    string Id,
    string Name,
    string Version,
    string? Author,
    string? Description,
    PluginKind Kind,
    bool IsSystem,
    bool IsEnabled,
    bool CanDisable,
    bool CanUninstall,
    int? ActionCount,
    string? Error,
    string Source = PluginCatalog.DeckHostSource,
    string? Origin = null,
    string? Category = null,
    string? InstallDirectory = null,
    string? IconPath = null);

/// <summary>
/// Keeps track of all plugins:
/// <list type="bullet">
/// <item>system plugins compiled into DeckHost (can be disabled unless required, never uninstalled);</item>
/// <item>DeckHost plugin packages (.zip with plugin.json) in <c>plugins/functional/&lt;id&gt;</c>;</item>
/// <item>Elgato Stream Deck SDK plugins (.streamDeckPlugin with a &lt;uuid&gt;.sdPlugin folder)
/// in <c>plugins/streamdeck</c>, run by <see cref="StreamDeckRuntime"/>.</item>
/// </list>
/// Thread-safe: install/uninstall can run on a background thread (Changed is raised on that thread).
/// </summary>
public sealed class PluginCatalog : IDisposable
{
    public const string DeckHostSource = "DeckHost";
    public const string StreamDeckSource = "Stream Deck";

    private const string StateFileName = "state.json";
    private const string StagingDirectoryName = ".staging";

    private readonly string _rootDirectory;
    private readonly IReadOnlyList<IDevicePlugin> _devicePlugins;
    private readonly StreamDeckRuntime? _streamDeck;
    private readonly List<Slot> _slots = [];
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingRemoval = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _origins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();
    private IProgress<string>? _progress;
    private bool _changed;

    public PluginCatalog(
        string rootDirectory,
        IEnumerable<IDevicePlugin> devicePlugins,
        IEnumerable<IApplicationPlugin> systemPlugins,
        IEnumerable<string> requiredPluginIds,
        StreamDeckRuntime? streamDeckRuntime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
        _devicePlugins = devicePlugins.ToList();
        _streamDeck = streamDeckRuntime;

        var required = requiredPluginIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in systemPlugins)
        {
            _slots.Add(new Slot(plugin.Id, SlotKind.System, IsRequired: required.Contains(plugin.Id), Directory: null) { Instance = plugin });
        }
    }

    public event EventHandler? Changed;

    public static string DefaultRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "DeckHost",
        "plugins");

    public string InstallDirectory => Path.Combine(_rootDirectory, "functional");

    public string StreamDeckDirectory => Path.Combine(_rootDirectory, "streamdeck");

    public bool SupportsStreamDeckPlugins => _streamDeck is not null;

    // ---------------------------------------------------------------- thread-safe public surface
    // Installing and uninstalling can take seconds (downloads, unpacking, stopping processes), so
    // callers run them on a background thread. Every public member takes the same (re-entrant) lock,
    // and Changed is raised after it is released, on the calling thread.

    public IReadOnlyList<IApplicationPlugin> EnabledApplicationPlugins
    {
        get
        {
            lock (_sync)
            {
                return EnabledApplicationPluginsCore;
            }
        }
    }

    public IReadOnlyList<PluginEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return EntriesCore;
            }
        }
    }

    public bool IsInstalled(string id)
    {
        lock (_sync)
        {
            return IsInstalledCore(id);
        }
    }

    /// <summary>Finishes pending removals and loads (and starts) the enabled installed plugins.</summary>
    public void Load() => Run(null, LoadCore);

    /// <summary>
    /// Installs (or upgrades) a DeckHost package (.zip with plugin.json) or a Stream Deck plugin
    /// (.streamDeckPlugin / .zip with a &lt;uuid&gt;.sdPlugin folder), checked in a staging folder first.
    /// </summary>
    /// <param name="fallbackId">Plugin id for Stream Deck archives without a .sdPlugin folder.</param>
    /// <param name="origin">Where the package came from (store source or "File"), shown in the Plugin Manager.</param>
    public PluginEntry Install(string packagePath, string? fallbackId = null, IProgress<string>? progress = null, string? origin = null)
    {
        PluginEntry? entry = null;
        Run(progress, () =>
        {
            entry = InstallCore(packagePath, fallbackId);
            _origins[entry.Id] = origin ?? "File";
            SaveState();
            entry = ToEntry(FindSlot(entry.Id));
        });
        return entry!;
    }

    /// <summary>
    /// Records where installed plugins came from when that is unknown (e.g. installed by an older
    /// DeckHost), matching them against the store catalogs by id. Known origins are kept.
    /// </summary>
    public void RecordUnknownOrigins(IEnumerable<(string Id, string Origin)> candidates) => Run(null, () =>
    {
        var updated = false;
        foreach (var (id, origin) in candidates)
        {
            if (_slots.Any(slot => slot.Kind != SlotKind.System && string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase))
                && !_origins.ContainsKey(id))
            {
                _origins[id] = origin;
                updated = true;
            }
        }

        if (updated)
        {
            SaveState();
            OnChanged();
        }
    });

    public void Uninstall(string id, IProgress<string>? progress = null) => Run(progress, () => UninstallCore(id));

    public void SetEnabled(string id, bool enabled, IProgress<string>? progress = null) => Run(progress, () => SetEnabledCore(id, enabled));

    public void Dispose() => Run(null, DisposeCore);

    private void Run(IProgress<string>? progress, Action work)
    {
        bool changed;
        lock (_sync)
        {
            _progress = progress;
            _changed = false;
            try
            {
                work();
            }
            finally
            {
                _progress = null;
                changed = _changed;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnChanged() => _changed = true;

    private void Report(string message) => _progress?.Report(message);

    // ---------------------------------------------------------------- implementation (called under the lock)

    private IReadOnlyList<IApplicationPlugin> EnabledApplicationPluginsCore =>
        [.. _slots.Where(slot => slot.Instance is not null && !_disabled.Contains(slot.Id)).Select(slot => slot.Instance!)];

    private IReadOnlyList<PluginEntry> EntriesCore =>
    [
        .. _devicePlugins.Select(plugin => new PluginEntry(
            plugin.Id,
            plugin.Name,
            plugin.GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            null,
            $"Supports {string.Join(", ", plugin.Devices.Select(device => device.Name).Distinct())}.",
            PluginKind.Device,
            IsSystem: true,
            IsEnabled: true,
            CanDisable: false,
            CanUninstall: false,
            ActionCount: null,
            Error: null)),
        .. _slots.Select(ToEntry),
    ];

    private bool IsInstalledCore(string id) => _slots.Any(slot => string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase));

    private void LoadCore()
    {
        LoadState();

        foreach (var id in _pendingRemoval.ToList())
        {
            if (TryDeleteDirectory(Path.Combine(InstallDirectory, id)) && TryDeleteDirectory(Path.Combine(StreamDeckDirectory, id + ".sdPlugin")))
            {
                _pendingRemoval.Remove(id);
            }
        }

        if (Directory.Exists(InstallDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(InstallDirectory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(directory);
                if (_pendingRemoval.Contains(id) || IsInstalled(id))
                {
                    continue;
                }

                var slot = new Slot(id, SlotKind.Package, IsRequired: false, directory);
                _slots.Add(slot);
                try
                {
                    slot.Manifest = PluginManifest.Parse(File.ReadAllText(Path.Combine(directory, PluginManifest.FileName)));
                    if (!_disabled.Contains(id))
                    {
                        LoadInstance(slot);
                    }
                }
                catch (Exception ex) when (ex is PluginException or IOException or UnauthorizedAccessException)
                {
                    slot.Error = ex.Message;
                }
            }
        }

        if (_streamDeck is not null && Directory.Exists(StreamDeckDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(StreamDeckDirectory, "*.sdPlugin").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var manifest = StreamDeckManifest.Load(directory);
                    if (_pendingRemoval.Contains(manifest.Uuid) || IsInstalled(manifest.Uuid))
                    {
                        continue;
                    }

                    var slot = new Slot(manifest.Uuid, SlotKind.StreamDeck, IsRequired: false, directory) { StreamDeckManifest = manifest };
                    _slots.Add(slot);
                    if (!_disabled.Contains(slot.Id))
                    {
                        StartStreamDeck(slot);
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException)
                {
                    _slots.Add(new Slot(Path.GetFileName(directory), SlotKind.StreamDeck, IsRequired: false, directory) { Error = $"Invalid plugin: {ex.Message}" });
                }
            }
        }

        TryDeleteDirectory(Path.Combine(_rootDirectory, StagingDirectoryName));
        SaveState();
    }

    private PluginEntry InstallCore(string packagePath, string? fallbackId)
    {
        if (!File.Exists(packagePath))
        {
            throw new PluginException($"Package not found: {packagePath}");
        }

        var stagingDirectory = Path.Combine(_rootDirectory, StagingDirectoryName, Guid.NewGuid().ToString("N"));
        try
        {
            Report("Unpacking…");
            ExtractPackage(packagePath, stagingDirectory, depth: 0);

            if (File.Exists(Path.Combine(stagingDirectory, PluginManifest.FileName)))
            {
                return InstallDeckHostPackage(stagingDirectory);
            }

            var sdPluginDirectory = FindStreamDeckFolder(stagingDirectory, fallbackId);
            if (sdPluginDirectory is not null)
            {
                return InstallStreamDeckPlugin(sdPluginDirectory);
            }

            throw new PluginException("This isn't a DeckHost plugin (no plugin.json) or a Stream Deck plugin (no .sdPlugin folder with a manifest.json).");
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private void UninstallCore(string id)
    {
        var slot = FindSlot(id);
        if (slot.Kind == SlotKind.System)
        {
            throw new PluginException($"{slot.DisplayName} is a system plugin and can't be uninstalled. You can disable it instead.");
        }

        Unload(slot);
        _slots.Remove(slot);
        _disabled.Remove(slot.Id);
        _origins.Remove(slot.Id);
        Report("Removing files…");
        if (slot.Directory is not null && !TryDeleteDirectory(slot.Directory))
        {
            _pendingRemoval.Add(slot.Id);
        }

        SaveState();
        OnChanged();
    }

    private void SetEnabledCore(string id, bool enabled)
    {
        var slot = FindSlot(id);
        if (slot.IsRequired && !enabled)
        {
            throw new PluginException($"{slot.DisplayName} is required by the editor and can't be disabled.");
        }

        if (enabled == !_disabled.Contains(slot.Id))
        {
            return;
        }

        if (enabled)
        {
            _disabled.Remove(slot.Id);
            switch (slot.Kind)
            {
                case SlotKind.Package:
                    LoadInstance(slot);
                    break;
                case SlotKind.StreamDeck:
                    StartStreamDeck(slot);
                    break;
            }
        }
        else
        {
            _disabled.Add(slot.Id);
            Unload(slot);
        }

        SaveState();
        OnChanged();
    }

    private void DisposeCore()
    {
        foreach (var slot in _slots)
        {
            Unload(slot);
        }
    }

    // ---------------------------------------------------------------- installing

    private static void ExtractPackage(string packagePath, string target, int depth)
    {
        try
        {
            ZipFile.ExtractToDirectory(packagePath, target);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new PluginException($"Could not unpack the package: {ex.Message}");
        }

        // Some downloads wrap the real package in another archive (a single .streamDeckPlugin or .zip inside).
        var entries = Directory.GetFileSystemEntries(target);
        if (depth == 0 && entries.Length == 1 && File.Exists(entries[0]) &&
            (entries[0].EndsWith(".streamDeckPlugin", StringComparison.OrdinalIgnoreCase) || entries[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            var inner = entries[0] + ".inner";
            File.Move(entries[0], inner);
            ExtractPackage(inner, target, depth + 1);
            File.Delete(inner);
        }
    }

    private static string? FindStreamDeckFolder(string root, string? fallbackId)
    {
        var folder = Directory.EnumerateDirectories(root, "*.sdPlugin", SearchOption.AllDirectories)
            .FirstOrDefault(directory => File.Exists(Path.Combine(directory, "manifest.json")));
        if (folder is not null)
        {
            return folder;
        }

        if (fallbackId is not null && File.Exists(Path.Combine(root, "manifest.json")))
        {
            var renamed = Path.Combine(Path.GetDirectoryName(root)!, fallbackId + ".sdPlugin");
            Directory.Move(root, renamed);
            Directory.CreateDirectory(root);
            return renamed;
        }

        return null;
    }

    private PluginEntry InstallDeckHostPackage(string stagingDirectory)
    {
        var manifest = PluginManifest.Parse(File.ReadAllText(Path.Combine(stagingDirectory, PluginManifest.FileName)));
        EnsureNotSystem(manifest.Id);

        // Trial load: proves the package contains a working plugin before anything is replaced.
        var trial = new Slot(manifest.Id, SlotKind.Package, IsRequired: false, stagingDirectory) { Manifest = manifest };
        LoadInstance(trial);
        Unload(trial);

        var targetDirectory = Path.Combine(InstallDirectory, manifest.Id);
        ReplaceExisting(manifest.Id, targetDirectory);
        Directory.CreateDirectory(InstallDirectory);
        Directory.Move(stagingDirectory, targetDirectory);

        var slot = new Slot(manifest.Id, SlotKind.Package, IsRequired: false, targetDirectory) { Manifest = manifest };
        _slots.Add(slot);
        _disabled.Remove(manifest.Id);
        _pendingRemoval.Remove(manifest.Id);
        LoadInstance(slot);
        SaveState();
        OnChanged();
        return ToEntry(slot);
    }

    private PluginEntry InstallStreamDeckPlugin(string sdPluginDirectory)
    {
        if (_streamDeck is null)
        {
            throw new PluginException("Stream Deck plugins aren't supported in this build.");
        }

        StreamDeckManifest manifest;
        try
        {
            manifest = StreamDeckManifest.Load(sdPluginDirectory);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            throw new PluginException($"The Stream Deck plugin's manifest.json can't be read: {ex.Message}");
        }

        EnsureNotSystem(manifest.Uuid);
        var targetDirectory = Path.Combine(StreamDeckDirectory, manifest.Uuid + ".sdPlugin");
        ReplaceExisting(manifest.Uuid, targetDirectory);
        Directory.CreateDirectory(StreamDeckDirectory);
        Directory.Move(sdPluginDirectory, targetDirectory);

        var slot = new Slot(manifest.Uuid, SlotKind.StreamDeck, IsRequired: false, targetDirectory) { StreamDeckManifest = StreamDeckManifest.Load(targetDirectory) };
        _slots.Add(slot);
        _disabled.Remove(slot.Id);
        _pendingRemoval.Remove(slot.Id);
        StartStreamDeck(slot);
        SaveState();
        OnChanged();
        return ToEntry(slot);
    }

    private void EnsureNotSystem(string id)
    {
        if (_slots.Any(slot => slot.Kind == SlotKind.System && string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase)) ||
            _devicePlugins.Any(plugin => string.Equals(plugin.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PluginException($"'{id}' is a system plugin and is already part of DeckHost.");
        }
    }

    private void ReplaceExisting(string id, string targetDirectory)
    {
        var existing = _slots.FirstOrDefault(slot => string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Unload(existing);
            _slots.Remove(existing);
        }

        if (Directory.Exists(targetDirectory) && !TryDeleteDirectory(targetDirectory))
        {
            throw new PluginException("The previous version's files are in use; restart DeckHost and install again.");
        }
    }

    // ---------------------------------------------------------------- loading

    private void StartStreamDeck(Slot slot)
    {
        if (_streamDeck is null || slot.StreamDeckManifest is not { } manifest)
        {
            return;
        }

        slot.Error = null;
        slot.Instance = _streamDeck.CreatePlugin(manifest);
        try
        {
            Report($"Starting {manifest.Name}…");
            _streamDeck.Start(manifest);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            // Keep the actions visible so existing mappings stay resolved; they just do nothing.
            slot.Error = ex.Message;
        }
    }

    private static void LoadInstance(Slot slot)
    {
        if (slot.Instance is not null || slot.Directory is null || slot.Manifest is null)
        {
            return;
        }

        slot.Error = null;
        var context = new PluginLoadContext(slot.Directory, slot.Id);
        try
        {
            var assemblyPath = Path.Combine(slot.Directory, slot.Manifest.Assembly);
            if (!File.Exists(assemblyPath))
            {
                throw new PluginException($"The package does not contain {slot.Manifest.Assembly}.");
            }

            var assembly = context.LoadPluginAssembly(slot.Manifest.Assembly);
            var pluginType = GetLoadableTypes(assembly)
                .FirstOrDefault(type => type is { IsAbstract: false, IsInterface: false } &&
                                        typeof(IApplicationPlugin).IsAssignableFrom(type) &&
                                        type.GetConstructor(Type.EmptyTypes) is not null)
                ?? throw new PluginException($"{slot.Manifest.Assembly} contains no public IApplicationPlugin with a parameterless constructor (built against this DeckHost.Contracts?).");

            var instance = (IApplicationPlugin)Activator.CreateInstance(pluginType)!;
            if (!string.Equals(instance.Id, slot.Manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                (instance as IDisposable)?.Dispose();
                throw new PluginException($"The plugin reports id '{instance.Id}' but its manifest says '{slot.Manifest.Id}'.");
            }

            slot.Instance = instance;
            slot.Context = context;
        }
        catch (Exception ex)
        {
            context.Unload();
            slot.Error = ex is PluginException ? ex.Message : $"Failed to load: {ex.GetBaseException().Message}";
            throw ex as PluginException ?? new PluginException(slot.Error, ex);
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null)!;
        }
    }

    private void Unload(Slot slot)
    {
        switch (slot.Kind)
        {
            case SlotKind.System:
                return;
            case SlotKind.StreamDeck:
                Report($"Stopping {slot.DisplayName}…");
                _streamDeck?.Stop(slot.Id);
                slot.Instance = null;
                return;
        }

        try
        {
            (slot.Instance as IDisposable)?.Dispose();
        }
        catch
        {
            // A plugin failing to clean up must not block uninstalling it.
        }

        slot.Instance = null;
        slot.Context?.Unload();
        slot.Context = null;
    }

    // ---------------------------------------------------------------- helpers

    private Slot FindSlot(string id) =>
        _slots.FirstOrDefault(slot => string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new PluginException($"Unknown plugin '{id}'.");

    private PluginEntry ToEntry(Slot slot)
    {
        var instance = slot.Instance;
        var sd = slot.StreamDeckManifest;
        return new PluginEntry(
            slot.Id,
            instance?.Name ?? slot.Manifest?.Name ?? sd?.Name ?? slot.Id,
            slot.Manifest?.Version ?? sd?.Version ?? instance?.Version ?? "?",
            slot.Manifest?.Author ?? sd?.Author ?? instance?.Author,
            slot.Manifest?.Description ?? sd?.Description ?? instance?.Description,
            PluginKind.Application,
            slot.Kind == SlotKind.System,
            IsEnabled: !_disabled.Contains(slot.Id) && (slot.Error is null || slot.Kind == SlotKind.StreamDeck),
            CanDisable: !slot.IsRequired,
            CanUninstall: slot.Kind != SlotKind.System,
            instance?.Actions.Count,
            slot.Error,
            slot.Kind == SlotKind.StreamDeck ? StreamDeckSource : DeckHostSource,
            slot.Kind == SlotKind.System ? "Built into DeckHost" : _origins.GetValueOrDefault(slot.Id, "File"),
            slot.StreamDeckManifest?.Category,
            slot.Directory,
            IconPathOf(slot));
    }

    /// <summary>The plugin's icon file: the Stream Deck manifest's Icon, or plugin.json's icon.</summary>
    private static string? IconPathOf(Slot slot)
    {
        if (slot.StreamDeckManifest is { } sd)
        {
            return sd.ResolveImage(sd.Icon);
        }

        if (slot.Manifest?.Icon is { Length: > 0 } icon && slot.Directory is { } directory)
        {
            var path = Path.GetFullPath(Path.Combine(directory, icon));
            return path.StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
        }

        return null;
    }

    private void LoadState()
    {
        try
        {
            var path = Path.Combine(_rootDirectory, StateFileName);
            if (!File.Exists(path))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<CatalogState>(File.ReadAllText(path));
            _disabled.UnionWith(state?.Disabled ?? []);
            _pendingRemoval.UnionWith(state?.PendingRemoval ?? []);
            foreach (var (id, origin) in state?.Origins ?? [])
            {
                _origins[id] = origin;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable state: start with everything enabled.
        }
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(_rootDirectory);
            var state = new CatalogState([.. _disabled.Order()], [.. _pendingRemoval.Order()], new Dictionary<string, string>(_origins));
            File.WriteAllText(Path.Combine(_rootDirectory, StateFileName), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return true;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private enum SlotKind
    {
        System,
        Package,
        StreamDeck,
    }

    private sealed record CatalogState(string[] Disabled, string[] PendingRemoval, Dictionary<string, string>? Origins = null);

    private sealed record Slot(string Id, SlotKind Kind, bool IsRequired, string? Directory)
    {
        public PluginManifest? Manifest { get; set; }

        public StreamDeckManifest? StreamDeckManifest { get; set; }

        public IApplicationPlugin? Instance { get; set; }

        public PluginLoadContext? Context { get; set; }

        public string? Error { get; set; }

        public string DisplayName => Instance?.Name ?? Manifest?.Name ?? StreamDeckManifest?.Name ?? Id;
    }
}
