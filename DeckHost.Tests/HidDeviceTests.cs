using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.Hid;

namespace DeckHost.Tests;

public class HidDeviceTests
{
    private static HidDeviceDescription Pedal() => new("pedal", "Foot pedal", 0x1234, 0x5678,
    [
        new HidControl("key.0", "Left", HidControlKind.Key, 0, 0, 72, 72, new HidInput(HidInputKind.Bit, 1, Mask: 0x01)),
        new HidControl("key.1", "Right", HidControlKind.Key, 86, 0, 72, 72, new HidInput(HidInputKind.Value, 2, Value: 7)),
        new HidControl("encoder.0", "Jog", HidControlKind.Knob, 172, 0, 56, 56, new HidInput(HidInputKind.Relative, 3)) { Press = new HidInput(HidInputKind.Bit, 1, Mask: 0x80) },
        new HidControl("encoder.1", "Shuttle", HidControlKind.Knob, 240, 0, 56, 56, new HidInput(HidInputKind.Absolute, 4, Invert: true)),
    ]);

    [Test]
    public void A_description_round_trips_through_json_and_lays_out_its_controls()
    {
        var description = Pedal();
        var copy = HidDeviceDescription.FromJson(description.ToJson())!;

        Assert.That(copy.ToJson(), Is.EqualTo(description.ToJson()));
        Assert.That(copy.DeviceId, Is.EqualTo("hid.pedal"));
        var controls = copy.PhysicalControls();
        Assert.That(controls[0].Type, Is.EqualTo(ControlType.Key));
        Assert.That(controls[0].Inputs.HasFlag(InputCapabilities.LongPress), Is.True);
        Assert.That(controls[2].Inputs, Is.EqualTo(InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release));
        Assert.That(controls[3].Inputs, Is.EqualTo(InputCapabilities.Rotate));
        Assert.That(controls[0].Outputs, Is.EqualTo(OutputCapabilities.None));
        Assert.That(HidDeviceDescription.FromJson("{ nope"), Is.Null);
        Assert.That(HidDeviceDescription.MakeId("My foot pedal #2"), Is.EqualTo("my-foot-pedal-2"));
    }

    [Test]
    public void Reports_become_presses_releases_and_turns()
    {
        var parser = new HidInputParser(Pedal());

        Assert.That(parser.Parse(new byte[] { 0, 0, 0, 0, 100 }), Is.Empty);        // the shuttle's first position is the start
        Assert.That(
            parser.Parse(new byte[] { 0, 0x01, 7, 0xFE, 103 }),
            Is.EqualTo(new HidInputEvent[] { new("key.0", InputCapabilities.Press, 1), new("key.1", InputCapabilities.Press, 1), new("encoder.0", InputCapabilities.Rotate, -2), new("encoder.1", InputCapabilities.Rotate, -3) }));
        Assert.That(
            parser.Parse(new byte[] { 0, 0x80, 0, 0, 103 }),
            Is.EqualTo(new HidInputEvent[] { new("key.0", InputCapabilities.Release, 0), new("key.1", InputCapabilities.Release, 0), new("encoder.0", InputCapabilities.Press, 1) }));
        // Wrapping from 255 to 1 is two steps forward (reversed here: -2).
        parser.Parse(new byte[] { 0, 0x80, 0, 0, 255 });
        Assert.That(parser.Parse(new byte[] { 0, 0x80, 0, 0, 1 }), Does.Contain(new HidInputEvent("encoder.1", InputCapabilities.Rotate, -2)));
    }

    [Test]
    public void Inputs_tied_to_a_report_id_ignore_other_reports()
    {
        var description = new HidDeviceDescription("x", "X", 1, 2, [new HidControl("key.0", "A", HidControlKind.Key, 0, 0, 72, 72, new HidInput(HidInputKind.Bit, 1, Mask: 1, ReportId: 2))]);
        var parser = new HidInputParser(description);

        Assert.That(parser.Parse(new byte[] { 1, 1 }), Is.Empty);
        Assert.That(parser.Parse(new byte[] { 2, 1 }), Has.Exactly(1).Items);
        Assert.That(parser.Parse(new byte[] { 1, 0 }), Is.Empty);                   // another report: still pressed
    }

    [Test]
    public void The_learner_finds_a_key_by_its_bit_or_value_and_ignores_noisy_bytes()
    {
        // Byte 4 is a sensor that changes all the time.
        byte[][] idle = [[0, 0, 0, 0, 10], [0, 0, 0, 0, 11], [0, 0, 0, 0, 12]];

        var bit = HidLearner.LearnKey(idle, [[0, 0, 0, 0, 13], [0, 0, 0x04, 0, 14]]);
        Assert.That(bit, Is.EqualTo(new HidInput(HidInputKind.Bit, 2, Mask: 0x04)));

        var value = HidLearner.LearnKey(idle, [[0, 0, 0, 0x1E, 13]]);
        Assert.That(value, Is.EqualTo(new HidInput(HidInputKind.Value, 3, Value: 0x1E)));

        // Wired the other way: the bit clears while pressed.
        var inverted = HidLearner.LearnKey([[0, 0xFF]], [[0, 0xFB]]);
        Assert.That(inverted, Is.EqualTo(new HidInput(HidInputKind.Bit, 1, Mask: 0x04, Invert: true)));

        Assert.That(HidLearner.LearnKey(idle, [[0, 0, 0, 0, 99]]), Is.Null);
    }

    [Test]
    public void The_learner_tells_stepping_knobs_from_position_knobs_and_their_direction()
    {
        byte[][] idle = [[1, 0, 0, 50]];

        var steps = HidLearner.LearnKnob(idle, [[1, 0, 1, 50], [1, 0, 0, 50], [1, 0, 1, 50]]);
        Assert.That(steps, Is.EqualTo(new HidInput(HidInputKind.Relative, 2, ReportId: 1)));

        var backwards = HidLearner.LearnKnob(idle, [[1, 0, 0xFF, 50], [1, 0, 0xFF, 50]]);
        Assert.That(backwards!.Invert, Is.True);

        var position = HidLearner.LearnKnob(idle, [[1, 0, 0, 52], [1, 0, 0, 55]]);
        Assert.That(position, Is.EqualTo(new HidInput(HidInputKind.Absolute, 3, ReportId: 1)));

        var down = HidLearner.LearnKnob(idle, [[1, 0, 0, 48], [1, 0, 0, 45]]);
        Assert.That(down!.Invert, Is.True);
    }

    [Test]
    public void The_plugin_lists_saved_descriptions_as_offline_devices()
    {
        var folder = Path.Combine(Path.GetTempPath(), "deckhost-hid-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var plugin = new HidDevicePlugin(folder);
            var changes = 0;
            plugin.DevicesChanged += (_, _) => changes++;
            Assert.That(plugin.Devices, Is.Empty);

            plugin.Save(Pedal());
            var device = Expect.Single(plugin.Devices);
            Assert.That((device.Id, device.Name, device.IsConnected), Is.EqualTo(("hid.pedal", "Foot pedal", false)));
            Assert.That(device.Controls.Count, Is.EqualTo(4));

            using (var reloaded = new HidDevicePlugin(folder))
            {
                Assert.That(reloaded.Devices, Has.Exactly(1).Items);
            }

            plugin.Delete("pedal");
            Assert.That(plugin.Devices, Is.Empty);
            Assert.That(changes, Is.EqualTo(2));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
