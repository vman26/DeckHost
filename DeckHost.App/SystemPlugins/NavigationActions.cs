using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Collections.Generic;
using System.Globalization;

namespace DeckHost.App.SystemPlugins;

/// <summary>
/// Flips through the pages of the profile: the next, the previous or a specific page. On a knob
/// each input can do something else (turned left the previous page, turned right the next).
/// </summary>
internal sealed class PageAction() : DeckActionBase(
    "builtin.page",
    "Pages",
    InputCapabilities.None,
    OutputCapabilities.None,
    "Shows another page of this profile: the next, the previous or a specific one. On a knob, turning flips through the pages. Add pages with + under the device.")
{
    private const string ModeKey = "mode";
    private const string PageKey = "page";
    private const string Next = "next";
    private const string Previous = "previous";
    private const string Specific = "specific";
    private const string Nothing = "none";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(ModeKey, "Go to", ActionSettingKind.Choice, Next, Options:
        [
            new(Next, "Next page"),
            new(Previous, "Previous page"),
            new(Specific, "A specific page"),
            new(Nothing, "Do nothing"),
        ])
        {
            PerInput = true,
            InputDefaults = new Dictionary<string, string>
            {
                [ControlInputs.LongPress] = Nothing,
                [ControlInputs.RotateLeft] = Previous,
                [ControlInputs.RotateRight] = Next,
                [ControlInputs.SwipeLeft] = Next,
                [ControlInputs.SwipeRight] = Previous,
            },
        },
        new(PageKey, "Page", ActionSettingKind.Number, "1")
        {
            PerInput = true,
            ShowWhen = new(ModeKey, Specific),
            Range = new(1, 20),
        },
    ];

    protected override void Run(ActionContext context)
    {
        switch (context.Settings.Get(ModeKey, Next))
        {
            case Nothing:
                break;
            case Previous:
                context.Host.ActivateRelativePage(context.DeviceId, -1);
                break;
            case Specific:
                context.Host.ActivatePage(context.DeviceId, context.Settings.GetInt(PageKey, 1));
                break;
            default:
                context.Host.ActivateRelativePage(context.DeviceId, 1);
                break;
        }
    }

    protected override string Title(IRenderContext context)
    {
        var settings = context.Settings.ForInput(Settings, ControlInputs.Press);
        return settings.Get(ModeKey, Next) switch
        {
            Previous => "Prev Page",
            Specific => "Page " + settings.GetInt(PageKey, 1).ToString(CultureInfo.CurrentCulture),
            Nothing => "Pages",
            _ => "Next Page",
        };
    }

    protected override string? Subtitle(IRenderContext context) => null;

    protected override string? Icon(IRenderContext context) =>
        context.Settings.ForInput(Settings, ControlInputs.Press).Get(ModeKey, Next) == Previous ? Icons.PagePrevious : Icons.PageNext;

    protected override (uint From, uint To) TileColors => Palette.Profiles;

    protected override string? Caption(IRenderContext context) => Title(context);
}

/// <summary>
/// A key that opens a layout of its own (a folder), which has a Back key. The folder is made when
/// the action is placed on a key; its id is kept in the binding's settings.
/// </summary>
internal sealed class FolderAction() : DeckActionBase(
    "builtin.folder",
    "Folder",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "A key that opens a layout of its own, with a Back key to return. Double-click it (or use Open folder below) to fill it.")
{
    public const string ActionId = "builtin.folder";

    /// <summary>The setting that holds the folder's layout id (set by the editor, not shown).</summary>
    public const string FolderIdKey = "folder";

    public const string NameKey = "name";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(NameKey, "Folder name", ActionSettingKind.Text, "Folder", "Shown on the key and in the editor."),
    ];

    public override bool CanBeMultiActionStep => false;

    protected override void Run(ActionContext context)
    {
        if (context.Settings.Get(FolderIdKey) is { Length: > 0 } folderId)
        {
            context.Host.OpenFolder(context.DeviceId, folderId);
        }
    }

    protected override string Title(IRenderContext context) => context.Settings.Get(NameKey, "Folder");

    protected override string? Subtitle(IRenderContext context) => null;

    protected override string? Icon(IRenderContext context) => Icons.Folder;

    protected override (uint From, uint To) TileColors => Palette.Keys;

    protected override string? Caption(IRenderContext context) => Title(context);
}

/// <summary>Leaves the folder it's in: back to the layout whose key opened the folder.</summary>
internal sealed class FolderBackAction() : DeckActionBase(
    "builtin.folder-back",
    "Back",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Leaves the folder: back to the page or folder it was opened from. Every new folder gets one in its first key.")
{
    public const string ActionId = "builtin.folder-back";

    public override bool CanBeMultiActionStep => false;

    protected override void Run(ActionContext context) => context.Host.CloseFolder(context.DeviceId);

    protected override string? Subtitle(IRenderContext context) => null;

    protected override string? Icon(IRenderContext context) => Icons.Back;

    protected override (uint From, uint To) TileColors => Palette.Security;
}
