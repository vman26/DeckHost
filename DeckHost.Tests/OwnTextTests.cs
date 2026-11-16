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

    [Fact]
    public async Task An_action_that_ignores_the_text_gets_it_as_a_label()
    {
        var plain = await RenderAsync(new SolidAction("solid", Blue), text: null);
        var titled = await RenderAsync(new SolidAction("solid", Blue), text: "HELLO");

        Assert.False(HasWhite(plain));
        Assert.True(HasWhite(titled), "the host draws the text (white by default) over a picture without it");
    }

    [Fact]
    public async Task An_action_that_draws_the_text_itself_gets_no_second_copy()
    {
        var frame = await RenderAsync(new SelfTitledAction(), text: "HELLO");

        Assert.True(Has(frame, 0, 255, 0), "the action's own (green) text");
        Assert.False(HasWhite(frame), "no extra white label from the host");
    }

    [Fact]
    public void A_reading_on_a_key_shows_the_own_text_with_its_icon()
    {
        var context = new TextCapture();
        context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), "MY COUNT");

        context.DrawReading("MY COUNT", "7", null, 0xFF93C5FD, Icons.Counter);

        Assert.Contains("MY COUNT", context.Labels);
        Assert.Contains("7", context.Texts);
    }

    [Fact]
    public void A_reading_on_a_key_keeps_the_icon_alone_without_own_text()
    {
        var context = new TextCapture();

        context.DrawReading("Counter", "7", null, 0xFF93C5FD, Icons.Counter);

        Assert.DoesNotContain("Counter", context.Labels.Concat(context.Texts));
    }

    private static async Task<ControlFrame> RenderAsync(IDeckAction action, string? text)
    {
        var plugin = new StripDevicePlugin();
        using var store = new VariableStore();
        var frames = new ConcurrentDictionary<string, ControlFrame>();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin("p", action)], store);
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var binding = Bindings.Single(StripDevicePlugin.ZoneId, "p", action.Id) with { Appearance = new ControlAppearance(text) };
        host.SetProfiles([new DeviceProfile("one", "one", StripDevicePlugin.DeviceId, [binding])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return frames[StripDevicePlugin.ZoneId];
    }

    private static bool HasWhite(ControlFrame frame) => Has(frame, 255, 255, 255, tolerance: 40);

    private static bool Has(ControlFrame frame, int red, int green, int blue, int tolerance = 60)
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
