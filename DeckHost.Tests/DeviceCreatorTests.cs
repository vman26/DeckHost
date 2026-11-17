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

    [Test]
    public void A_key_pressed_to_look_at_the_reports_can_still_be_added_afterwards()
    {
        // The user presses the key to watch What it sends, then takes a while to click Add key.
        var now = DateTimeOffset.UtcNow;
        using var plugin = new HidDevicePlugin(_folder);
        var source = new FakeSource();
        using var creator = new DeviceCreatorViewModel(plugin, source, static action => action(), () => now);
        creator.SelectedHardware = creator.Hardware.Single();
        void Send(params byte[] report) => source.Report!(report);
        Send(1, 0, 0, 0);
        Send(1, 0x01, 0, 0);
        Send(1, 0, 0, 0);

        now += TimeSpan.FromSeconds(10);
        creator.AddKeyCommand.Execute(null);
        Send(1, 0x01, 0, 0);

        Assert.That(creator.Controls.Select(static control => control.Name), Is.EqualTo(new[] { "Key 1" }));
        Assert.That(creator.Status, Does.StartWith("Added Key 1"));
    }

    [Test]
    public void A_key_that_is_pressed_right_before_Add_key_asks_to_wait_and_try_again()
    {
        var now = DateTimeOffset.UtcNow;
        using var plugin = new HidDevicePlugin(_folder);
        var source = new FakeSource();
        using var creator = new DeviceCreatorViewModel(plugin, source, static action => action(), () => now);
        creator.SelectedHardware = creator.Hardware.Single();
        void Send(params byte[] report) => source.Report!(report);
        Send(1, 0, 0, 0);
        Send(1, 0x01, 0, 0);
        Send(1, 0, 0, 0);

        creator.AddKeyCommand.Execute(null);
        Send(1, 0x01, 0, 0);

        Assert.That(creator.Controls, Is.Empty);
        Assert.That(creator.Status, Does.Contain("try again"));

        // After letting go and a pause the same key is learned.
        Send(1, 0, 0, 0);
        now += TimeSpan.FromSeconds(5);
        creator.CancelLearningCommand.Execute(null);
        creator.AddKeyCommand.Execute(null);
        Send(1, 0x01, 0, 0);
        Assert.That(creator.Controls.Select(static control => control.Name), Is.EqualTo(new[] { "Key 1" }));
    }

    [Test]
    public void A_device_that_streams_changing_values_can_still_have_its_keys_added()
    {
        var now = DateTimeOffset.UtcNow;
        using var plugin = new HidDevicePlugin(_folder);
        var source = new FakeSource();
        using var creator = new DeviceCreatorViewModel(plugin, source, static action => action(), () => now);
        creator.SelectedHardware = creator.Hardware.Single();
        void Send(params byte[] report) => source.Report!(report);

        // A counter byte changes with every report.
        for (byte counter = 0; counter < 8; counter++)
        {
            Send(1, 0, counter, 0);
        }

        creator.AddKeyCommand.Execute(null);
        Send(1, 0, 9, 0);
        Send(1, 0, 10, 0);
        Assert.That(creator.IsLearning, Is.True);
        Assert.That(creator.Status, Does.Not.Contain("try again"));

        Send(1, 0x04, 11, 0);
        Assert.That(creator.Controls.Select(static control => control.Name), Is.EqualTo(new[] { "Key 1" }));
    }

    [Test]
    public void Keys_and_a_pushable_knob_are_learned_placed_numbered_and_saved_as_a_device()
    {
        using var plugin = new HidDevicePlugin(_folder);
        var source = new FakeSource();
        using var creator = new DeviceCreatorViewModel(plugin, source, static action => action());
        creator.SelectedHardware = creator.Hardware.Single();
        Assert.That(creator.Name, Is.EqualTo("Button Box"));
        void Send(params byte[] report) => source.Report!(report);

        foreach (var bit in new byte[] { 0x01, 0x02 })
        {
            creator.AddKeyCommand.Execute(null);
            Assert.That(creator.IsLearning, Is.True);
            Send(1, bit, 0, 0);
            Send(1, 0, 0, 0);
        }

        // The same key again is recognized, not added twice.
        creator.AddKeyCommand.Execute(null);
        Send(1, 0x01, 0, 0);
        Assert.That(creator.Status, Does.Contain("already there"));

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

        var description = Expect.Single(plugin.Descriptions);
        Assert.That((description.Id, description.VendorId, description.ProductId, description.UsagePage), Is.EqualTo(("button-box", 0x16C0, 0x05DF, 0xFF00)));
        Assert.That(description.Controls.Select(static control => $"{control.Id}:{control.Name}"), Is.EqualTo(new[] { "key.0:Key 2", "key.1:Key 1", "encoder.0:Knob 1" }));
        Assert.That(description.Controls[2].Input, Is.EqualTo(new HidInput(HidInputKind.Relative, 2, ReportId: 1)));
        Assert.That(description.Controls[2].Press, Is.EqualTo(new HidInput(HidInputKind.Bit, 3, Mask: 0x10, ReportId: 1)));
        Assert.That(Expect.Single(plugin.Devices).Id, Is.EqualTo("hid.button-box"));

        // Live: a press lights the key up.
        Send(1, 0x02, 0, 0);
        Assert.That(second.IsDown, Is.True);

        creator.Dispose();
        Assert.That(source.Report, Is.Null);
    }
}
