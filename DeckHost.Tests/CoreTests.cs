using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core;
using DeckHost.Core.Models;
using DeckHost.Core.Profiles;
using DeckHost.Core.State;

namespace DeckHost.Tests;

public class VariableStoreTests
{
    private const string Key = "a";
    private const int Answer = 42;
    private const int RaceRounds = 200;
    private const int LastValue = 99;

    [Test]
    public void TryGetValue_AfterPublish_ReturnsTheValue()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish(Key, Answer);

        // Act
        var found = store.TryGetValue<int>(Key, out var value);

        // Assert
        Assert.That(found, Is.True);
        Assert.That(value, Is.EqualTo(Answer));
    }

    [Test]
    public void TryGetValue_AskedForAnotherType_ReturnsFalse()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish(Key, Answer);

        // Act
        var found = store.TryGetValue<string>(Key, out _);

        // Assert
        Assert.That(found, Is.False);
    }

    [Test]
    public void Observe_SubscribedBeforeTheFirstPublish_ReceivesEveryValue()
    {
        // Arrange
        using var store = new VariableStore();
        var received = new List<int>();
        using var subscription = store.Observe<int>(Key).Subscribe(received.Add);

        // Act
        store.Publish(Key, 1);
        store.Publish(Key, 2);

        // Assert
        Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    [Repeat(RaceRounds)]
    public async Task Observe_RacingWithTheFirstPublish_NeverLosesTheSubscriber()
    {
        // Arrange
        using var store = new VariableStore();
        var key = $"k{Guid.NewGuid():N}";
        var last = -1;
        var start = new ManualResetEventSlim();
        var observe = Task.Run(() =>
        {
            start.Wait();
            return store.Observe<int>(key).Subscribe(value => Volatile.Write(ref last, value));
        });
        var publish = Task.Run(() =>
        {
            start.Wait();
            store.Publish(key, 1);
        });

        // Act
        start.Set();
        using var subscription = await observe;
        await publish;
        store.Publish(key, LastValue);

        // Assert
        Assert.That(Volatile.Read(ref last), Is.EqualTo(LastValue));
    }
}

public class ActionRouterTests
{
    private const string DeviceId = "dev";
    private const string PluginId = "p";
    private const string ActionId = "a";
    private const string Key = "key.0";
    private const string ProfileId = "profile";
    private const string LastEventKey = "devices.dev.key.0.lastEvent";

    private static DeviceProfile ProfileWith(params ControlBinding[] bindings) => new(ProfileId, "Profile", DeviceId, bindings);

    private static DeviceInputEvent Press() => new(DeviceId, Key, InputCapabilities.Press, 1);

    [Test]
    public void Dispatch_BoundAction_ReturnsTrueAndRunsTheActionOnce()
    {
        // Arrange
        using var store = new VariableStore();
        var action = new RecordingAction(ActionId);
        var router = new ActionRouter(store, [new TestApplicationPlugin(PluginId, action)]);

        // Act
        var dispatched = router.Dispatch(ProfileWith(Bindings.Single(Key, PluginId, ActionId)), Press(), NullActionHost.Instance);

        // Assert
        Assert.That(dispatched, Is.True);
        Assert.That(Expect.Single(action.Executions).EventType, Is.EqualTo(InputCapabilities.Press));
    }

    [Test]
    public void Dispatch_BoundAction_PublishesTheInputState()
    {
        // Arrange
        using var store = new VariableStore();
        var router = new ActionRouter(store, [new TestApplicationPlugin(PluginId, new RecordingAction(ActionId))]);

        // Act
        router.Dispatch(ProfileWith(Bindings.Single(Key, PluginId, ActionId)), Press(), NullActionHost.Instance);

        // Assert
        Assert.That(store.TryGetValue<InputCapabilities>(LastEventKey, out var lastEvent), Is.True);
        Assert.That(lastEvent, Is.EqualTo(InputCapabilities.Press));
    }

