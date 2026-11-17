
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Contracts.Interfaces;

namespace DeckHost.Tests;

/// <summary>Dimming and explaining the actions that don't fit the selected control.</summary>
public sealed class ActionFitTests
{
    private static readonly PhysicalControl Pushbutton = new("button.0", "Button 1", ControlType.Button, new Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.None);

    private static readonly PhysicalControl KeyWithDisplay = new("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage | OutputCapabilities.Text);

    private static ActionItemViewModel KeyImageAction() => ActionItemViewModel.Create("p", "P", new ImageAction());

    [Test]
    public void UpdateFit_NothingSelected_EveryActionIsCompatible()
    {
        var action = KeyImageAction();

        action.UpdateFit(null);

        Assert.That(action.IsCompatible, Is.True);
        Assert.That(action.ListOpacity, Is.EqualTo(1));
    }

    [Test]
    public void UpdateFit_ControlWithoutADisplay_DimsAnActionThatNeedsOne()
    {
        var action = KeyImageAction();

        action.UpdateFit(Pushbutton);

        Assert.That(action.IsCompatible, Is.False);
        Assert.That(action.ListOpacity, Is.LessThan(1));
    }

    [Test]
    public void UpdateFit_ControlWithoutADisplay_ExplainsWhyInTheToolTip()
    {
        var action = KeyImageAction();

        action.UpdateFit(Pushbutton);

        Assert.That(action.ListToolTip, Does.Contain("display"));
    }

    [Test]
    public void UpdateFit_ControlThatFits_KeepsTheActionBright()
    {
        var action = KeyImageAction();
        action.UpdateFit(Pushbutton);

        action.UpdateFit(KeyWithDisplay);

        Assert.That(action.IsCompatible, Is.True);
        Assert.That(action.ListToolTip, Is.EqualTo(action.Summary));
    }

    private sealed class ImageAction : IDeckAction
    {
        public string Id => "image";

        public string Name => "Image";

        public InputCapabilities RequiredInputs => InputCapabilities.None;

        public OutputCapabilities RequiredOutputs => OutputCapabilities.ColorImage;

        public void Execute(ActionContext context)
        {
        }

        public void Render(IRenderContext context)
        {
        }
    }
}
