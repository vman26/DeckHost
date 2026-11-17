using DeckHost.App.Profiles;
using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Profiles;
using System.IO.Compression;
using System.Text;

namespace DeckHost.Tests;

public sealed class ProfileFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("deckhost-profile-files").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly IReadOnlyList<ActionItemViewModel> Actions =
        [.. SystemPluginSet.Create().SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action)))];

    /// <summary>A 5×2 key grid and two knobs, like the N4 Pro.</summary>
    private static readonly IReadOnlyList<PhysicalControl> Controls =
    [
        .. Enumerable.Range(0, 10).Select(index => new PhysicalControl($"key.{index}", $"Key {index + 1}", ControlType.Key,
            new Rect(index % 5 * 10, index / 5 * 10, 9, 9), InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress, OutputCapabilities.ColorImage)),
        new PhysicalControl("encoder.0", "Knob 1", ControlType.Encoder, new Rect(0, 30, 9, 9), InputCapabilities.Press | InputCapabilities.Rotate, OutputCapabilities.None),
        new PhysicalControl("encoder.1", "Knob 2", ControlType.Encoder, new Rect(10, 30, 9, 9), InputCapabilities.Press | InputCapabilities.Rotate, OutputCapabilities.None),
    ];

    private static ControlBinding Binding(string control, string actionId, string? image = null, params (string, string)[] settings) =>
        new(control, ProfileActionKind.Single,
            new ActionReference(SystemPluginSet.BuiltInPluginId, "Built-ins", actionId, actionId, InputCapabilities.None, OutputCapabilities.None,
                new ActionSettings(settings.Select(static pair => new KeyValuePair<string, string>(pair.Item1, pair.Item2)))),
            new ControlAppearance(ImagePath: image), []);

    private static WorkflowNode CallNode(string id, string shared) =>
        new(id, WorkflowNodeKinds.Call) { Settings = new ActionSettings(new Dictionary<string, string> { [WorkflowKeys.Workflow] = shared }) };

    private static Workflow SharedFlow(string id, params WorkflowNode[] nodes) =>
        new() { Id = id, Name = "Shared " + id, Nodes = [new WorkflowNode("t", WorkflowNodeKinds.Trigger), .. nodes] };

    [Test]
    public void A_profile_takes_the_shared_workflows_it_calls_along_with_new_ids_and_leaves_the_others()
    {
        var calling = new ControlBinding("key.0", ProfileActionKind.Workflow, Binding("key.0", WorkflowAction.ActionId).Action, new ControlAppearance(), [])
        {
            Workflow = new Workflow { Nodes = [new WorkflowNode("t", WorkflowNodeKinds.Trigger), CallNode("c", "a")] },
        };
        var profile = new DeviceProfile("p", "Studio", "dev", [calling]);
        var shared = new[] { SharedFlow("a", CallNode("inner", "b")), SharedFlow("b"), SharedFlow("unused") };
        var file = Path.Combine(_root, "shared" + ProfileBundle.Extension);

        ProfileBundle.Export([profile], file, shared);
        var imported = ProfileBundle.ImportBundle(file, "dev", Path.Combine(_root, "assets"));

        Assert.That(imported.SharedWorkflows.Select(static workflow => workflow.Name), Is.EquivalentTo(new[] { "Shared a", "Shared b" }), "the one nothing calls stays behind");
        var a = imported.SharedWorkflows.Single(static workflow => workflow.Name == "Shared a");
        var b = imported.SharedWorkflows.Single(static workflow => workflow.Name == "Shared b");
        Assert.That(a.Id, Is.Not.EqualTo("a").And.StartWith("a."), "new ids, so an import never replaces yours");
        var calledFromProfile = imported.Layouts[0].Bindings[0].Workflow!.Nodes.Single(static node => node.Kind == WorkflowNodeKinds.Call).EffectiveSettings.Get(WorkflowKeys.Workflow);
        Assert.That(calledFromProfile, Is.EqualTo(a.Id), "the profile's Call node follows");
        Assert.That(a.Nodes.Single(static node => node.Kind == WorkflowNodeKinds.Call).EffectiveSettings.Get(WorkflowKeys.Workflow), Is.EqualTo(b.Id), "so does a call inside a shared workflow");
        Assert.That(ProfileBundle.Import(file, "dev", Path.Combine(_root, "assets")), Has.Count.EqualTo(1), "the plain Import still works");
    }

    [Test]
    public void A_bundle_without_shared_workflows_has_none_and_used_ones_are_found_through_calls_only()
    {
        var plain = new DeviceProfile("p", "Plain", "dev", [Binding("key.0", FolderBackAction.ActionId)]);
        var file = Path.Combine(_root, "plain" + ProfileBundle.Extension);

        ProfileBundle.Export([plain], file, [SharedFlow("a")]);

        Assert.That(ProfileBundle.ImportBundle(file, "dev", Path.Combine(_root, "assets")).SharedWorkflows, Is.Empty);
        Assert.That(ProfileBundle.SharedWorkflowsUsed([plain], [SharedFlow("a")]), Is.Empty);
    }

    [Test]
    public void The_images_of_background_layers_travel_with_the_profile_and_other_layers_are_kept()
    {
        var picture = Path.Combine(_root, "layer.png");
        File.WriteAllBytes(picture, [9, 9, 9]);
        var profile = new DeviceProfile("p", "Studio", "dev", [], Background: new ProfileBackground("#123456")
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Image, Opacity: 0.5) { ImagePath = picture },
                new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = "#FF0000", ToColor = "#0000FF", Angle = 30 },
            ],
        });
        var file = Path.Combine(_root, "layers" + ProfileBundle.Extension);

        ProfileBundle.Export([profile], file);
        File.Delete(picture);
        var imported = ProfileBundle.Import(file, "dev", Path.Combine(_root, "assets"));

        var layers = imported[0].Background!.Layers!;
        Assert.That(layers, Has.Count.EqualTo(2));
        Assert.That(File.ReadAllBytes(layers[0].ImagePath!), Is.EqualTo(new byte[] { 9, 9, 9 }), "the picture came along");
        Assert.That(layers[0].Opacity, Is.EqualTo(0.5));
        Assert.That(layers[1].Angle, Is.EqualTo(30));
        Assert.That(imported[0].Background!.Color, Is.EqualTo("#123456"));
    }

    [Test]
    public void A_profile_travels_with_its_pages_folders_and_images()
    {
        var image = Path.Combine(_root, "photo.png");
        File.WriteAllBytes(image, [1, 2, 3]);
        var profile = new DeviceProfile("p", "Studio", "dev", [Binding("key.0", FolderAction.ActionId, image, (FolderAction.FolderIdKey, "p.folder"))]);
        var page = new DeviceProfile("p.page", "Page 2", "dev", []) { ParentId = "p", Kind = ProfileKinds.Page };
        var folder = new DeviceProfile("p.folder", "Tools", "dev", [Binding("key.0", FolderBackAction.ActionId)]) { ParentId = "p", Kind = ProfileKinds.Folder };
        var file = Path.Combine(_root, "studio" + ProfileBundle.Extension);

        ProfileBundle.Export([profile, page, folder], file);
        File.Delete(image); // the bundle carries its own copy
        var imported = ProfileBundle.Import(file, "other-device", Path.Combine(_root, "assets"));

        Assert.That(imported.Count, Is.EqualTo(3));
        Expect.All(imported, layout => Assert.That(layout.DeviceId, Is.EqualTo("other-device")));
        Assert.That(imported.Any(layout => layout.Id is "p" or "p.page" or "p.folder"), Is.False); // never replaces the original
        var root = imported[0];
        var importedFolder = imported.Single(layout => layout.Kind == ProfileKinds.Folder);
        Assert.That(importedFolder.ParentId, Is.EqualTo(root.Id));
        Assert.That(root.Bindings[0].Action.Settings.Get(FolderAction.FolderIdKey), Is.EqualTo(importedFolder.Id)); // the key opens the copy
        Assert.That(File.ReadAllBytes(root.Bindings[0].Appearance.ImagePath!), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Other_files_are_refused_plainly()
    {
        var file = Path.Combine(_root, "not-a-profile" + ProfileBundle.Extension);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Assert.That(Assert.Throws<ProfileBundleException>(() => ProfileBundle.Import(file, "dev", _root)).Message, Does.Contain("isn't a DeckHost profile"));
    }

    [Test]
    public void Profiles_are_backed_up_once_a_day()
    {
        var store = new ProfileStore(Path.Combine(_root, "profiles.json"));
        var document = new ProfileDocument(ProfileDocument.CurrentVersion, "p", [new DeviceProfile("p", "P", "dev", [])]);
        store.Save(document); // nothing to back up yet
        Assert.That(store.ListBackups(), Is.Empty);

        store.Save(document with { SelectedProfileId = "p2" });
        store.Save(document with { SelectedProfileId = "p3" }); // same day: one backup
        var backup = Expect.Single(store.ListBackups());
        Assert.That(ProfileStore.LoadFrom(backup.FullName)!.SelectedProfileId, Is.EqualTo("p"));
    }

    private string StreamDeckProfile(params (string Path, string Content)[] files)
    {
        var file = Path.Combine(_root, $"{Guid.NewGuid():N}{StreamDeckProfileImporter.Extension}");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var (path, content) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return file;
    }

    [Test]
    public void A_paged_stream_deck_profile_becomes_a_profile_with_pages_and_folders()
    {
        var file = StreamDeckProfile(
            ("ABC.sdProfile/manifest.json", """
                { "Name": "Streaming", "Version": "2.0", "Device": { "Model": "20GAA9901", "UUID": "" },
                  "Pages": { "Current": "page-1", "Pages": ["page-1", "page-2"] } }
                """),
            ("ABC.sdProfile/Profiles/PAGE-1/manifest.json", """
                { "Controllers": [
                    { "Type": "Keypad", "Actions": {
                      "0,0": { "UUID": "com.elgato.streamdeck.system.website", "Name": "Website", "Settings": { "path": "https://example.com" }, "State": 0,
                               "States": [ { "Title": "Docs", "Image": "Images/docs.png" } ] },
                      "1,1": { "UUID": "com.elgato.streamdeck.system.multimedia", "Settings": { "actionIdx": 3 }, "States": [ {} ] },
                      "4,0": { "UUID": "com.elgato.streamdeck.profile.openchild", "Settings": { "ProfileUUID": "FOLDER-1" }, "States": [ { "Title": "Tools" } ] },
                      "2,0": { "UUID": "com.elgato.streamdeck.multiactions.routine2", "Settings": {
                                 "Routine": [ { "UUID": "com.elgato.streamdeck.system.multimedia", "Settings": { "actionIdx": 0 } } ],
                                 "RoutineAlt": [ { "UUID": "com.elgato.streamdeck.system.multimedia", "Settings": { "actionIdx": 1 } } ] }, "States": [ {} ] },
                      "6,2": { "UUID": "com.elgato.streamdeck.system.hotkey", "States": [ {} ] },
                      "3,0": { "UUID": "com.example.unknown.action", "Name": "Weather", "States": [ { "Title": "Sunny" } ] } } },
                    { "Type": "Encoder", "Actions": {
                      "1,0": { "UUID": "com.elgato.streamdeck.page.next", "States": [ {} ] } } } ] }
                """),
            ("ABC.sdProfile/Profiles/PAGE-1/Images/docs.png", "png-bytes"),
            ("ABC.sdProfile/Profiles/PAGE-2/manifest.json", """{ "Controllers": [ { "Type": "Keypad", "Actions": { "0,0": { "UUID": "com.elgato.streamdeck.page.previous", "States": [ {} ] } } } ] }"""),
            ("ABC.sdProfile/Profiles/FOLDER-1/manifest.json", """
                { "Name": "Tools", "Controllers": [ { "Type": "Keypad", "Actions": {
                    "0,0": { "UUID": "com.elgato.streamdeck.profile.backtoparent", "States": [ {} ] },
                    "1,0": { "UUID": "com.elgato.streamdeck.system.text", "Settings": { "pastedText": "hello" }, "States": [ {} ] } } } ] }
                """));

        var result = StreamDeckProfileImporter.Import(file, "dev", Controls, Actions, Path.Combine(_root, "assets"));

        var root = result.Layouts[0];
        Assert.That((root.Name, root.Kind), Is.EqualTo(("Streaming", (string?)null)));
        Assert.That(result.Layouts.Any(layout => layout.Kind == ProfileKinds.Page && layout.ParentId == root.Id && layout.Bindings[0].Action.Settings.Get("mode") == "previous"), Is.True);

        var website = root.Bindings.Single(binding => binding.ControlId == "key.0");
        Assert.That((website.Action.ActionId, website.Action.Settings.Get("target"), website.Appearance.Text), Is.EqualTo(("system.open", "https://example.com", "Docs")));
        Assert.That(File.ReadAllText(website.Appearance.ImagePath!), Is.EqualTo("png-bytes"));
        Assert.That(root.Bindings.Single(binding => binding.ControlId == "key.6").Action.ActionId, Is.EqualTo("media.mute")); // column 1, row 1
        Assert.That(root.Bindings.Single(binding => binding.ControlId == "encoder.1").Action.ActionId, Is.EqualTo("builtin.page"));

        var toggle = root.Bindings.Single(binding => binding.ControlId == "key.2");
        Assert.That(toggle.ActionKind, Is.EqualTo(ProfileActionKind.MultiActionSwitch));
        Assert.That(toggle.Steps.Select(step => (step.Action.ActionId, step.Group)), Is.EqualTo(new[] { ("media.playpause", StepGroups.First), ("media.next", StepGroups.Second) }));

        var folderKey = root.Bindings.Single(binding => binding.ControlId == "key.4");
        var folder = result.Layouts.Single(layout => layout.Id == folderKey.Action.Settings.Get(FolderAction.FolderIdKey));
        Assert.That((folder.Kind, folder.ParentId, folder.Name), Is.EqualTo((ProfileKinds.Folder, root.Id, "Tools")));
        Assert.That(folder.Bindings.OrderBy(binding => binding.ControlId).Select(binding => binding.Action.ActionId), Is.EqualTo(new[] { FolderBackAction.ActionId, "system.type-text" }));

        // Unknown: kept as the key looked; a key past this device's grid: not placed.
        Assert.That(root.Bindings.Single(binding => binding.ControlId == "key.3").Appearance.Text, Is.EqualTo("Sunny"));
        Assert.That(result.Unavailable, Does.Contain("Weather"));
        Assert.That(result.NotPlaced, Is.EqualTo(1));
    }

    [Test]
    public void A_single_page_stream_deck_profile_imports_too()
    {
        var file = StreamDeckProfile(("X.sdProfile/manifest.json", """
            { "Name": "Old", "Version": "1.0", "Actions": {
                "0,0": { "UUID": "com.elgato.streamdeck.system.open", "Settings": { "path": "C:\\Tools\\app.exe" }, "States": [ { "Title": "App" } ] },
                "1,0": { "UUID": "com.elgato.streamdeck.multiactions.routine", "Settings": { "Routine": [
                    { "UUID": "com.elgato.streamdeck.system.multimedia", "Settings": { "actionIdx": 4 } },
                    { "UUID": "com.elgato.streamdeck.system.multimedia", "Settings": { "actionIdx": 4 } } ] }, "States": [ {} ] } } }
            """));

        var result = StreamDeckProfileImporter.Import(file, "dev", Controls, Actions, _root);

        var root = Expect.Single(result.Layouts);
        Assert.That(root.Bindings.Single(binding => binding.ControlId == "key.0").Action.ActionId, Is.EqualTo("system.launch"));
        var multi = root.Bindings.Single(binding => binding.ControlId == "key.1");
        Assert.That((multi.ActionKind, multi.Steps.Count), Is.EqualTo((ProfileActionKind.MultiAction, 2)));
        Assert.That(result.Imported, Is.EqualTo(2));
    }
}
