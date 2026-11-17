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

    private const string Dev = "dev";
    private const string OtherDevice = "other-device";
    private const string SharedA = "a";
    private const string SharedB = "b";
    private const string Unused = "unused";
    private const string AssetsFolder = "assets";
    private const string Blue = "#0000FF";
    private const string Red = "#FF0000";
    private const double HalfOpacity = 0.5;
    private const int LayerAngle = 30;

    private string AssetsPath => Path.Combine(_root, AssetsFolder);

    private (DeviceProfile Profile, Workflow[] Shared, string File) SharedWorkflowsSetup()
    {
        var calling = new ControlBinding("key.0", ProfileActionKind.Workflow, Binding("key.0", WorkflowAction.ActionId).Action, new ControlAppearance(), [])
        {
            Workflow = new Workflow { Nodes = [new WorkflowNode("t", WorkflowNodeKinds.Trigger), CallNode("c", SharedA)] },
        };
        var profile = new DeviceProfile("p", "Studio", Dev, [calling]);
        var shared = new[] { SharedFlow(SharedA, CallNode("inner", SharedB)), SharedFlow(SharedB), SharedFlow(Unused) };
        return (profile, shared, Path.Combine(_root, "shared" + ProfileBundle.Extension));
    }

    [Test]
    public void ImportBundle_AProfileThatCallsSharedWorkflows_BringsAlongTheOnesItUsesAndLeavesTheOthers()
    {
        // Arrange
        var (profile, shared, file) = SharedWorkflowsSetup();
        ProfileBundle.Export([profile], file, shared);

        // Act
        var imported = ProfileBundle.ImportBundle(file, Dev, AssetsPath);

        // Assert
        Assert.That(imported.SharedWorkflows.Select(static workflow => workflow.Name), Is.EquivalentTo(new[] { "Shared a", "Shared b" }), "the one nothing calls stays behind");
    }

    [Test]
    public void ImportBundle_SharedWorkflows_GetNewIdsSoAnImportNeverReplacesYours()
    {
        // Arrange
        var (profile, shared, file) = SharedWorkflowsSetup();
        ProfileBundle.Export([profile], file, shared);

        // Act
        var imported = ProfileBundle.ImportBundle(file, Dev, AssetsPath);

        // Assert
        var a = imported.SharedWorkflows.Single(static workflow => workflow.Name == "Shared a");
        Assert.That(a.Id, Is.Not.EqualTo(SharedA).And.StartWith(SharedA + "."));
    }

    [Test]
    public void ImportBundle_SharedWorkflows_KeepTheCallsBetweenThemAndFromTheProfileConnected()
    {
        // Arrange
        var (profile, shared, file) = SharedWorkflowsSetup();
        ProfileBundle.Export([profile], file, shared);

        // Act
        var imported = ProfileBundle.ImportBundle(file, Dev, AssetsPath);

        // Assert
        var a = imported.SharedWorkflows.Single(static workflow => workflow.Name == "Shared a");
        var b = imported.SharedWorkflows.Single(static workflow => workflow.Name == "Shared b");
        var calledFromProfile = imported.Layouts[0].Bindings[0].Workflow!.Nodes.Single(static node => node.Kind == WorkflowNodeKinds.Call).EffectiveSettings.Get(WorkflowKeys.Workflow);
        Assert.That(calledFromProfile, Is.EqualTo(a.Id), "the profile's Call node follows");
        Assert.That(a.Nodes.Single(static node => node.Kind == WorkflowNodeKinds.Call).EffectiveSettings.Get(WorkflowKeys.Workflow), Is.EqualTo(b.Id), "so does a call inside a shared workflow");
    }

    [Test]
    public void Import_ABundleWithSharedWorkflows_StillImportsThePlainProfile()
    {
        // Arrange
        var (profile, shared, file) = SharedWorkflowsSetup();
        ProfileBundle.Export([profile], file, shared);

        // Act
        var layouts = ProfileBundle.Import(file, Dev, AssetsPath);

        // Assert
        Assert.That(layouts, Has.Count.EqualTo(1));
    }

    [Test]
    public void ImportBundle_AProfileThatCallsNothing_HasNoSharedWorkflows()
    {
        // Arrange
        var plain = new DeviceProfile("p", "Plain", Dev, [Binding("key.0", FolderBackAction.ActionId)]);
        var file = Path.Combine(_root, "plain" + ProfileBundle.Extension);
        ProfileBundle.Export([plain], file, [SharedFlow(SharedA)]);

        // Act
        var imported = ProfileBundle.ImportBundle(file, Dev, AssetsPath);

        // Assert
        Assert.That(imported.SharedWorkflows, Is.Empty);
    }

    [Test]
    public void SharedWorkflowsUsed_AProfileThatCallsNothing_FindsNone()
    {
        // Arrange
        var plain = new DeviceProfile("p", "Plain", Dev, [Binding("key.0", FolderBackAction.ActionId)]);

        // Act
        var used = ProfileBundle.SharedWorkflowsUsed([plain], [SharedFlow(SharedA)]);

        // Assert
        Assert.That(used, Is.Empty);
    }

    private (DeviceProfile Profile, string Picture, string File) LayeredProfile()
    {
        var picture = Path.Combine(_root, "layer.png");
        File.WriteAllBytes(picture, [9, 9, 9]);
        var profile = new DeviceProfile("p", "Studio", Dev, [], Background: new ProfileBackground("#123456")
        {
            Layers =
            [
                new BackgroundLayer(BackgroundLayerKinds.Image, Opacity: HalfOpacity) { ImagePath = picture },
                new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Red, ToColor = Blue, Angle = LayerAngle },
            ],
        });
        return (profile, picture, Path.Combine(_root, "layers" + ProfileBundle.Extension));
    }

    [Test]
    public void Import_ALayeredBackground_BringsTheLayersPictureAlong()
    {
        // Arrange
        var (profile, picture, file) = LayeredProfile();
        ProfileBundle.Export([profile], file);
        File.Delete(picture);

        // Act
        var imported = ProfileBundle.Import(file, Dev, AssetsPath);

        // Assert
        var layers = imported[0].Background!.Layers!;
        Assert.That(layers, Has.Count.EqualTo(2));
        Assert.That(File.ReadAllBytes(layers[0].ImagePath!), Is.EqualTo(new byte[] { 9, 9, 9 }), "the picture came along");
    }

    [Test]
    public void Import_ALayeredBackground_KeepsTheOtherLayersAndTheColor()
    {
        // Arrange
        var (profile, picture, file) = LayeredProfile();
        ProfileBundle.Export([profile], file);
        File.Delete(picture);

        // Act
        var imported = ProfileBundle.Import(file, Dev, AssetsPath);

        // Assert
        var layers = imported[0].Background!.Layers!;
        Assert.That(layers[0].Opacity, Is.EqualTo(HalfOpacity));
        Assert.That(layers[1].Angle, Is.EqualTo(LayerAngle));
        Assert.That(imported[0].Background!.Color, Is.EqualTo("#123456"));
    }

    private (DeviceProfile[] Layouts, string Image, string File) ProfileWithAPageAndAFolder()
    {
        var image = Path.Combine(_root, "photo.png");
        File.WriteAllBytes(image, [1, 2, 3]);
        var profile = new DeviceProfile("p", "Studio", Dev, [Binding("key.0", FolderAction.ActionId, image, (FolderAction.FolderIdKey, "p.folder"))]);
        var page = new DeviceProfile("p.page", "Page 2", Dev, []) { ParentId = "p", Kind = ProfileKinds.Page };
        var folder = new DeviceProfile("p.folder", "Tools", Dev, [Binding("key.0", FolderBackAction.ActionId)]) { ParentId = "p", Kind = ProfileKinds.Folder };
        return ([profile, page, folder], image, Path.Combine(_root, "studio" + ProfileBundle.Extension));
    }

    [Test]
    public void Import_AProfileWithAPageAndAFolder_BringsAllThreeLayoutsToTheOtherDeviceWithNewIds()
    {
        // Arrange
        var (layouts, image, file) = ProfileWithAPageAndAFolder();
        ProfileBundle.Export(layouts, file);
        File.Delete(image); // the bundle carries its own copy

        // Act
        var imported = ProfileBundle.Import(file, OtherDevice, AssetsPath);

        // Assert
        Assert.That(imported.Count, Is.EqualTo(3));
        Expect.All(imported, layout => Assert.That(layout.DeviceId, Is.EqualTo(OtherDevice)));
        Assert.That(imported.Any(layout => layout.Id is "p" or "p.page" or "p.folder"), Is.False, "never replaces the original");
    }

    [Test]
    public void Import_AFolderKey_OpensTheCopiedFolder()
    {
        // Arrange
        var (layouts, image, file) = ProfileWithAPageAndAFolder();
        ProfileBundle.Export(layouts, file);
        File.Delete(image);

        // Act
        var imported = ProfileBundle.Import(file, OtherDevice, AssetsPath);

        // Assert
        var root = imported[0];
        var importedFolder = imported.Single(layout => layout.Kind == ProfileKinds.Folder);
        Assert.That(importedFolder.ParentId, Is.EqualTo(root.Id));
        Assert.That(root.Bindings[0].Action.Settings.Get(FolderAction.FolderIdKey), Is.EqualTo(importedFolder.Id));
    }

    [Test]
    public void Import_AKeyImage_IsUnpackedFromTheBundle()
    {
        // Arrange
        var (layouts, image, file) = ProfileWithAPageAndAFolder();
        ProfileBundle.Export(layouts, file);
        File.Delete(image);

        // Act
        var imported = ProfileBundle.Import(file, OtherDevice, AssetsPath);

        // Assert
        Assert.That(File.ReadAllBytes(imported[0].Bindings[0].Appearance.ImagePath!), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void Import_AFileThatIsNoProfile_IsRefusedPlainly()
    {
        // Arrange
        var file = Path.Combine(_root, "not-a-profile" + ProfileBundle.Extension);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        // Act
        var error = Assert.Throws<ProfileBundleException>(() => ProfileBundle.Import(file, Dev, _root));

        // Assert
        Assert.That(error.Message, Does.Contain("isn't a DeckHost profile"));
    }

    private static ProfileDocument DocumentSelecting(string profileId) =>
        new(ProfileDocument.CurrentVersion, profileId, [new DeviceProfile("p", "P", Dev, [])]);

    [Test]
    public void ListBackups_AfterTheFirstSave_HasNothingToBackUpYet()
    {
        // Arrange
        var store = new ProfileStore(Path.Combine(_root, "profiles.json"));

        // Act
        store.Save(DocumentSelecting("p"));

        // Assert
        Assert.That(store.ListBackups(), Is.Empty);
    }

    [Test]
    public void ListBackups_SeveralSavesOnTheSameDay_KeepsOneBackupOfTheFirstVersion()
    {
        // Arrange
        var store = new ProfileStore(Path.Combine(_root, "profiles.json"));
        store.Save(DocumentSelecting("p"));

        // Act
        store.Save(DocumentSelecting("p2"));
        store.Save(DocumentSelecting("p3"));

        // Assert
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

    private string PagedStreamDeckProfile() =>
        StreamDeckProfile(
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

    private const string KeyMuteAction = "media.mute";

    private StreamDeckImportResult ImportPaged() =>
        StreamDeckProfileImporter.Import(PagedStreamDeckProfile(), Dev, Controls, Actions, AssetsPath);

    [Test]
    public void Import_APagedStreamDeckProfile_BecomesAProfileWithItsNameAndAPage()
    {
        // Act
        var result = ImportPaged();

        // Assert
        var root = result.Layouts[0];
        Assert.That((root.Name, root.Kind), Is.EqualTo(("Streaming", (string?)null)));
        Assert.That(result.Layouts.Any(layout => layout.Kind == ProfileKinds.Page && layout.ParentId == root.Id && layout.Bindings[0].Action.Settings.Get("mode") == "previous"), Is.True);
    }

    [Test]
    public void Import_AWebsiteKey_BecomesAnOpenActionWithItsTitleAndImage()
    {
        // Act
        var result = ImportPaged();

        // Assert
        var website = result.Layouts[0].Bindings.Single(binding => binding.ControlId == "key.0");
        Assert.That((website.Action.ActionId, website.Action.Settings.Get("target"), website.Appearance.Text), Is.EqualTo(("system.open", "https://example.com", "Docs")));
        Assert.That(File.ReadAllText(website.Appearance.ImagePath!), Is.EqualTo("png-bytes"));
    }

    [Test]
    public void Import_KeysAndDialsByColumnAndRow_LandOnTheMatchingControls()
    {
        // Act
        var root = ImportPaged().Layouts[0];

        // Assert
        Assert.That(root.Bindings.Single(binding => binding.ControlId == "key.6").Action.ActionId, Is.EqualTo(KeyMuteAction), "column 1, row 1");
        Assert.That(root.Bindings.Single(binding => binding.ControlId == "encoder.1").Action.ActionId, Is.EqualTo("builtin.page"));
    }

    [Test]
    public void Import_ARoutineWithAnAlternative_BecomesAMultiActionSwitch()
    {
        // Act
        var root = ImportPaged().Layouts[0];

        // Assert
        var toggle = root.Bindings.Single(binding => binding.ControlId == "key.2");
        Assert.That(toggle.ActionKind, Is.EqualTo(ProfileActionKind.MultiActionSwitch));
        Assert.That(toggle.Steps.Select(step => (step.Action.ActionId, step.Group)), Is.EqualTo(new[] { ("media.playpause", StepGroups.First), ("media.next", StepGroups.Second) }));
    }

    [Test]
    public void Import_AnOpenChildKey_BecomesAFolderWithItsKeys()
    {
        // Act
        var result = ImportPaged();

        // Assert
        var root = result.Layouts[0];
        var folderKey = root.Bindings.Single(binding => binding.ControlId == "key.4");
        var folder = result.Layouts.Single(layout => layout.Id == folderKey.Action.Settings.Get(FolderAction.FolderIdKey));
        Assert.That((folder.Kind, folder.ParentId, folder.Name), Is.EqualTo((ProfileKinds.Folder, root.Id, "Tools")));
        Assert.That(folder.Bindings.OrderBy(binding => binding.ControlId).Select(binding => binding.Action.ActionId), Is.EqualTo(new[] { FolderBackAction.ActionId, "system.type-text" }));
    }

    [Test]
    public void Import_AnUnknownActionAndAKeyPastTheGrid_IsKeptAsTheKeyLookedOrNotPlaced()
    {
        // Act
        var result = ImportPaged();

        // Assert
        Assert.That(result.Layouts[0].Bindings.Single(binding => binding.ControlId == "key.3").Appearance.Text, Is.EqualTo("Sunny"));
        Assert.That(result.Unavailable, Does.Contain("Weather"));
        Assert.That(result.NotPlaced, Is.EqualTo(1));
    }

    private string SinglePageStreamDeckProfile() => StreamDeckProfile(("X.sdProfile/manifest.json", """
            { "Name": "Old", "Version": "1.0", "Actions": {
                "0,0": { "UUID": "com.elgato.streamdeck.system.open", "Settings": { "path": "C:\\Tools\\app.exe" }, "States": [ { "Title": "App" } ] },
                "1,0": { "UUID": "com.elgato.streamdeck.multiactions.routine", "Settings": { "Routine": [
                    { "UUID": "com.elgato.streamdeck.system.multimedia", "Settings": { "actionIdx": 4 } },
                    { "UUID": "com.elgato.streamdeck.system.multimedia", "Settings": { "actionIdx": 4 } } ] }, "States": [ {} ] } } }
            """));

    [Test]
    public void Import_ASinglePageStreamDeckProfile_MapsAnOpenKeyToALaunchAction()
    {
        // Act
        var result = StreamDeckProfileImporter.Import(SinglePageStreamDeckProfile(), Dev, Controls, Actions, _root);

        // Assert
        var root = Expect.Single(result.Layouts);
        Assert.That(root.Bindings.Single(binding => binding.ControlId == "key.0").Action.ActionId, Is.EqualTo("system.launch"));
    }

    [Test]
    public void Import_ASinglePageStreamDeckProfile_MapsARoutineToAMultiActionWithItsSteps()
    {
        // Act
        var result = StreamDeckProfileImporter.Import(SinglePageStreamDeckProfile(), Dev, Controls, Actions, _root);

        // Assert
        var multi = Expect.Single(result.Layouts).Bindings.Single(binding => binding.ControlId == "key.1");
        Assert.That((multi.ActionKind, multi.Steps.Count), Is.EqualTo((ProfileActionKind.MultiAction, 2)));
        Assert.That(result.Imported, Is.EqualTo(2));
    }
}
