using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;

namespace DeckHost.Tests;

public sealed class PagesAndFoldersTests : IAsyncDisposable
{
    private const string Device = FakeDevicePlugin.DeviceId;
    private readonly VariableStore _store = new();
    private readonly DeviceHostService _host;
    private readonly IApplicationPlugin _builtIns = SystemPluginSet.Create().First(plugin => plugin.Id == SystemPluginSet.BuiltInPluginId);
    private string? _active;

    public PagesAndFoldersTests()
    {
        _host = new DeviceHostService([new FakeDevicePlugin()], [_builtIns], _store);
        _host.ActiveProfileChanged += (_, profileId) => _active = profileId;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _store.Dispose();
    }

    private ControlBinding Builtin(string controlId, string actionId, params (string Key, string Value)[] settings) =>
        new(controlId, ProfileActionKind.Single,
            new ActionReference(_builtIns.Id, _builtIns.Name, actionId, actionId, InputCapabilities.None, OutputCapabilities.None,
                new ActionSettings(settings.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value)))),
            new ControlAppearance(), []);

    private async Task Press(string controlId)
    {
        _host.SimulateInput(new DeviceInputEvent(Device, controlId, InputCapabilities.Press, 1));
        await _host.WhenIdleAsync();
        await _host.WhenIdleAsync(); // the action asks the host to switch: one more round
    }

    private async Task Start(params DeviceProfile[] profiles)
    {
        _host.SetProfiles(profiles);
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();
    }

    private DeviceProfile Profile(string id, params ControlBinding[] bindings) => new(id, id, Device, bindings);

    private DeviceProfile Page(string id, string parent, params ControlBinding[] bindings) =>
        new DeviceProfile(id, id, Device, bindings) { ParentId = parent, Kind = ProfileKinds.Page };

    private DeviceProfile Folder(string id, string parent, params ControlBinding[] bindings) =>
        new DeviceProfile(id, id, Device, bindings) { ParentId = parent, Kind = ProfileKinds.Folder };

    [Fact]
    public async Task The_pages_action_flips_through_the_pages_and_wraps_around()
    {
        var next = Builtin("key.0", "builtin.page", ("mode", "next"));
        await Start(Profile("p1", next), Page("p2", "p1", next), Page("p3", "p1", next), Profile("other"));

        await Press("key.0");
        Assert.Equal("p2", _active);
        await Press("key.0");
        Assert.Equal("p3", _active);
        await Press("key.0");
        Assert.Equal("p1", _active);
    }

    [Fact]
    public async Task A_specific_page_and_the_previous_page()
    {
        await Start(
            Profile("p1", Builtin("key.0", "builtin.page", ("mode", "specific"), ("page", "3"))),
            Page("p2", "p1"),
            Page("p3", "p1", Builtin("key.0", "builtin.page", ("mode", "previous"))));

        await Press("key.0");
        Assert.Equal("p3", _active);
        await Press("key.0");
        Assert.Equal("p2", _active);
    }

    [Fact]
    public async Task Swiping_a_strip_without_an_action_flips_pages_like_a_phone()
    {
        await Start(Profile("p1"), Page("p2", "p1"));

        _host.SimulateInput(new DeviceInputEvent(Device, "key.1", InputCapabilities.Swipe, -1));
        await _host.WhenIdleAsync();
        Assert.Equal("p2", _active);

        _host.SimulateInput(new DeviceInputEvent(Device, "key.1", InputCapabilities.Swipe, 1));
        await _host.WhenIdleAsync();
        Assert.Equal("p1", _active);
    }

    [Fact]
    public async Task A_folder_opens_and_its_back_key_returns_to_the_page_it_was_opened_from()
    {
        await Start(
            Profile("p1"),
            Page("p2", "p1", Builtin("key.1", FolderAction.ActionId, (FolderAction.FolderIdKey, "f1"))),
            Folder("f1", "p2", Builtin("key.0", FolderBackAction.ActionId), Builtin("key.1", FolderAction.ActionId, (FolderAction.FolderIdKey, "f2"))),
            Folder("f2", "f1", Builtin("key.0", FolderBackAction.ActionId)));
        _host.SetActiveProfile("p2");
        await _host.WhenIdleAsync();

        await Press("key.1");
        Assert.Equal("f1", _active);
        await Press("key.1");
        Assert.Equal("f2", _active); // a folder in a folder
        await Press("key.0");
        Assert.Equal("f1", _active);
        await Press("key.0");
        Assert.Equal("p2", _active);
    }

    [Fact]
    public async Task Next_page_inside_a_folder_goes_to_the_page_after_the_folders_page()
    {
        await Start(
            Profile("p1", Builtin("key.1", FolderAction.ActionId, (FolderAction.FolderIdKey, "f1"))),
            Page("p2", "p1"),
            Folder("f1", "p1", Builtin("key.0", "builtin.page", ("mode", "next"))));
        await Press("key.1");
        Assert.Equal("f1", _active);

        await Press("key.0");
        Assert.Equal("p2", _active);
    }

    [Fact]
    public async Task Switching_profiles_skips_pages_and_folders()
    {
        var switchNext = Builtin("key.0", "builtin.switch-profile", ("mode", "next"));
        await Start(Profile("a", switchNext), Page("a2", "a", switchNext), Folder("af", "a"), Profile("b", switchNext));
        _host.SetActiveProfile("a2");
        await _host.WhenIdleAsync();

        await Press("key.0");
        Assert.Equal("b", _active);
        await Press("key.0");
        Assert.Equal("a", _active); // a profile starts on its first page
    }

    [Fact]
    public void Layouts_keep_their_kind_and_parent_when_saved()
    {
        var page = Page("p2", "p1");
        var json = System.Text.Json.JsonSerializer.Serialize(page);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<DeviceProfile>(json)!;

        Assert.Equal((ProfileKinds.Page, "p1"), (loaded.Kind, loaded.ParentId));
        Assert.False(loaded.IsRoot);
        Assert.True(Profile("p1").IsRoot);
    }

    [Fact]
    public void The_settings_editor_keeps_values_it_doesnt_show()
    {
        var folder = (FolderAction)_builtIns.Actions.First(action => action.Id == FolderAction.ActionId);
        var editor = new SettingsEditorViewModel(folder.Settings,
            new ActionSettings([new(FolderAction.FolderIdKey, "f1"), new(FolderAction.NameKey, "Tools"), new("plugin.private", "x")]),
            InputCapabilities.Press | InputCapabilities.Release);

        editor.Fields.First(field => field.Definition.Key == FolderAction.NameKey).Value = "Audio";
        var saved = editor.ToSettings();

        Assert.Equal("f1", saved.Get(FolderAction.FolderIdKey));
        Assert.Equal("x", saved.Get("plugin.private"));
        Assert.Equal("Audio", saved.Get(FolderAction.NameKey));
    }
}
