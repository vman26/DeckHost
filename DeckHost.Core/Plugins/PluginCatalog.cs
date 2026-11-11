using DeckHost.Contracts.Interfaces;
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
    string? Error);

/// <summary>
/// Keeps track of all plugins: the system plugins compiled into DeckHost and the plugins
/// installed from packages into <c>%AppData%/DeckHost/plugins/functional/&lt;id&gt;</c>.
/// System plugins can be disabled (except required ones) but never uninstalled.
/// Not thread-safe: use it from the UI thread.
/// </summary>
public sealed class PluginCatalog : IDisposable
{
    private const string StateFileName = "state.json";
    private const string StagingDirectoryName = ".staging";

    private readonly string _rootDirectory;
    private readonly IReadOnlyList<IDevicePlugin> _devicePlugins;
    private readonly List<Slot> _slots = [];
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingRemoval = new(StringComparer.OrdinalIgnoreCase);

    public PluginCatalog(
        string rootDirectory,
        IEnumerable<IDevicePlugin> devicePlugins,
        IEnumerable<IApplicationPlugin> systemPlugins,
        IEnumerable<string> requiredPluginIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
        _devicePlugins = devicePlugins.ToList();

        var required = requiredPluginIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in systemPlugins)
        {
            _slots.Add(new Slot(plugin.Id, IsSystem: true, IsRequired: required.Contains(plugin.Id), Directory: null) { Instance = plugin });
        }
    }

    public event EventHandler? Changed;

    public static string DefaultRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "DeckHost",
        "plugins");

    public string InstallDirectory => Path.Combine(_rootDirectory, "functional");

    public IReadOnlyList<IApplicationPlugin> EnabledApplicationPlugins =>
        [.. _slots.Where(slot => slot.Instance is not null && !_disabled.Contains(slot.Id)).Select(slot => slot.Instance!)];

    public IReadOnlyList<PluginEntry> Entries =>
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

    /// <summary>Finishes pending removals and loads the enabled installed plugins.</summary>
    public void Load()
    {
        LoadState();

        foreach (var id in _pendingRemoval.ToList())
        {
            if (TryDeleteDirectory(Path.Combine(InstallDirectory, id)))
            {
                _pendingRemoval.Remove(id);
            }
        }

        if (Directory.Exists(InstallDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(InstallDirectory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(directory);
                if (_pendingRemoval.Contains(id) || _slots.Any(slot => string.Equals(slot.Id, id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var slot = new Slot(id, IsSystem: false, IsRequired: false, directory);
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

        TryDeleteDirectory(Path.Combine(_rootDirectory, StagingDirectoryName));
        SaveState();
    }

    /// <summary>
    /// Installs (or upgrades) a plugin package. The package is checked and loaded in a staging
    /// folder first, so a broken package never replaces a working installation.
    /// </summary>
    public PluginEntry Install(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new PluginException($"Package not found: {packagePath}");
        }

        PluginManifest manifest;
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntry = archive.GetEntry(PluginManifest.FileName)
                ?? throw new PluginException($"The package has no {PluginManifest.FileName} in its root.");
            using var reader = new StreamReader(manifestEntry.Open());
            manifest = PluginManifest.Parse(reader.ReadToEnd());
        }
        catch (InvalidDataException ex)
        {
            throw new PluginException($"The file is not a valid plugin package (zip): {ex.Message}");
        }

        if (_slots.Any(slot => slot.IsSystem && string.Equals(slot.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)) ||
            _devicePlugins.Any(plugin => string.Equals(plugin.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PluginException($"'{manifest.Id}' is a system plugin and is already part of DeckHost.");
        }

        var stagingDirectory = Path.Combine(_rootDirectory, StagingDirectoryName, Guid.NewGuid().ToString("N"));
        try
        {
            try
            {
                ZipFile.ExtractToDirectory(packagePath, stagingDirectory);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw new PluginException($"Could not unpack the package: {ex.Message}");
            }

            // Trial load: proves the package contains a working plugin before anything is replaced.
            var trial = new Slot(manifest.Id, IsSystem: false, IsRequired: false, stagingDirectory) { Manifest = manifest };
            LoadInstance(trial);
            Unload(trial);

            var existing = _slots.FirstOrDefault(slot => string.Equals(slot.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
            var targetDirectory = Path.Combine(InstallDirectory, manifest.Id);
            if (existing is not null)
            {
                Unload(existing);
                _slots.Remove(existing);
            }

            if (Directory.Exists(targetDirectory) && !TryDeleteDirectory(targetDirectory))
            {
                throw new PluginException("The previous version's files are in use; restart DeckHost and install again.");
            }

            Directory.CreateDirectory(InstallDirectory);
            Directory.Move(stagingDirectory, targetDirectory);

            var slot = new Slot(manifest.Id, IsSystem: false, IsRequired: false, targetDirectory) { Manifest = manifest };
            _slots.Add(slot);
            _disabled.Remove(manifest.Id);
            _pendingRemoval.Remove(manifest.Id);
            LoadInstance(slot);
            SaveState();
            OnChanged();
            return ToEntry(slot);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    public void Uninstall(string id)
    {
        var slot = FindSlot(id);
        if (slot.IsSystem)
        {
            throw new PluginException($"{slot.DisplayName} is a system plugin and can't be uninstalled. You can disable it instead.");
        }

        Unload(slot);
        _slots.Remove(slot);
        _disabled.Remove(slot.Id);
        if (slot.Directory is not null && !TryDeleteDirectory(slot.Directory))
        {
            _pendingRemoval.Add(slot.Id);
        }

        SaveState();
        OnChanged();
    }

    public void SetEnabled(string id, bool enabled)
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
            if (!slot.IsSystem)
            {
                LoadInstance(slot);
            }
        }
        else
        {
            _disabled.Add(slot.Id);
            if (!slot.IsSystem)
            {
                Unload(slot);
            }
        }

        SaveState();
        OnChanged();
    }

    public void Dispose()
    {
        foreach (var slot in _slots.Where(static slot => !slot.IsSystem))
        {
            Unload(slot);
        }
    }

    // ---------------------------------------------------------------- loading

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

    private static void Unload(Slot slot)
    {
        if (slot.IsSystem)
        {
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
        return new PluginEntry(
            slot.Id,
            instance?.Name ?? slot.Manifest?.Name ?? slot.Id,
            slot.Manifest?.Version ?? instance?.Version ?? "?",
            slot.Manifest?.Author ?? instance?.Author,
            slot.Manifest?.Description ?? instance?.Description,
            PluginKind.Application,
            slot.IsSystem,
            IsEnabled: !_disabled.Contains(slot.Id) && slot.Error is null,
            CanDisable: !slot.IsRequired,
            CanUninstall: !slot.IsSystem,
            instance?.Actions.Count,
            slot.Error);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

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
            var state = new CatalogState([.. _disabled.Order()], [.. _pendingRemoval.Order()]);
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

    private sealed record CatalogState(string[] Disabled, string[] PendingRemoval);

    private sealed record Slot(string Id, bool IsSystem, bool IsRequired, string? Directory)
    {
        public PluginManifest? Manifest { get; set; }

        public IApplicationPlugin? Instance { get; set; }

        public PluginLoadContext? Context { get; set; }

        public string? Error { get; set; }

        public string DisplayName => Instance?.Name ?? Manifest?.Name ?? Id;
    }
}
