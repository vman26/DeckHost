using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The rules behind number settings' sliders: snapping, clamping and how values are stored.</summary>
public class NumberRangeTests
{
    private const double Tolerance = 1e-9;
    private const string DutchCulture = "nl-NL";

    [TestCase(0, 100, 1, 37.4, 37)]
    [TestCase(0, 100, 1, 37.5, 38)]
    [TestCase(0, 100, 1, -5, 0)]
    [TestCase(0, 100, 1, 250, 100)]
    [TestCase(1, 10, 1, 6.2, 6)]
    [TestCase(0.1, 2, 0.05, 0.37, 0.35)]
    [TestCase(0.1, 2, 0.05, 0.38, 0.4)]
    [TestCase(0, 100, 5, 12, 10)]
    [TestCase(0, 100, 5, 13, 15)]
    [TestCase(-50, 50, 10, -24, -20)]
    public void Snap_AValue_GoesToTheNearestStepInsideTheRange(double minimum, double maximum, double step, double value, double expected)
    {
        // Arrange
        var range = new NumberRange(minimum, maximum, step);

        // Act
        var snapped = range.Snap(value);

        // Assert
        Assert.That(snapped, Is.EqualTo(expected).Within(Tolerance));
    }

    [TestCase(10, 9)]
    [TestCase(50, 9)]
    [TestCase(-1, 0)]
    public void Snap_AStepThatDoesNotDivideTheRange_StaysOnAStepInsideIt(double value, double expected)
    {
        // Arrange (0, 3, 6, 9: the maximum itself isn't a step, so the last step is the highest value)
        var range = new NumberRange(0, 10, 3);

        // Act
        var snapped = range.Snap(value);

        // Assert
        Assert.That(snapped, Is.EqualTo(expected).Within(Tolerance));
    }

    [TestCase(1, 42.0, "42")]
    [TestCase(1, 42.6, "43")]
    [TestCase(0.05, 0.35, "0.35")]
    [TestCase(0.5, 2.0, "2")]
    public void Format_UnderACommaDecimalCulture_StillWritesTheInvariantCulture(double step, double value, string expected)
    {
        // Arrange (as on a Dutch or German Windows, which must not change what is saved)
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(DutchCulture);
        try
        {
            // Act
            var text = new NumberRange(0, 100, step).Format(value);

            // Assert
            Assert.That(text, Is.EqualTo(expected));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    public void Describe_ARangeWithAUnit_AppendsTheUnit()
    {
        // Arrange
        var range = new NumberRange(0, 100) { Unit = "%" };

        // Act
        var text = range.Describe(75);

        // Assert
        Assert.That(text, Is.EqualTo("75 %"));
    }

    [Test]
    public void Describe_ARangeWithoutAUnit_IsJustTheNumber()
    {
        // Act
        var text = new NumberRange(0, 100).Describe(75);

        // Assert
        Assert.That(text, Is.EqualTo("75"));
    }

    [TestCase(1.0, true)]
    [TestCase(5.0, true)]
    [TestCase(0.1, false)]
    public void IsWholeNumber_ByStep_IsTrueOnlyForWholeSteps(double step, bool expected)
    {
        // Arrange
        var range = new NumberRange(0, 100, step);

        // Act
        var isWhole = range.IsWholeNumber;

        // Assert
        Assert.That(isWhole, Is.EqualTo(expected));
    }

    [TestCase("speed", 1, 0.35)]
    [TestCase("bad", 1, 1)]
    [TestCase("missing", 2, 2)]
    public void GetDouble_FromStoredText_ReadsTheNumberOrTheDefault(string key, double fallback, double expected)
    {
        // Arrange
        var settings = new ActionSettings([new("speed", "0.35"), new("bad", "fast")]);

        // Act
        var value = settings.GetDouble(key, fallback);

        // Assert
        Assert.That(value, Is.EqualTo(expected));
    }

    [TestCase("keyBrightness")]
    [TestCase("ledBrightness")]
    public void GetDeviceSettingDefinitions_ABrightnessSetting_IsAPercentageSliderThatIsEmptyByDefault(string key)
    {
        // Arrange
        var plugin = new DeckHost.Device.MiraBox.MiraBoxDevicePlugin();

        // Act
        var definition = plugin.GetDeviceSettingDefinitions(DeckHost.Device.MiraBox.MiraBoxDevicePlugin.PrimaryDeviceId).Single(candidate => candidate.Key == key);

        // Assert
        Assert.That(definition.Kind, Is.EqualTo(ActionSettingKind.Number));
        Assert.That(definition.DefaultValue, Is.EqualTo(string.Empty), "empty by default, so nothing is sent until it is changed");
        Assert.That((definition.Range!.Minimum, definition.Range.Maximum, definition.Range.Unit, definition.Range.UnsetValue), Is.EqualTo((0d, 100d, "%", (double?)100)));
    }
}
