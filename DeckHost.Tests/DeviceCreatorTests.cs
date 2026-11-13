using DeckHost.App.ViewModels;
using DeckHost.Device.Hid;

namespace DeckHost.Tests;

public sealed class DeviceCreatorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-creator-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeSource : IHidDeviceSource
    {
        public Action<byte[]>? Report { get; private set; }

        public IReadOnlyList<HidDeviceInfo> List() => [new("/dev/hidraw0", 0x16C0, 0x05DF, "Button Box", "Example", 0xFF00, 1, 4)];

        public IDisposable Listen(string path, Action<byte[]> report, Action<string> failed)
        {
            Report = report;
            return new Stop(() => Report = null);
        }

        private sealed class Stop(Action stop) : IDisposable
        {
            public void Dispose() => stop();
        }
    }

    [Fact]
    public void Keys_and_a_pushable_knob_are_learned_placed_numbered_and_saved_as_a_device()
    {
        using var plugin = new HidDevicePlugin(_folder);
        var source = new FakeSource();
        using var creator = new DeviceCreatorViewModel(plugin, source, static action => action());
        creator.SelectedHardware = creator.Hardware.Single();
        Assert.Equal("Button Box", creator.Name);
        void Send(params byte[] report) => source.Report!(report);

        foreach (var bit in new byte[] { 0x01, 0x02 })
        {
            creator.AddKeyCommand.Execute(null);
            Assert.True(creator.IsLearning);
            Send(1, bit, 0, 0);
            Send(1, 0, 0, 0);
        }

        // The same key again is recognized, not added twice.
        creator.AddKeyCommand.Execute(null);
        Send(1, 0x01, 0, 0);
        Assert.Contains("already there", creator.Status);

        creator.AddKnobCommand.Execute(null);
        Send(1, 0, 1, 0);
        Send(1, 0, 1, 0);
        creator.DoneTurningCommand.Execute(null);
        creator.LearnPressCommand.Execute(null);
        Send(1, 0, 0, 0x10);

        // Dragged: Key 2 to the left of Key 1, so it becomes key.0.
        var second = creator.Controls.Single(control => control.Name == "Key 2");
        second.MoveTo(-50, 0);
        creator.Controls.Single(control => control.Name == "Key 1").MoveTo(100, 0);
        second.Dropped();

        creator.SaveCommand.Execute(null);

        var description = Assert.Single(plugin.Descriptions);
        Assert.Equal(("button-box", 0x16C0, 0x05DF, 0xFF00), (description.Id, description.VendorId, description.ProductId, description.UsagePage));
        Assert.Equal(["key.0:Key 2", "key.1:Key 1", "encoder.0:Knob 1"], description.Controls.Select(static control => $"{control.Id}:{control.Name}"));
        Assert.Equal(new HidInput(HidInputKind.Relative, 2, ReportId: 1), description.Controls[2].Input);
        Assert.Equal(new HidInput(HidInputKind.Bit, 3, Mask: 0x10, ReportId: 1), description.Controls[2].Press);
        Assert.Equal("hid.button-box", Assert.Single(plugin.Devices).Id);

        // Live: a press lights the key up.
        Send(1, 0x02, 0, 0);
        Assert.True(second.IsDown);

        creator.Dispose();
        Assert.Null(source.Report);
    }
}
