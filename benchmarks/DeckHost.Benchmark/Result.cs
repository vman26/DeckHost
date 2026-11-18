using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckHost.Benchmark;

/// <summary>The machine a run was measured on.</summary>
public sealed record MachineInfo(string System, string Description, string Architecture, int Processors, double MemoryGb);

/// <summary>One run: what was measured, where, and the numbers (keys from <see cref="Metrics"/>).</summary>
public sealed record BenchmarkResult(
    string Version,
    DateTimeOffset Measured,
    MachineInfo Machine,
    IReadOnlyDictionary<string, double> Metrics)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static BenchmarkResult? FromJson(string json) => JsonSerializer.Deserialize<BenchmarkResult>(json, JsonOptions);
}

/// <summary>The run as Markdown: a table of every number, its limit and, when given, the previous release's value.</summary>
public static class Report
{
    public static string Markdown(BenchmarkResult result, IReadOnlyDictionary<string, double> limits, BenchmarkResult? previous = null)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"### DeckHost {result.Version} on {result.Machine.System}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{result.Machine.Description}, {result.Machine.Architecture}, {result.Machine.Processors} processors, {result.Machine.MemoryGb:0.#} GB.");
        text.AppendLine();
        text.AppendLine(previous is null ? "| | Measured | Limit | |" : $"| | Measured | {previous.Version} | Limit | |");
        text.AppendLine(previous is null ? "|---|---:|---:|---|" : "|---|---:|---:|---:|---|");
        foreach (var metric in Metrics.All)
        {
            if (!result.Metrics.TryGetValue(metric.Key, out var value))
            {
                continue;
            }

            var hasLimit = limits.TryGetValue(metric.Key, out var limit);
            var verdict = !hasLimit ? string.Empty : value <= limit ? "✅" : "❌ over";
            var cells = new List<string> { $"{metric.Label}", Format(value, metric) };
            if (previous is not null)
            {
                cells.Add(previous.Metrics.TryGetValue(metric.Key, out var before) ? Format(before, metric) : "–");
            }

            cells.Add(hasLimit ? Format(limit, metric) : string.Empty);
            cells.Add(verdict);
            text.AppendLine("| " + string.Join(" | ", cells) + " |");
        }

        return text.ToString();
    }

    public static string Format(double value, MetricInfo metric)
    {
        var number = value.ToString("F" + metric.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return metric.Unit.Length == 0 ? number : $"{number} {metric.Unit}";
    }
}
