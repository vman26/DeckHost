using System.Reflection;
using System.Runtime.Loader;

namespace DeckHost.Core.Plugins;

/// <summary>
/// Isolated, unloadable context for one installed plugin. Assemblies are read into memory
/// instead of loaded from disk, so the plugin's files are never locked and uninstalling can
/// delete them right away. DeckHost.Contracts always comes from the host so the plugin's
/// IApplicationPlugin is the same type the host uses.
/// </summary>
internal sealed class PluginLoadContext(string pluginDirectory, string name) : AssemblyLoadContext(name, isCollectible: true)
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        typeof(Contracts.Interfaces.IApplicationPlugin).Assembly.GetName().Name!,
    };

    public Assembly LoadPluginAssembly(string fileName) => LoadFromFile(Path.Combine(pluginDirectory, fileName));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || SharedAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        var candidate = Path.Combine(pluginDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromFile(candidate) : null;
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