    [Test]
    public void Dispatch_ActionThatDoesNotAcceptTheEvent_ReturnsFalse()
    {
        // Arrange
        using var store = new VariableStore();
        var action = new RecordingAction(ActionId, InputCapabilities.Rotate);
        var router = new ActionRouter(store, [new TestApplicationPlugin(PluginId, action)]);

        // Act
        var dispatched = router.Dispatch(ProfileWith(Bindings.Single(Key, PluginId, ActionId)), Press(), NullActionHost.Instance);

        // Assert
        Assert.That(dispatched, Is.False);
    }

    [Test]
    public void Dispatch_ActionThatDoesNotAcceptTheEvent_DoesNotRunIt()
    {
        // Arrange
        using var store = new VariableStore();
        var action = new RecordingAction(ActionId, InputCapabilities.Rotate);
        var router = new ActionRouter(store, [new TestApplicationPlugin(PluginId, action)]);

        // Act
        router.Dispatch(ProfileWith(Bindings.Single(Key, PluginId, ActionId)), Press(), NullActionHost.Instance);

        // Assert
        Assert.That(action.Executions, Is.Empty);
    }

    private sealed record MultiActionRun(ActionRouter Router, DeviceProfile Profile, List<string> Order, List<string> Failures);

    private static MultiActionRun MultiActionWithAFailingMiddleStep(VariableStore store)
    {
        const string multi = "multi";
        const string first = "first";
        const string failing = "failing";
        const string last = "last";
        var order = new List<string>();
        var failures = new List<string>();
        var plugin = new TestApplicationPlugin(PluginId,
            new RecordingAction(multi),
            new RecordingAction(first) { OnExecute = _ => order.Add(first) },
            new RecordingAction(failing) { OnExecute = _ => throw new InvalidOperationException("boom") },
            new RecordingAction(last) { OnExecute = _ => order.Add(last) });
        var router = new ActionRouter(store, [plugin], (reference, _) => failures.Add(reference.ActionId));
        var binding = new ControlBinding(
            Key,
            ProfileActionKind.MultiAction,
            Bindings.Reference(PluginId, multi),
            new ControlAppearance(),
            [
                new MultiActionStep(2, Bindings.Reference(PluginId, last)),
                new MultiActionStep(1, Bindings.Reference(PluginId, failing)),
                new MultiActionStep(0, Bindings.Reference(PluginId, first)),
            ]);
        return new MultiActionRun(router, ProfileWith(binding), order, failures);
    }

    [Test]
    public void Dispatch_MultiActionWithAFailingStep_RunsTheOtherStepsInOrder()
    {
        // Arrange
        using var store = new VariableStore();
        var run = MultiActionWithAFailingMiddleStep(store);

        // Act
        var dispatched = run.Router.Dispatch(run.Profile, Press(), NullActionHost.Instance);

        // Assert
        Assert.That(dispatched, Is.True);
        Assert.That(run.Order, Is.EqualTo(new[] { "first", "last" }));
    }

    [Test]
    public void Dispatch_MultiActionWithAFailingStep_ReportsTheFailure()
    {
        // Arrange
        using var store = new VariableStore();
        var run = MultiActionWithAFailingMiddleStep(store);

        // Act
        run.Router.Dispatch(run.Profile, Press(), NullActionHost.Instance);

        // Assert
        Assert.That(run.Failures, Is.EqualTo(new[] { "failing" }));
    }

    [Test]
    public void DuplicateActionKeys_TwoPluginsWithTheSameActionId_AreReportedInsteadOfThrowing()
    {
        // Arrange
        using var store = new VariableStore();

        // Act
        var router = new ActionRouter(store, [new TestApplicationPlugin(PluginId, new RecordingAction(ActionId)), new TestApplicationPlugin(PluginId, new RecordingAction(ActionId))]);

        // Assert
        Assert.That(router.DuplicateActionKeys, Is.EqualTo(new[] { $"{PluginId}:{ActionId}" }));
    }
}

