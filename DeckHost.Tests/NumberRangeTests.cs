using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The rules behind number settings' sliders: snapping, clamping and how values are stored.</summary>
public class NumberRangeTests
{
    [Theory]
    [InlineData(0, 100, 1, 37.4, 37)]
    [InlineData(0, 100, 1, 37.5, 38)]
    [InlineData(0, 100, 1, -5, 0)]
    [InlineData(0, 100, 1, 250, 100)]
    [InlineData(1, 10, 1, 6.2, 6)]
    [InlineData(0.1, 2, 0.05, 0.37, 0.35)]
    [InlineData(0.1, 2, 0.05, 0.38, 0.4)]
    [InlineData(0, 100, 5, 12, 10)]
    [InlineData(0, 100, 5, 13, 15)]
    [InlineData(-50, 50, 10, -24, -20)]
    public void Snaps_to_the_nearest_step_inside_the_range(double minimum, double maximum, double step, double value, double expected) =>
        Assert.Equal(expected, new NumberRange(minimum, maximum, step).Snap(value), 9);

    [Fact]
    public void A_step_that_does_not_divide_the_range_stays_on_a_step_inside_it()
    {
        // 0, 3, 6, 9: the maximum itself isn't a step, so the last step is the highest value.
        var range = new NumberRange(0, 10, 3);
        Assert.Equal(9, range.Snap(10), 9);
        Assert.Equal(9, range.Snap(50), 9);
        Assert.Equal(0, range.Snap(-1), 9);
    }

    [Theory]
    [InlineData(1, 42.0, "42")]
    [InlineData(1, 42.6, "43")]
    [InlineData(0.05, 0.35, "0.35")]
    [InlineData(0.5, 2.0, "2")]
    public void Stores_values_in_the_invariant_culture(double step, double value, string expected)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A comma-decimal culture (as on a Dutch or German Windows) must not change what is saved.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
            Assert.Equal(expected, new NumberRange(0, 100, step).Format(value));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Describes_values_with_their_unit()
    {
        Assert.Equal("75 %", new NumberRange(0, 100) { Unit = "%" }.Describe(75));
        Assert.Equal("75", new NumberRange(0, 100).Describe(75));
    }

    [Fact]
    public void Knows_whole_and_fractional_steps()
    {
        Assert.True(new NumberRange(0, 100).IsWholeNumber);
        Assert.True(new NumberRange(0, 100, 5).IsWholeNumber);
        Assert.False(new NumberRange(0, 1, 0.1).IsWholeNumber);
    }

    [Fact]
    public void Reads_stored_numbers_back()
    {
        var settings = new ActionSettings([new("speed", "0.35"), new("bad", "fast")]);
        Assert.Equal(0.35, settings.GetDouble("speed", 1));
        Assert.Equal(1, settings.GetDouble("bad", 1));
        Assert.Equal(2, settings.GetDouble("missing", 2));
    }

    [Fact]
    public void Device_brightness_has_a_percentage_slider_that_defaults_to_the_device_default()
    {
        var definitions = new DeckHost.Device.MiraBox.MiraBoxDevicePlugin().GetDeviceSettingDefinitions(DeckHost.Device.MiraBox.MiraBoxDevicePlugin.PrimaryDeviceId);
        foreach (var key in new[] { "keyBrightness", "ledBrightness" })
        {
            var definition = definitions.Single(candidate => candidate.Key == key);
            Assert.Equal(ActionSettingKind.Number, definition.Kind);

            // Empty by default, so nothing is sent until it is changed; the slider shows the device's 100 %.
            Assert.Equal(string.Empty, definition.DefaultValue);
            Assert.Equal((0d, 100d, "%", (double?)100), (definition.Range!.Minimum, definition.Range.Maximum, definition.Range.Unit, definition.Range.UnsetValue));
        }
    }
}
