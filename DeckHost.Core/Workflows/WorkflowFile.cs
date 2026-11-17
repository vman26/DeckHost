using DeckHost.Contracts.Models;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DeckHost.Core.Workflows;

/// <summary>
/// A workflow as a file to share (<c>.deckhostworkflow</c>, JSON): the workflow with its name,
/// readable by another DeckHost. Reading checks what it gets (it may come from anyone): known node
/// kinds, unique ids, links between existing nodes and valid ports, no loops, sane sizes.
/// </summary>
public static class WorkflowFile
{
    public const string Format = "deckhost-workflow";
    public const string Extension = ".deckhostworkflow";
    public const int CurrentVersion = 1;
    public const int MaxNodes = 300;
    public const int MaxLinks = 1000;
    public const int MaxBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(Workflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return JsonSerializer.Serialize(new { format = Format, version = CurrentVersion, name = workflow.Name, workflow }, Options);
    }

    /// <summary>A file name for a workflow: its name without characters a file can't have.</summary>
    public static string FileNameOf(Workflow workflow, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(workflow.Name) ? fallback : workflow.Name.Trim();
        var clean = string.Concat(name.Where(static c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return (clean.Length == 0 ? "workflow" : clean) + Extension;
    }

    /// <summary>The workflow in a file (or a bare workflow object), checked and cleaned; null with <paramref name="error"/> when it can't be used.</summary>
    public static Workflow? TryRead(string json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes)
        {
            error = "That isn't a DeckHost workflow file.";
            return null;
        }

        Workflow? workflow;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                error = "That isn't a DeckHost workflow file.";
                return null;
            }

            if (root["format"] is { } format && format.GetValue<string>() != Format)
            {
                error = "That file isn't a DeckHost workflow.";
                return null;
            }

            if (root["version"]?.GetValue<int>() > CurrentVersion)
            {
                error = "That workflow was made by a newer DeckHost: update DeckHost to open it.";
                return null;
            }

            var body = root["workflow"] as JsonObject ?? (root["nodes"] is not null ? root : null);
            if (body is null)
            {
                error = "That file has no workflow in it.";
                return null;
            }

            workflow = body.Deserialize<Workflow>(Options);
            if (workflow is not null && string.IsNullOrWhiteSpace(workflow.Name) && root["name"] is JsonValue name && name.TryGetValue<string>(out var fileName))
            {
                workflow = workflow with { Name = fileName };
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            error = "That file couldn't be read as a workflow.";
            return null;
        }

        return workflow is null ? Fail("That file has no workflow in it.", out error) : Check(workflow, out error);
    }

    private static Workflow? Fail(string message, out string? error)
    {
        error = message;
        return null;
    }

    private static Workflow? Check(Workflow workflow, out string? error)
    {
        error = null;
        if (workflow.Nodes.Count > MaxNodes || workflow.Links.Count > MaxLinks)
        {
            return Fail($"That workflow is too big (more than {MaxNodes} nodes or {MaxLinks} links).", out error);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in workflow.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id))
            {
                return Fail("That workflow has a node without an id, or two with the same id.", out error);
            }

            if (!WorkflowNodeKinds.All.Contains(node.Kind))
            {
                return Fail($"That workflow uses a node DeckHost doesn't know ('{node.Kind}'): it may need a newer DeckHost.", out error);
            }

            if (node.Kind == WorkflowNodeKinds.Switch && WorkflowCases.Parse(node.EffectiveSettings.Get(WorkflowKeys.Cases)).Count > WorkflowCases.Max)
            {
                return Fail($"A Switch node in that workflow has more than {WorkflowCases.Max} cases.", out error);
            }
        }

        // Links that lead nowhere are left out, and so is one that would close a loop.
        var checkedWorkflow = workflow with { Nodes = [.. workflow.Nodes.Select(static node => node with { X = Finite(node.X), Y = Finite(node.Y) })], Links = [] };
        var links = new List<WorkflowLink>();
        foreach (var link in workflow.Links.Distinct())
        {
            if (checkedWorkflow.Find(link.From) is not { } from || checkedWorkflow.Find(link.To) is not { } to ||
                !WorkflowNodeKinds.OutputsOf(from).Contains(link.Port) || !WorkflowNodeKinds.HasInput(to.Kind) ||
                (checkedWorkflow with { Links = links }).WouldCycle(link.From, link.To))
            {
                continue;
            }

            links.Add(link);
        }

        return checkedWorkflow with { Links = links };
    }

    private static double Finite(double value) => double.IsFinite(value) ? Math.Clamp(value, -100_000, 100_000) : 0;
}
