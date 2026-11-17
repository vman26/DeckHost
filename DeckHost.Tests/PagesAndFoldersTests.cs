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
    private const string FirstKey = "key.0";
    private const string SecondKey = "key.1";
    private const string PageAction = "builtin.page";
    private const string SwitchProfileAction = "builtin.switch-profile";
    private const string ModeKey = "mode";
    private const string FirstProfile = "p1";
    private const string SecondPage = "p2";
    private const string ThirdPage = "p3";
    private const string Folder1 = "f1";
    private const string Folder2 = "f2";
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

    private ControlBinding NextPage() => Builtin(FirstKey, PageAction, (ModeKey, "next"));

    private async Task StartWithThreePages()
    {
        var next = NextPage();
        await Start(Profile(FirstProfile, next), Page(SecondPage, FirstProfile, next), Page(ThirdPage, FirstProfile, next), Profile("other"));
    }

    [Test]
    public async Task Press_NextPage_GoesToTheNextPage()
    {
        // Arrange
        await StartWithThreePages();

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo(SecondPage));
    }

    [Test]
    public async Task Press_NextPageOnTheLastPage_WrapsAroundToTheFirstPage()
    {
        // Arrange
        await StartWithThreePages();
        await Press(FirstKey);
        await Press(FirstKey);

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo(FirstProfile));
    }

    private async Task StartWithASpecificAndAPreviousPageKey() => await Start(
        Profile(FirstProfile, Builtin(FirstKey, PageAction, (ModeKey, "specific"), ("page", "3"))),
        Page(SecondPage, FirstProfile),
        Page(ThirdPage, FirstProfile, Builtin(FirstKey, PageAction, (ModeKey, "previous"))));

    [Test]
    public async Task Press_SpecificPageNumber_GoesToThatPage()
    {
        // Arrange
        await StartWithASpecificAndAPreviousPageKey();

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo(ThirdPage));
    }

    [Test]
    public async Task Press_PreviousPage_GoesToThePageBefore()
    {
        // Arrange
        await StartWithASpecificAndAPreviousPageKey();
        await Press(FirstKey);

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo(SecondPage));
    }

    [Test]
    public async Task SimulateInput_SwipeLeftOnAStripWithoutAnAction_GoesToTheNextPage()
    {
        // Arrange
        await Start(Profile(FirstProfile), Page(SecondPage, FirstProfile));

        // Act
        _host.SimulateInput(new DeviceInputEvent(Device, SecondKey, InputCapabilities.Swipe, -1));
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(_active, Is.EqualTo(SecondPage));
    }

    [Test]
    public async Task SimulateInput_SwipeRightOnAStripWithoutAnAction_GoesBackToThePreviousPage()
    {
        // Arrange
        await Start(Profile(FirstProfile), Page(SecondPage, FirstProfile));
        _host.SimulateInput(new DeviceInputEvent(Device, SecondKey, InputCapabilities.Swipe, -1));
        await _host.WhenIdleAsync();

        // Act
        _host.SimulateInput(new DeviceInputEvent(Device, SecondKey, InputCapabilities.Swipe, 1));
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(_active, Is.EqualTo(FirstProfile));
    }

    private async Task StartWithAFolderInAFolderOnThePageTwo()
    {
        await Start(
            Profile(FirstProfile),
            Page(SecondPage, FirstProfile, Builtin(SecondKey, FolderAction.ActionId, (FolderAction.FolderIdKey, Folder1))),
            Folder(Folder1, SecondPage, Builtin(FirstKey, FolderBackAction.ActionId), Builtin(SecondKey, FolderAction.ActionId, (FolderAction.FolderIdKey, Folder2))),
            Folder(Folder2, Folder1, Builtin(FirstKey, FolderBackAction.ActionId)));
        _host.SetActiveProfile(SecondPage);
        await _host.WhenIdleAsync();
    }

    [Test]
    public async Task Press_FolderKey_OpensTheFolder()
    {
        // Arrange
        await StartWithAFolderInAFolderOnThePageTwo();

        // Act
        await Press(SecondKey);

        // Assert
        Assert.That(_active, Is.EqualTo(Folder1));
    }

    [Test]
    public async Task Press_FolderKeyInsideAFolder_OpensTheFolderInTheFolder()
    {
        // Arrange
        await StartWithAFolderInAFolderOnThePageTwo();
        await Press(SecondKey);

        // Act
        await Press(SecondKey);

        // Assert
        Assert.That(_active, Is.EqualTo(Folder2));
    }

    [Test]
    public async Task Press_BackKeyInAFolderInAFolder_ReturnsToTheOuterFolder()
    {
        // Arrange
        await StartWithAFolderInAFolderOnThePageTwo();
        await Press(SecondKey);
        await Press(SecondKey);

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo(Folder1));
    }

    [Test]
    public async Task Press_BackKeyInAFolder_ReturnsToThePageItWasOpenedFrom()
    {
        // Arrange
        await StartWithAFolderInAFolderOnThePageTwo();
        await Press(SecondKey);

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo(SecondPage));
    }

    [Test]
    public async Task Press_NextPageInsideAFolder_GoesToThePageAfterTheFoldersPage()
    {
        // Arrange
        await Start(
            Profile(FirstProfile, Builtin(SecondKey, FolderAction.ActionId, (FolderAction.FolderIdKey, Folder1))),
            Page(SecondPage, FirstProfile),
            Folder(Folder1, FirstProfile, NextPage()));
        await Press(SecondKey);

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo(SecondPage));
    }

    private async Task StartWithPagesAndFoldersBetweenTwoProfiles()
    {
        var switchNext = Builtin(FirstKey, SwitchProfileAction, (ModeKey, "next"));
        await Start(Profile("a", switchNext), Page("a2", "a", switchNext), Folder("af", "a"), Profile("b", switchNext));
        _host.SetActiveProfile("a2");
        await _host.WhenIdleAsync();
    }

    [Test]
    public async Task Press_SwitchToTheNextProfile_SkipsPagesAndFolders()
    {
        // Arrange
        await StartWithPagesAndFoldersBetweenTwoProfiles();

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo("b"));
    }

    [Test]
    public async Task Press_SwitchToTheNextProfileFromTheLastOne_StartsTheFirstProfileOnItsFirstPage()
    {
        // Arrange
        await StartWithPagesAndFoldersBetweenTwoProfiles();
        await Press(FirstKey);

        // Act
        await Press(FirstKey);

        // Assert
        Assert.That(_active, Is.EqualTo("a"));
    }

    [Test]
    public void Deserialize_PageSavedWithItsKindAndParent_KeepsBoth()
    {
        // Arrange
        var json = System.Text.Json.JsonSerializer.Serialize(Page(SecondPage, FirstProfile));

        // Act
        var loaded = System.Text.Json.JsonSerializer.Deserialize<DeviceProfile>(json)!;

        // Assert
        Assert.That((loaded.Kind, loaded.ParentId), Is.EqualTo((ProfileKinds.Page, FirstProfile)));
        Assert.That(loaded.IsRoot, Is.False);
    }

    [Test]
    public void IsRoot_PlainProfile_IsTrue()
    {
        // Arrange
        var profile = Profile(FirstProfile);

        // Act
        var isRoot = profile.IsRoot;

        // Assert
        Assert.That(isRoot, Is.True);
    }

    [Test]
    public void ToSettings_ValueEditedInTheEditor_KeepsTheValuesItDoesNotShow()
    {
        // Arrange
        var folder = (FolderAction)_builtIns.Actions.First(action => action.Id == FolderAction.ActionId);
        var editor = new SettingsEditorViewModel(folder.Settings,
            new ActionSettings([new(FolderAction.FolderIdKey, Folder1), new(FolderAction.NameKey, "Tools"), new("plugin.private", "x")]),
            InputCapabilities.Press | InputCapabilities.Release);
        editor.Fields.First(field => field.Definition.Key == FolderAction.NameKey).Value = "Audio";

        // Act
        var saved = editor.ToSettings();

        // Assert
        Assert.That(saved.Get(FolderAction.FolderIdKey), Is.EqualTo(Folder1));
        Assert.That(saved.Get("plugin.private"), Is.EqualTo("x"));
        Assert.That(saved.Get(FolderAction.NameKey), Is.EqualTo("Audio"));
    }
}
