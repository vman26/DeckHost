using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace DeckHost.Core.Plugins;

/// <summary>
/// Isolated context for one installed plugin. Assemblies are read into memory instead of loaded
/// from disk, so the plugin's files are never locked and uninstalling can delete them right away.
/// DeckHost.Contracts (and SkiaSharp) always come from the host so the plugin's interfaces are the
/// same types the host uses.
/// <para>
/// Action plugins get a collectible context (unloaded when they are disabled or removed). Device
/// plugins don't (<paramref name="collectible"/> false): they call native code that calls back
/// (<c>[UnmanagedCallersOnly]</c>), which a collectible context can't host; a disabled one stays in
/// memory until DeckHost restarts.
/// </para>
/// <para>
/// A memory-loaded assembly has no <see cref="Assembly.Location"/>, so the plugin's folder is published
/// as the <see cref="AppContext"/> value <c>DeckHost.PluginDirectory.&lt;assembly name&gt;</c>
/// (<see cref="DirectoryKey"/>), and native libraries the plugin imports are looked up in that folder.
/// </para>
/// </summary>
internal sealed class PluginLoadContext(string pluginDirectory, string name, bool collectible = true) : AssemblyLoadContext(name, isCollectible: collectible)
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        typeof(Contracts.Interfaces.IApplicationPlugin).Assembly.GetName().Name!,

        // Device plugins draw with the host's SkiaSharp (managed and native), so a second copy never loads.
        typeof(SkiaSharp.SKBitmap).Assembly.GetName().Name!,
    };

    /// <summary>
    /// Libraries that keep process-wide state and so must exist once per process, shared by every plugin and the app:
    /// HidSharp starts a device-watching thread that registers a fixed Windows window class, and a second copy (one per
    /// device plugin, plus the app's device creator) fails with "HidSharp RegisterClass failed", closing DeckHost.
    /// The host's copy is used when it has one, else the first plugin's copy is loaded into the default context.
    /// </summary>
    private static readonly HashSet<string> ProcessWideAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "HidSharp",
    };

    private static readonly Lock ProcessWideSync = new();

    /// <summary>The <see cref="AppContext"/> key under which a plugin assembly finds its folder.</summary>
    public static string DirectoryKey(string assemblyName) => "DeckHost.PluginDirectory." + assemblyName;

    public Assembly LoadPluginAssembly(string fileName)
    {
        AppContext.SetData(DirectoryKey(Path.GetFileNameWithoutExtension(fileName)), pluginDirectory);
        return LoadFromFile(Path.Combine(pluginDirectory, fileName));
    }

    /// <summary>Unloads a collectible context; a device plugin's context stays (see the class remarks).</summary>
    public void TryUnload()
    {
        if (IsCollectible)
        {
            Unload();
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || SharedAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        if (ProcessWideAssemblies.Contains(assemblyName.Name))
        {
            return LoadProcessWide(assemblyName);
        }

        var candidate = Path.Combine(pluginDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromFile(candidate) : null;
    }

    private Assembly? LoadProcessWide(AssemblyName assemblyName)
    {
        lock (ProcessWideSync)
        {
            var loaded = Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null)
            {
                return loaded;
            }

            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
            {
                // The host doesn't ship it: the plugin's copy becomes the process's.
            }

            var candidate = Path.Combine(pluginDirectory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? Default.LoadFromAssemblyPath(candidate) : null;
        }
    }

    /// <summary>A <c>[DllImport]</c>/<c>[LibraryImport]</c> of the plugin: its own folder first, then the usual places.</summary>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var candidate in NativeFileNames(unmanagedDllName))
        {
            var path = Path.Combine(pluginDirectory, candidate);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> NativeFileNames(string name)
    {
        yield return name;
        if (Path.HasExtension(name))
        {
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return name + ".dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return name + ".dylib";
            yield return "lib" + name + ".dylib";
        }
        else
        {
            yield return name + ".so";
            yield return "lib" + name + ".so";
        }
    }

    private Assembly LoadFromFile(string path)
    {
        using var assembly = new MemoryStream(File.ReadAllBytes(path));
        var symbolsPath = Path.ChangeExtension(path, ".pdb");
        if (File.Exists(symbolsPath))
        {
            using var symbols = new MemoryStream(File.ReadAllBytes(symbolsPath));
            return LoadFromStream(assembly, symbols);
        }

        return LoadFromStream(assembly);
    }
}
