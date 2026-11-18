using DeckHost.Benchmark;

namespace DeckHost.Tests;

/// <summary>The pure parts of benchmarks/DeckHost.Benchmark: statistics, budgets and the report.</summary>
public sealed class BenchmarkTests
{
    private const string IdleCpu = Metrics.IdleWindowCpu;
    private const string Memory = Metrics.IdleWindowMemory;
    private const double DefaultCpuLimit = 2;
    private const double LinuxCpuLimit = 5;
    private const double MemoryLimit = 300;

    private static readonly string BudgetsJson = $$"""
        {
          // A comment, as in benchmarks/budgets.json.
          "default": { "{{IdleCpu}}": {{DefaultCpuLimit}}, "{{Memory}}": {{MemoryLimit}} },
          "linux": { "{{IdleCpu}}": {{LinuxCpuLimit}} },
        }
        """;

    [TestCase(50, 5)]
    [TestCase(95, 10)]
    [TestCase(100, 10)]
    public void Percentile_TenSamples_IsTheNearestRank(double percent, double expected)
    {
        // Arrange
        double[] samples = [10, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        // Act
        var value = Stats.Percentile(samples, percent);

        // Assert
        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void Percentile_NoSamples_IsZero()
    {
        // Act
        var value = Stats.Percentile([], 95);

        // Assert
        Assert.That(value, Is.Zero);
    }

    [Test]
    public void Growth_ASteadyClimb_IsTheDifferenceBetweenTheEnds()
    {
        // Arrange (100 → 119 MB over 20 samples; the first and last 5 average 102 and 117)
        var samples = Enumerable.Range(100, 20).Select(static value => (double)value).ToList();
        const double ExpectedGrowth = 15;

        // Act
        var growth = Stats.Growth(samples);

        // Assert
        Assert.That(growth, Is.EqualTo(ExpectedGrowth));
    }

    [Test]
    public void For_ASystemWithItsOwnLimit_OverridesOnlyThatLimit()
    {
        // Arrange
        var budgets = Budgets.Parse(BudgetsJson);

        // Act
        var linux = budgets.For("linux");

        // Assert
        Assert.That(linux[IdleCpu], Is.EqualTo(LinuxCpuLimit));
        Assert.That(linux[Memory], Is.EqualTo(MemoryLimit));
    }

    [Test]
    public void Check_ANumberOverItsLimit_IsReported()
    {
        // Arrange
        var budgets = Budgets.Parse(BudgetsJson);
        const double TooMuchCpu = 3;
        var metrics = new Dictionary<string, double> { [IdleCpu] = TooMuchCpu, [Memory] = MemoryLimit };

        // Act
        var over = budgets.Check("windows", metrics);

        // Assert
        var violation = Expect.Single(over);
        Assert.That(violation, Is.EqualTo(new BudgetViolation(IdleCpu, TooMuchCpu, DefaultCpuLimit)));
    }

    [Test]
    public void Check_ANumberNotMeasured_IsNotReported()
    {
        // Arrange
        var budgets = Budgets.Parse(BudgetsJson);

        // Act
        var over = budgets.Check("windows", new Dictionary<string, double>());

        // Assert
        Assert.That(over, Is.Empty);
    }

    [Test]
    public void Markdown_ANumberOverItsLimit_IsMarked()
    {
        // Arrange
        var result = new BenchmarkResult("1.0.0", DateTimeOffset.UnixEpoch, new MachineInfo("windows", "Windows", "x64", 4, 16),
            new Dictionary<string, double> { [IdleCpu] = 3, [Memory] = 100 });
        var limits = Budgets.Parse(BudgetsJson).For("windows");

        // Act
        var markdown = Report.Markdown(result, limits);

        // Assert
        Assert.That(markdown, Does.Contain("| Idle, window open: CPU | 3.0 % of a core | 2.0 % of a core | ❌ over |"));
        Assert.That(markdown, Does.Contain("| Idle, window open: memory | 100 MB | 300 MB | ✅ |"));
    }

    [Test]
    public void FromJson_AWrittenResult_ReadsTheSame()
    {
        // Arrange
        var result = new BenchmarkResult("1.0.0+abc1234", DateTimeOffset.UnixEpoch, new MachineInfo("linux", "Ubuntu", "x64", 4, 16),
            new Dictionary<string, double> { [IdleCpu] = 1.5 });

        // Act
        var read = BenchmarkResult.FromJson(result.ToJson());

        // Assert
        Assert.That(read!.Version, Is.EqualTo(result.Version));
        Assert.That(read.Metrics, Is.EqualTo(result.Metrics));
    }

    [TestCase("/opt/DeckHost/DeckHost.App", "DeckHost.App")]
    [TestCase(@"C:\DeckHost\DeckHost.App.exe", "DeckHost.App")]
    public void ProcessName_AProgram_IsWhatTheSystemListsItAs(string program, string expected)
    {
        // Act
        var name = DeckHostRun.ProcessName(program.Replace('\\', Path.DirectorySeparatorChar));

        // Assert
        Assert.That(name, Is.EqualTo(expected));
    }

    [TestCase("windows")]
    [TestCase("linux")]
    [TestCase("macos")]
    public void For_TheRepositoryBudgets_LimitsEveryCheckedNumber(string system)
    {
        // Arrange
        var budgets = Budgets.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "benchmarks", "budgets.json")));
        string[] checkedMetrics = [Metrics.StartupFirstFrame, Metrics.IdleWindowCpu, Metrics.IdleTrayCpu, Metrics.BusyCpu, Metrics.BusyMemoryGrowth, Metrics.LatencyP95];

        // Act
        var limits = budgets.For(system);

        // Assert
        Assert.That(checkedMetrics, Is.All.Matches<string>(metric => limits.ContainsKey(metric)));
    }

    [Test]
    public void ParseStat_AThreadWhoseNameHasSpacesAndParentheses_ReadsTheNameAndItsTicks()
    {
        // Arrange
        const string Line = "1234 (.NET TP (Worker)) S 1 1234 1234 0 -1 4194368 120 0 0 0 250 75 0 0 20 0 31 0 9000 1000 50";
        const long ExpectedTicks = 250 + 75;

        // Act
        var stat = ThreadUsage.ParseStat(Line);

        // Assert
        Assert.That(stat, Is.EqualTo((".NET TP (Worker)", ExpectedTicks)));
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(path))!;
}
