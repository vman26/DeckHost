using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DeckHost.App.Views;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;

namespace DeckHost.UiTests;

public sealed class DesktopDeckTests
{
    private const string DeviceId = "desk.test";
    private const string FirstKey = "key.0";

    [AvaloniaTest]
    public async Task ShowDeck_ClickOnAKey_PressesAndReleasesItOnTheDevice()
    {
        // Arrange
        using var store = new VariableStore();
        await using var host = new DeviceHostService([new TwoKeyDevice()], [], store);
        var inputs = new List<DeviceInputEvent>();
        host.InputReceived += input => { lock (inputs) { inputs.Add(input); } };
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        var window = new DesktopDeckWindow(host);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var key = window.GetVisualDescendants().OfType<Border>().First(static border => ToolTip.GetTip(border) as string == "Key 1");
        var point = key.TranslatePoint(new Point(3, 3), window)!.Value;

        // Act
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await host.WhenIdleAsync();

        // Assert
        lock (inputs)
        {
            Assert.That(inputs.Where(static input => input.ControlId == FirstKey).Select(static input => input.EventType),
                Is.EqualTo(new[] { InputCapabilities.Press, InputCapabilities.Release }));
        }

        window.Close();
    }

    private sealed class TwoKeyDevice : IDevicePlugin, IDeviceOutput
    {
        public string Id => "desk.plugin";

        public string Name => "Desk";

        public IReadOnlyCollection<DeviceDefinition> Devices { get; } =
        [
            new(DeviceId, "Desk deck",
            [
                new PhysicalControl(FirstKey, "Key 1", ControlType.Key, new DeckHost.Contracts.Models.Rect(0, 0, 72, 72), InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage, 72, 72),
                new PhysicalControl("key.1", "Key 2", ControlType.Key, new DeckHost.Contracts.Models.Rect(86, 0, 72, 72), InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage, 72, 72),
            ], IsConnected: false),
        ];

        public IDeviceOutput Output => this;

        public event EventHandler? DevicesChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
        {
            error = null;
            return true;
        }

        public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
        {
            error = null;
            return true;
        }

        public void Clear(string deviceId)
        {
        }

        public void Dispose()
        {
        }
    }
}
