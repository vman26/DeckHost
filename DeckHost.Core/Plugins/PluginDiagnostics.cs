using System.IO.Compression;
using DeckHost.Core.Updates;

namespace DeckHost.Core.Plugins;

/// <summary>What a DeckHost plugin may do, as its <c>plugin.json</c> declares in <c>permissions</c>.</summary>
/// <remarks>
/// Declarations, not enforcement: DeckHost plugins run in the app's process (docs ADR 0007). They
/// tell the user what a plugin does before installing it, and a plugin that declares nothing but
/// opens connections stands out in review.
/// </remarks>
public static class PluginPermissions
{
    public const string Network = "network";
    public const string Processes = "processes";
    public const string Files = "files";
    public const string Input = "input";
    public const string System = "system";
    public const string Clipboard = "clipboard";

    /// <summary>The known permissions and what they mean to a user.</summary>
    public static IReadOnlyDictionary<string, string> Known { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [Network] = "use the network",
        [Processes] = "start programs",
        [Files] = "read and write files",
        [Input] = "press keys and move the mouse",
        [System] = "change system settings (volume, power, displays)",
        [Clipboard] = "read and change the clipboard",
    };

    /// <summary>"It can use the network and start programs." (or that it declares nothing).</summary>
    public static string Describe(IReadOnlyList<string>? permissions)
    {
        var known = (permissions ?? []).Select(static permission => Known.GetValueOrDefault(permission)).OfType<string>().Distinct().ToList();
        return known.Count switch
        {
            0 => "It declares no special permissions.",
            1 => $"It can {known[0]}.",
            _ => $"It can {string.Join(", ", known.Take(known.Count - 1))} and {known[^1]}.",
        };
    }
}

/// <summary>A problem found in a plugin package.</summary>
public sealed record PluginProblem(bool IsError, string Message)
{
    public override string ToString() => (IsError ? "error: " : "warning: ") + Message;
}

/// <summary>
/// Checks a DeckHost plugin package (a folder or a .zip) the way the catalog would load it, without
/// loading code: the manifest, the files it names, the version, permissions and files that don't
/// belong. Used for installed plugins (warnings in the Plugins page) and by
/// <c>DeckHost --check-plugin &lt;package&gt;</c> for plugin authors.
/// </summary>
public static class PluginDiagnostics
{
    /// <summary>Contracts always comes from the host; a copy in the package is ignored.</summary>
    private static readonly string[] HostAssemblies = ["DeckHost.Contracts.dll"];

    public static IReadOnlyList<PluginProblem> Check(string packageOrFolder)
    {
        if (Directory.Exists(packageOrFolder))
        {
            return CheckFolder(packageOrFolder);
        }

        if (!File.Exists(packageOrFolder))
        {
            return [new(true, $"Not found: {packageOrFolder}")];
        }

        var folder = Path.Combine(Path.GetTempPath(), "deckhost-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(packageOrFolder, folder);
            return CheckFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return [new(true, $"Can't unpack the package: {ex.Message}")];
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static IReadOnlyList<PluginProblem> CheckFolder(string folder)
    {
        var problems = new List<PluginProblem>();
        var manifestPath = Path.Combine(folder, PluginManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            problems.Add(new(true, $"No {PluginManifest.FileName} at the root of the package."));
            return problems;
        }

        PluginManifest manifest;
        try
        {
            manifest = PluginManifest.Parse(File.ReadAllText(manifestPath));
        }
        catch (PluginException ex)
        {
            problems.Add(new(true, ex.Message));
            return problems;
        }

        if (!File.Exists(Path.Combine(folder, manifest.Assembly)))
        {
            problems.Add(new(true, $"'{manifest.Assembly}' (the manifest's assembly) is not in the package."));
        }

        if (!SemanticVersion.TryParse(manifest.Version, out _))
        {
            problems.Add(new(false, $"Version '{manifest.Version}' is not a version like 1.2.0: updates can't be ordered."));
        }

        if (manifest.Icon is { Length: > 0 } icon && !File.Exists(Path.Combine(folder, icon)))
        {
            problems.Add(new(false, $"The icon '{icon}' is not in the package."));
        }

        foreach (var permission in manifest.Permissions ?? [])
        {
            if (!PluginPermissions.Known.ContainsKey(permission))
            {
                problems.Add(new(false, $"Unknown permission '{permission}' (known: {string.Join(", ", PluginPermissions.Known.Keys)})."));
            }
        }

        foreach (var host in HostAssemblies)
        {
            if (File.Exists(Path.Combine(folder, host)))
            {
                problems.Add(new(false, $"{host} is in the package but DeckHost always uses its own: reference it with Private=false."));
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.Author))
        {
            problems.Add(new(false, "No author: the Plugins page and the store show who made a plugin."));
        }

        return problems;
    }
}
