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

    [Fact]
    public void A_description_round_trips_through_json_and_lays_out_its_controls()
    {
        var description = Pedal();
        var copy = HidDeviceDescription.FromJson(description.ToJson())!;

        Assert.Equal(description.ToJson(), copy.ToJson());
        Assert.Equal("hid.pedal", copy.DeviceId);
        var controls = copy.PhysicalControls();
        Assert.Equal(ControlType.Key, controls[0].Type);
        Assert.True(controls[0].Inputs.HasFlag(InputCapabilities.LongPress));
        Assert.Equal(InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release, controls[2].Inputs);
        Assert.Equal(InputCapabilities.Rotate, controls[3].Inputs);
        Assert.Equal(OutputCapabilities.None, controls[0].Outputs);
        Assert.Null(HidDeviceDescription.FromJson("{ nope"));
        Assert.Equal("my-foot-pedal-2", HidDeviceDescription.MakeId("My foot pedal #2"));
    }

    [Fact]
    public void Reports_become_presses_releases_and_turns()
    {
        var parser = new HidInputParser(Pedal());

        Assert.Empty(parser.Parse(new byte[] { 0, 0, 0, 0, 100 }));        // the shuttle's first position is the start
        Assert.Equal(
            [new("key.0", InputCapabilities.Press, 1), new("key.1", InputCapabilities.Press, 1), new("encoder.0", InputCapabilities.Rotate, -2), new("encoder.1", InputCapabilities.Rotate, -3)],
            parser.Parse(new byte[] { 0, 0x01, 7, 0xFE, 103 }));
        Assert.Equal(
            [new("key.0", InputCapabilities.Release, 0), new("key.1", InputCapabilities.Release, 0), new("encoder.0", InputCapabilities.Press, 1)],
            parser.Parse(new byte[] { 0, 0x80, 0, 0, 103 }));
        // Wrapping from 255 to 1 is two steps forward (reversed here: -2).
        parser.Parse(new byte[] { 0, 0x80, 0, 0, 255 });
        Assert.Contains(new HidInputEvent("encoder.1", InputCapabilities.Rotate, -2), parser.Parse(new byte[] { 0, 0x80, 0, 0, 1 }));
    }

    [Fact]
    public void Inputs_tied_to_a_report_id_ignore_other_reports()
    {
        var description = new HidDeviceDescription("x", "X", 1, 2, [new HidControl("key.0", "A", HidControlKind.Key, 0, 0, 72, 72, new HidInput(HidInputKind.Bit, 1, Mask: 1, ReportId: 2))]);
        var parser = new HidInputParser(description);

        Assert.Empty(parser.Parse(new byte[] { 1, 1 }));
        Assert.Single(parser.Parse(new byte[] { 2, 1 }));
        Assert.Empty(parser.Parse(new byte[] { 1, 0 }));                   // another report: still pressed
    }

    [Fact]
    public void The_learner_finds_a_key_by_its_bit_or_value_and_ignores_noisy_bytes()
    {
        // Byte 4 is a sensor that changes all the time.
        byte[][] idle = [[0, 0, 0, 0, 10], [0, 0, 0, 0, 11], [0, 0, 0, 0, 12]];

        var bit = HidLearner.LearnKey(idle, [[0, 0, 0, 0, 13], [0, 0, 0x04, 0, 14]]);
        Assert.Equal(new HidInput(HidInputKind.Bit, 2, Mask: 0x04), bit);

        var value = HidLearner.LearnKey(idle, [[0, 0, 0, 0x1E, 13]]);
        Assert.Equal(new HidInput(HidInputKind.Value, 3, Value: 0x1E), value);

        // Wired the other way: the bit clears while pressed.
        var inverted = HidLearner.LearnKey([[0, 0xFF]], [[0, 0xFB]]);
        Assert.Equal(new HidInput(HidInputKind.Bit, 1, Mask: 0x04, Invert: true), inverted);

        Assert.Null(HidLearner.LearnKey(idle, [[0, 0, 0, 0, 99]]));
    }

    [Fact]
    public void The_learner_tells_stepping_knobs_from_position_knobs_and_their_direction()
    {
        byte[][] idle = [[1, 0, 0, 50]];

        var steps = HidLearner.LearnKnob(idle, [[1, 0, 1, 50], [1, 0, 0, 50], [1, 0, 1, 50]]);
        Assert.Equal(new HidInput(HidInputKind.Relative, 2, ReportId: 1), steps);

        var backwards = HidLearner.LearnKnob(idle, [[1, 0, 0xFF, 50], [1, 0, 0xFF, 50]]);
        Assert.True(backwards!.Invert);

        var position = HidLearner.LearnKnob(idle, [[1, 0, 0, 52], [1, 0, 0, 55]]);
        Assert.Equal(new HidInput(HidInputKind.Absolute, 3, ReportId: 1), position);

        var down = HidLearner.LearnKnob(idle, [[1, 0, 0, 48], [1, 0, 0, 45]]);
        Assert.True(down!.Invert);
    }

    [Fact]
    public void The_plugin_lists_saved_descriptions_as_offline_devices()
    {
        var folder = Path.Combine(Path.GetTempPath(), "deckhost-hid-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var plugin = new HidDevicePlugin(folder);
            var changes = 0;
            plugin.DevicesChanged += (_, _) => changes++;
            Assert.Empty(plugin.Devices);

            plugin.Save(Pedal());
            var device = Assert.Single(plugin.Devices);
            Assert.Equal(("hid.pedal", "Foot pedal", false), (device.Id, device.Name, device.IsConnected));
            Assert.Equal(4, device.Controls.Count);

            using (var reloaded = new HidDevicePlugin(folder))
            {
                Assert.Single(reloaded.Devices);
            }

            plugin.Delete("pedal");
            Assert.Empty(plugin.Devices);
            Assert.Equal(2, changes);
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
