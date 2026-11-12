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
    [Fact]
    public void Publish_then_TryGetValue_returns_value()
    {
        using var store = new VariableStore();
        store.Publish("a", 42);

        Assert.True(store.TryGetValue<int>("a", out var value));
        Assert.Equal(42, value);
        Assert.False(store.TryGetValue<string>("a", out _));
    }

    [Fact]
    public void Observe_before_first_publish_receives_values()
    {
        using var store = new VariableStore();
        var received = new List<int>();
        using var subscription = store.Observe<int>("a").Subscribe(received.Add);

        store.Publish("a", 1);
        store.Publish("a", 2);

        Assert.Equal([1, 2], received);
    }

    [Fact]
    public async Task Concurrent_observe_and_publish_never_loses_the_subscriber()
    {
        for (var round = 0; round < 200; round++)
        {
            using var store = new VariableStore();
            var key = $"k{round}";
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

            start.Set();
            using var subscription = await observe;
            await publish;
            store.Publish(key, 99);

            Assert.Equal(99, Volatile.Read(ref last));
        }
    }
}

public class ActionRouterTests
{
    private const string DeviceId = "dev";

    [Fact]
    public void Dispatch_runs_bound_action_and_publishes_input_state()
    {
        using var store = new VariableStore();
        var action = new RecordingAction("a");
        var router = new ActionRouter(store, [new TestApplicationPlugin("p", action)]);
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [Bindings.Single("key.0", "p", "a")]);

        var dispatched = router.Dispatch(profile, new DeviceInputEvent(DeviceId, "key.0", InputCapabilities.Press, 1), NullActionHost.Instance);

        Assert.True(dispatched);
        var context = Assert.Single(action.Executions);
        Assert.Equal(InputCapabilities.Press, context.EventType);
        Assert.True(store.TryGetValue<InputCapabilities>("devices.dev.key.0.lastEvent", out var lastEvent));
        Assert.Equal(InputCapabilities.Press, lastEvent);
    }

    [Fact]
    public void Dispatch_skips_actions_that_do_not_accept_the_event()
    {
        using var store = new VariableStore();
        var action = new RecordingAction("a", InputCapabilities.Rotate);
        var router = new ActionRouter(store, [new TestApplicationPlugin("p", action)]);
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [Bindings.Single("key.0", "p", "a")]);

        Assert.False(router.Dispatch(profile, new DeviceInputEvent(DeviceId, "key.0", InputCapabilities.Press, 1), NullActionHost.Instance));
        Assert.Empty(action.Executions);
    }

    [Fact]
    public void MultiAction_runs_steps_in_order_and_survives_a_failing_step()
    {
        using var store = new VariableStore();
        var order = new List<string>();
        var first = new RecordingAction("first") { OnExecute = _ => order.Add("first") };
        var failing = new RecordingAction("failing") { OnExecute = _ => throw new InvalidOperationException("boom") };
        var last = new RecordingAction("last") { OnExecute = _ => order.Add("last") };
        var failures = new List<string>();
        var router = new ActionRouter(store, [new TestApplicationPlugin("p", new RecordingAction("multi"), first, failing, last)], (reference, _) => failures.Add(reference.ActionId));

        var binding = new ControlBinding(
            "key.0",
            ProfileActionKind.MultiAction,
            Bindings.Reference("p", "multi"),
            new ControlAppearance(),
            [
                new MultiActionStep(2, Bindings.Reference("p", "last")),
                new MultiActionStep(1, Bindings.Reference("p", "failing")),
                new MultiActionStep(0, Bindings.Reference("p", "first")),
            ]);
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [binding]);

        Assert.True(router.Dispatch(profile, new DeviceInputEvent(DeviceId, "key.0", InputCapabilities.Press, 1), NullActionHost.Instance));
        Assert.Equal(["first", "last"], order);
        Assert.Equal(["failing"], failures);
    }

    [Fact]
    public void Duplicate_action_ids_are_reported_instead_of_throwing()
    {
        using var store = new VariableStore();
        var router = new ActionRouter(store, [new TestApplicationPlugin("p", new RecordingAction("a")), new TestApplicationPlugin("p", new RecordingAction("a"))]);

        Assert.Equal(["p:a"], router.DuplicateActionKeys);
    }
}

public class ProfileStoreTests
{
    [Fact]
    public void Save_then_Load_round_trips_profiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}", "profiles.json");
        try
        {
            var store = new ProfileStore(path);
            var binding = new ControlBinding(
                "key.3",
                ProfileActionKind.MultiAction,
                Bindings.Reference("p", "multi"),
                new ControlAppearance("Hello", "C:/image.png", new RgbFeedback("FF0000", "000000", 150, true)),
                [new MultiActionStep(0, Bindings.Reference("p", "a"))]);
            var document = new ProfileDocument(ProfileDocument.CurrentVersion, "profile", [new DeviceProfile("profile", "Profile", "dev", [binding])]);

            store.Save(document);
            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal("profile", loaded.SelectedProfileId);
            var loadedBinding = Assert.Single(Assert.Single(loaded.Profiles).Bindings);
            Assert.Equal(binding.ControlId, loadedBinding.ControlId);
            Assert.Equal(binding.ActionKind, loadedBinding.ActionKind);
            Assert.Equal(binding.Appearance, loadedBinding.Appearance);
            Assert.Equal(binding.Steps[0], loadedBinding.Steps[0]);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void The_default_background_and_new_appearance_fields_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}", "profiles.json");
        try
        {
            var store = new ProfileStore(path);
            var appearance = new ControlAppearance(BackgroundColor: "#80112233") { IconPath = "/icons/mic.svg", ShowTile = true };
            var binding = new ControlBinding("key.0", ProfileActionKind.Single, Bindings.Reference("p", "a"), appearance, []);
            store.Save(new ProfileDocument(ProfileDocument.CurrentVersion, "profile", [new DeviceProfile("profile", "Profile", "dev", [binding])],
                DefaultBackground: new ProfileBackground("#0F766E", "/images/wall.png")));

            var loaded = store.Load()!;

            Assert.Equal(new ProfileBackground("#0F766E", "/images/wall.png"), loaded.DefaultBackground);
            Assert.Equal(appearance, Assert.Single(Assert.Single(loaded.Profiles).Bindings).Appearance);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_null_when_there_is_no_file()
    {
        Assert.Null(new ProfileStore(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")).Load());
    }
}
