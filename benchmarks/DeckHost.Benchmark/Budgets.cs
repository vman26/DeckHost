using System.Text.Json;

namespace DeckHost.Benchmark;

/// <summary>A number over its limit.</summary>
public sealed record BudgetViolation(string Metric, double Value, double Limit);

/// <summary>
/// The limits a run must stay under (benchmarks/budgets.json): <c>default</c> for every system and
/// <c>windows</c>, <c>linux</c> or <c>macos</c> to override some. Every limit is a maximum.
/// </summary>
public sealed class Budgets
{
    private readonly Dictionary<string, Dictionary<string, double>> _limits;

    private Budgets(Dictionary<string, Dictionary<string, double>> limits) => _limits = limits;

    public static Budgets Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var limits = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in document.RootElement.EnumerateObject())
        {
            if (system.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            limits[system.Name] = system.Value.EnumerateObject()
                .Where(static limit => limit.Value.ValueKind == JsonValueKind.Number)
                .ToDictionary(static limit => limit.Name, static limit => limit.Value.GetDouble());
        }

        return new Budgets(limits);
    }

    /// <summary>The limits for a system: the defaults with its own on top.</summary>
    public IReadOnlyDictionary<string, double> For(string system)
    {
        var result = new Dictionary<string, double>(_limits.GetValueOrDefault("default") ?? []);
        foreach (var (metric, limit) in _limits.GetValueOrDefault(system) ?? [])
        {
            result[metric] = limit;
        }

        return result;
    }

    /// <summary>Every measured number over its limit (a number that wasn't measured isn't checked).</summary>
    public IReadOnlyList<BudgetViolation> Check(string system, IReadOnlyDictionary<string, double> metrics) =>
        [.. For(system)
            .Where(limit => metrics.TryGetValue(limit.Key, out var value) && value > limit.Value)
            .Select(limit => new BudgetViolation(limit.Key, metrics[limit.Key], limit.Value))];
}
