using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace DeckHost.App.Views;

/// <summary>
/// Screen readers read a button's content as its name, which is nothing useful for an icon or a
/// symbol (✕, ☰, ⟲). When such a button has no <see cref="AutomationProperties.NameProperty"/>
/// of its own, its tooltip becomes its name. <c>AccessibilityTests</c> checks that every icon
/// button in the views has one or the other.
/// </summary>
public static class AccessibleNames
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Control.LoadedEvent.AddClassHandler<Button>(static (button, _) => NameFromToolTip(button), RoutingStrategies.Direct);
        Control.LoadedEvent.AddClassHandler<ToggleButton>(static (button, _) => NameFromToolTip(button), RoutingStrategies.Direct);
    }

    /// <summary>Gives <paramref name="control"/> its tooltip as its name when its content says nothing readable.</summary>
    public static void NameFromToolTip(ContentControl control)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) || ToolTip.GetTip(control) is not string tip || string.IsNullOrWhiteSpace(tip))
        {
            return;
        }

        if (!IsReadable(control.Content))
        {
            AutomationProperties.SetName(control, tip);
        }
    }

    /// <summary>Whether content reads as words: text with a letter or digit, or a text block with one.</summary>
    public static bool IsReadable(object? content) => content switch
    {
        string text => text.Any(char.IsLetterOrDigit),
        TextBlock block => block.Text?.Any(char.IsLetterOrDigit) == true,
        Panel panel => panel.Children.Any(static child => IsReadable(child)),
        ContentControl inner => IsReadable(inner.Content),
        _ => false,
    };
}
