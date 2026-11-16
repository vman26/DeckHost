using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The rules behind number settings' sliders: snapping, clamping and how values are stored.</summary>
public class NumberRangeTests
{
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
    public void Snaps_to_the_nearest_step_inside_the_range(double minimum, double maximum, double step, double value, double expected) =>
        Assert.That(new NumberRange(minimum, maximum, step).Snap(value), Is.EqualTo(expected).Within(1e-9));

    [Test]
    public void A_step_that_does_not_divide_the_range_stays_on_a_step_inside_it()
    {
        // 0, 3, 6, 9: the maximum itself isn't a step, so the last step is the highest value.
        var range = new NumberRange(0, 10, 3);
        Assert.That(range.Snap(10), Is.EqualTo(9).Within(1e-9));
        Assert.That(range.Snap(50), Is.EqualTo(9).Within(1e-9));
        Assert.That(range.Snap(-1), Is.EqualTo(0).Within(1e-9));
    }

    [TestCase(1, 42.0, "42")]
    [TestCase(1, 42.6, "43")]
    [TestCase(0.05, 0.35, "0.35")]
    [TestCase(0.5, 2.0, "2")]
    public void Stores_values_in_the_invariant_culture(double step, double value, string expected)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A comma-decimal culture (as on a Dutch or German Windows) must not change what is saved.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
            Assert.That(new NumberRange(0, 100, step).Format(value), Is.EqualTo(expected));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Test]
    public void Describes_values_with_their_unit()
    {
        Assert.That(new NumberRange(0, 100) { Unit = "%" }.Describe(75), Is.EqualTo("75 %"));
        Assert.That(new NumberRange(0, 100).Describe(75), Is.EqualTo("75"));
    }

    [Test]
    public void Knows_whole_and_fractional_steps()
    {
        Assert.That(new NumberRange(0, 100).IsWholeNumber, Is.True);
        Assert.That(new NumberRange(0, 100, 5).IsWholeNumber, Is.True);
        Assert.That(new NumberRange(0, 1, 0.1).IsWholeNumber, Is.False);
    }

    [Test]
    public void Reads_stored_numbers_back()
    {
        var settings = new ActionSettings([new("speed", "0.35"), new("bad", "fast")]);
        Assert.That(settings.GetDouble("speed", 1), Is.EqualTo(0.35));
        Assert.That(settings.GetDouble("bad", 1), Is.EqualTo(1));
        Assert.That(settings.GetDouble("missing", 2), Is.EqualTo(2));
    }

    [Test]
    public void Device_brightness_has_a_percentage_slider_that_defaults_to_the_device_default()
    {
        var definitions = new DeckHost.Device.MiraBox.MiraBoxDevicePlugin().GetDeviceSettingDefinitions(DeckHost.Device.MiraBox.MiraBoxDevicePlugin.PrimaryDeviceId);
        foreach (var key in new[] { "keyBrightness", "ledBrightness" })
        {
            var definition = definitions.Single(candidate => candidate.Key == key);
            Assert.That(definition.Kind, Is.EqualTo(ActionSettingKind.Number));

            // Empty by default, so nothing is sent until it is changed; the slider shows the device's 100 %.
            Assert.That(definition.DefaultValue, Is.EqualTo(string.Empty));
            Assert.That((definition.Range!.Minimum, definition.Range.Maximum, definition.Range.Unit, definition.Range.UnsetValue), Is.EqualTo((0d, 100d, "%", (double?)100)));
        }
    }
}
