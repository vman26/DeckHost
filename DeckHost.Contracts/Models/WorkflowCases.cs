namespace DeckHost.Contracts.Models;

/// <summary>One case of a Switch node: a stable id (its output is <c>case:{id}</c>) and the text it is compared with.</summary>
public sealed record WorkflowCase(string Id, string Text);

/// <summary>Reads and writes a Switch node's cases, stored as one <c>id=text</c> line each.</summary>
public static class WorkflowCases
{
    /// <summary>The most cases one Switch node has (a limit for files and the canvas, not for use).</summary>
    public const int Max = 64;

    public const string PortPrefix = "case:";

    /// <summary>The cases in order; lines without an id, empty ids and repeated ids are left out.</summary>
    public static IReadOnlyList<WorkflowCase> Parse(string? text)
    {
        var cases = new List<WorkflowCase>();
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var cut = line.IndexOf('=', StringComparison.Ordinal);
            if (cut <= 0)
            {
                continue;
            }

            var id = line[..cut].Trim();
            if (id.Length > 0 && !cases.Any(existing => existing.Id == id))
            {
                cases.Add(new WorkflowCase(id, line[(cut + 1)..].TrimEnd('\r')));
            }
        }

        return cases;
    }

    public static string Serialize(IEnumerable<WorkflowCase> cases) =>
        string.Join('\n', cases.Select(static one => $"{one.Id}={one.Text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\n', ' ')}"));

    public static string PortOf(string caseId) => PortPrefix + caseId;

    /// <summary>The case id of an output port, or null for any other port.</summary>
    public static string? CaseIdOf(string port) => port.StartsWith(PortPrefix, StringComparison.Ordinal) ? port[PortPrefix.Length..] : null;

    /// <summary>A case id that no case of the node uses yet (c1, c2, …).</summary>
    public static string NextId(IEnumerable<WorkflowCase> cases)
    {
        var used = cases.Select(static one => one.Id).ToHashSet(StringComparer.Ordinal);
        for (var number = 1; ; number++)
        {
            if (!used.Contains("c" + number))
            {
                return "c" + number;
            }
        }
    }
}