public class ProfileStoreTests : IDisposable
{
    private const string ProfileId = "profile";
    private const string DeviceId = "dev";
    private const string DefaultColor = "#0F766E";
    private const string DefaultImage = "/images/wall.png";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}");
    private string ProfilesFile => Path.Combine(_folder, "profiles.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static ControlBinding MultiActionBinding() => new(
        "key.3",
        ProfileActionKind.MultiAction,
        Bindings.Reference("p", "multi"),
        new ControlAppearance("Hello", "C:/image.png", new RgbFeedback("FF0000", "000000", 150, true)),
        [new MultiActionStep(0, Bindings.Reference("p", "a"))]);

    private static ProfileDocument DocumentWith(ControlBinding binding, ProfileBackground? defaultBackground = null) =>
        new(ProfileDocument.CurrentVersion, ProfileId, [new DeviceProfile(ProfileId, "Profile", DeviceId, [binding])], DefaultBackground: defaultBackground);

    [Test]
    public void Load_AfterSave_ReturnsTheSelectedProfile()
    {
        // Arrange
        var store = new ProfileStore(ProfilesFile);
        store.Save(DocumentWith(MultiActionBinding()));

        // Act
        var loaded = store.Load();

        // Assert
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.SelectedProfileId, Is.EqualTo(ProfileId));
    }

    [Test]
    public void Load_AfterSave_ReturnsTheBindingWithItsAppearanceAndSteps()
    {
        // Arrange
        var binding = MultiActionBinding();
        var store = new ProfileStore(ProfilesFile);
        store.Save(DocumentWith(binding));

        // Act
        var loaded = store.Load()!;

        // Assert
        var loadedBinding = Expect.Single(Expect.Single(loaded.Profiles).Bindings);
        Assert.That(loadedBinding.ControlId, Is.EqualTo(binding.ControlId));
        Assert.That(loadedBinding.ActionKind, Is.EqualTo(binding.ActionKind));
        Assert.That(loadedBinding.Appearance, Is.EqualTo(binding.Appearance));
        Assert.That(loadedBinding.Steps[0], Is.EqualTo(binding.Steps[0]));
    }

    [Test]
    public void Save_Profiles_LeavesNoTemporaryFileBehind()
    {
        // Arrange
        var store = new ProfileStore(ProfilesFile);

        // Act
        store.Save(DocumentWith(MultiActionBinding()));

        // Assert
        Assert.That(File.Exists(ProfilesFile + ".tmp"), Is.False);
    }

    [Test]
    public void Load_AfterSavingADefaultBackground_ReturnsIt()
    {
        // Arrange
        var store = new ProfileStore(ProfilesFile);
        var binding = new ControlBinding("key.0", ProfileActionKind.Single, Bindings.Reference("p", "a"), new ControlAppearance(), []);
        store.Save(DocumentWith(binding, new ProfileBackground(DefaultColor, DefaultImage)));

        // Act
        var loaded = store.Load()!;

        // Assert
        Assert.That(loaded.DefaultBackground, Is.EqualTo(new ProfileBackground(DefaultColor, DefaultImage)));
    }

    [Test]
    public void Load_AfterSavingNewAppearanceFields_ReturnsThemUnchanged()
    {
        // Arrange
        var store = new ProfileStore(ProfilesFile);
        var appearance = new ControlAppearance(BackgroundColor: "#80112233") { IconPath = "/icons/mic.svg", ShowTile = true, Tile = new TileStyle("#FF0000", "#0000FF", 30) };
        var binding = new ControlBinding("key.0", ProfileActionKind.Single, Bindings.Reference("p", "a"), appearance, []);
        store.Save(DocumentWith(binding));

        // Act
        var loaded = store.Load()!;

        // Assert
        Assert.That(Expect.Single(Expect.Single(loaded.Profiles).Bindings).Appearance, Is.EqualTo(appearance));
    }

    [Test]
    public void Load_NoFile_ReturnsNull()
    {
        // Arrange
        var store = new ProfileStore(Path.Combine(_folder, "missing.json"));

        // Act
        var loaded = store.Load();

        // Assert
        Assert.That(loaded, Is.Null);
    }
}
