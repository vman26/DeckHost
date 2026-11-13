using System.Text.Json.Nodes;

namespace DeckHost.Core.Profiles;

/// <summary>
/// Upgrades a saved profiles document from older formats, one version at a time (docs ADR 0005).
/// Each step works on the JSON tree, never on today's C# types, so it keeps working as those change.
/// Additive changes (a new optional property) need no step; renames, moves and changes of meaning do.
/// </summary>
public static class ProfileMigrations
{
    /// <summary>A step from <see cref="From"/> to <c>From + 1</c>.</summary>
    public sealed record Step(int From, string Description, Action<JsonObject> Apply);

    /// <summary>
    /// The steps, in order. Version 1 is the first format with a version; add a step (and bump
    /// <see cref="ProfileDocument.CurrentVersion"/>) for each format change, with a test that
    /// migrates a sample of the old format.
    /// </summary>
    public static IReadOnlyList<Step> Steps { get; } = [];

    /// <summary>The document's format version (files without one are version 1).</summary>
    public static int VersionOf(JsonObject document) =>
        document.TryGetPropertyValue("version", out var node) && node is JsonValue value && value.TryGetValue<int>(out var version) ? version : 1;

    /// <summary>
    /// Applies the steps from the document's version up to <paramref name="target"/> and sets its
    /// version. Returns the descriptions of the steps that ran. A newer document is left as it is.
    /// </summary>
    public static IReadOnlyList<string> Migrate(JsonObject document, int target = ProfileDocument.CurrentVersion, IReadOnlyList<Step>? steps = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        steps ??= Steps;
        var version = VersionOf(document);
        var applied = new List<string>();
        while (version < target)
        {
            var step = steps.FirstOrDefault(candidate => candidate.From == version)
                ?? throw new InvalidOperationException($"No migration from profile format {version}.");
            step.Apply(document);
            applied.Add($"{version} → {version + 1}: {step.Description}");
            version++;
            document["version"] = version;
        }

        return applied;
    }
}
