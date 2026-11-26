using System.Text.RegularExpressions;
using System.Xml.Linq;
using DeckHost.App.ViewModels;

namespace DeckHost.Tests;

/// <summary>Screen readers and the keyboard: what the views must give them.</summary>
public sealed partial class AccessibilityTests
{
    private static readonly string[] ButtonTags = ["Button", "ToggleButton", "RepeatButton", "SplitButton", "DropDownButton"];

    private static string ViewsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeckHost.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "DeckHost.App");
    }

    [GeneratedRegex("[\\p{L}\\p{N}]")]
    private static partial Regex Readable();

    [Test]
    public void Views_EveryButtonWithoutWords_HasANameOrATooltip()
    {
        // Arrange
        var files = Directory.GetFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories);
        var missing = new List<string>();

        // Act
        foreach (var file in files)
        {
            foreach (var button in XDocument.Load(file).Descendants().Where(static element => ButtonTags.Contains(element.Name.LocalName)))
            {
                var attributes = button.DescendantsAndSelf().SelectMany(static element => element.Attributes())
                    .Where(static attribute => attribute.Name.LocalName is "Content" or "Text" or "Header")
                    .Select(static attribute => attribute.Value);
                var words = string.Join(' ', attributes.Append(button.Value));
                var named = button.Attributes().Any(static attribute => attribute.Name.LocalName is "AutomationProperties.Name" or "ToolTip.Tip");
                if (!named && !Readable().IsMatch(words))
                {
                    missing.Add($"{Path.GetFileName(file)}:{((System.Xml.IXmlLineInfo)button).LineNumber} ({words.Trim()})");
                }
            }
        }

        // Assert
        Assert.That(missing, Is.Empty, "Give these buttons AutomationProperties.Name or a ToolTip.Tip: " + string.Join(", ", missing));
    }

    [Test]
    public void Next_RightFromTheFirstKeyOfARow_GoesToTheSecondKey()
    {
        // Arrange
        Avalonia.Rect[] keys = [new(0, 0, 10, 10), new(20, 0, 10, 10), new(0, 20, 10, 10), new(20, 20, 10, 10)];

        // Act
        var next = CanvasNavigation.Next(keys, 0, CanvasDirection.Right);

        // Assert
        Assert.That(next, Is.EqualTo(1));
    }

    [Test]
    public void Next_DownFromAKey_StaysInItsColumn()
    {
        // Arrange
        Avalonia.Rect[] keys = [new(0, 0, 10, 10), new(20, 0, 10, 10), new(0, 20, 10, 10), new(20, 20, 10, 10)];

        // Act
        var next = CanvasNavigation.Next(keys, 1, CanvasDirection.Down);

        // Assert
        Assert.That(next, Is.EqualTo(3));
    }

    [Test]
    public void Next_LeftFromTheFirstKey_HasNowhereToGo()
    {
        // Arrange
        Avalonia.Rect[] keys = [new(0, 0, 10, 10), new(20, 0, 10, 10)];

        // Act
        var next = CanvasNavigation.Next(keys, 0, CanvasDirection.Left);

        // Assert
        Assert.That(next, Is.Null);
    }

    [Test]
    public void IsReadable_ASymbol_IsNotWords()
    {
        // Arrange
        const string Symbol = "✕";

        // Act
        var readable = DeckHost.App.Views.AccessibleNames.IsReadable(Symbol);

        // Assert
        Assert.That(readable, Is.False);
    }
}
