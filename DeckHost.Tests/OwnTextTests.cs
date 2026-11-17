using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;
using System.Collections.Concurrent;

namespace DeckHost.Tests;

/// <summary>The control's own text (Customize → Text) shows whatever the action draws.</summary>
public sealed class OwnTextTests
{
    private const uint Blue = 0xFF0000FF;
    private const uint Green = 0xFF00FF00;
    private const uint IconColor = 0xFF93C5FD;
    private const string PluginId = "p";
    private const string Hello = "HELLO";
    private const string OwnText = "MY COUNT";
    private const string Reading = "7";
    private const string ActionTitle = "Counter";
    private const int WhiteTolerance = 40;
    private const int ColorTolerance = 60;

    [Test]
    public async Task Render_ActionThatIgnoresTheTextAndNoText_DrawsNoWhite()
    {
        // Arrange
        var action = new SolidAction("solid", Blue);

        // Act
        var plain = await RenderAsync(action, text: null);

        // Assert
        Assert.That(HasWhite(plain), Is.False);
    }

    [Test]
    public async Task Render_ActionThatIgnoresTheText_GetsTheTextAsAWhiteLabel()
    {
        // Arrange
        var action = new SolidAction("solid", Blue);

        // Act
        var titled = await RenderAsync(action, text: Hello);

        // Assert
        Assert.That(HasWhite(titled), Is.True, "the host draws the text (white by default) over a picture without it");
    }

    [Test]
    public async Task Render_ActionThatDrawsTheTextItself_ShowsItsOwnGreenText()
    {
        // Arrange
        var action = new SelfTitledAction();

        // Act
        var frame = await RenderAsync(action, text: Hello);

        // Assert
        Assert.That(Has(frame, 0, 255, 0), Is.True, "the action's own (green) text");
    }

    [Test]
    public async Task Render_ActionThatDrawsTheTextItself_GetsNoSecondCopyFromTheHost()
    {
        // Arrange
        var action = new SelfTitledAction();

        // Act
        var frame = await RenderAsync(action, text: Hello);

        // Assert
        Assert.That(HasWhite(frame), Is.False, "no extra white label from the host");
    }

    [Test]
    public void DrawReading_KeyWithOwnText_ShowsTheOwnTextAsALabel()
    {
        // Arrange
        var context = new TextCapture();
        context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), OwnText);

        // Act
        context.DrawReading(OwnText, Reading, null, IconColor, Icons.Counter);

        // Assert
        Assert.That(context.Labels, Does.Contain(OwnText));
    }

    [Test]
    public void DrawReading_KeyWithOwnText_ShowsTheValue()
    {
        // Arrange
        var context = new TextCapture();
        context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), OwnText);

        // Act
        context.DrawReading(OwnText, Reading, null, IconColor, Icons.Counter);

        // Assert
        Assert.That(context.Texts, Does.Contain(Reading));
    }

    [Test]
    public void DrawReading_KeyWithoutOwnText_KeepsTheIconAlone()
    {
        // Arrange
        var context = new TextCapture();

        // Act
        context.DrawReading(ActionTitle, Reading, null, IconColor, Icons.Counter);

        // Assert
        Assert.That(context.Labels.Concat(context.Texts), Does.Not.Contain(ActionTitle));
    }

    private static async Task<ControlFrame> RenderAsync(IDeckAction action, string? text)
    {
        var plugin = new StripDevicePlugin();
        using var store = new VariableStore();
        var frames = new ConcurrentDictionary<string, ControlFrame>();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin(PluginId, action)], store);
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var binding = Bindings.Single(StripDevicePlugin.ZoneId, PluginId, action.Id) with { Appearance = new ControlAppearance(text) };
        host.SetProfiles([new DeviceProfile("one", "one", StripDevicePlugin.DeviceId, [binding])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return frames[StripDevicePlugin.ZoneId];
    }

    private static bool HasWhite(ControlFrame frame) => Has(frame, 255, 255, 255, tolerance: WhiteTolerance);

    private static bool Has(ControlFrame frame, int red, int green, int blue, int tolerance = ColorTolerance)
    {
        for (var index = 0; index < frame.Rgba.Length; index += 4)
        {
            if (Math.Abs(frame.Rgba[index] - red) <= tolerance && Math.Abs(frame.Rgba[index + 1] - green) <= tolerance && Math.Abs(frame.Rgba[index + 2] - blue) <= tolerance)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Fills blue and writes the control's own text in green itself.</summary>
    private sealed class SelfTitledAction : IDeckAction
    {
        public string Id => "self";

        public string Name => "Self";

        public InputCapabilities RequiredInputs => InputCapabilities.None;

        public OutputCapabilities RequiredOutputs => OutputCapabilities.None;

        public void Execute(ActionContext context)
        {
        }

        public void Render(IRenderContext context)
        {
            context.Clear(Blue);
            context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), out var text);
            context.DrawCenteredText(text ?? string.Empty, context.Width / 2d, context.Height / 2d, 40, Green);
        }
    }

    private sealed class TextCapture : IRenderContext
    {
        public List<string> Texts { get; } = [];

        public List<string> Labels { get; } = [];

        public string DeviceId => "dev";

        public string ControlId => "key.0";

        public int Width => 72;

        public int Height => 72;

        public IVariableStore Variables { get; } = new VariableStore();

        public ActionSettings Settings => ActionSettings.Empty;

        public string ProfileId => "p";

        public bool DrawEncodedImage(byte[] encodedImage) => true;

        public void DrawLabel(string text, TextVerticalAlignment defaultVertical = TextVerticalAlignment.Middle, float? defaultSize = null, uint defaultColor = 0xFFFFFFFF) => Labels.Add(text);

        public void Clear(uint argbColor)
        {
        }

        public void DrawText(string text, double x, double y, float size, uint argbColor = 0xFFFFFFFF) => Texts.Add(text);

        public void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF) => Texts.Add(text);

        public void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor)
        {
        }

        public void DrawImage(byte[] rgbaPixels, int width, int height)
        {
        }

        public bool DrawImageFile(string path) => true;
    }
}
