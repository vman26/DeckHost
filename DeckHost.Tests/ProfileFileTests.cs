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

    [Fact]
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

        Assert.Equal(3, imported.Count);
        Assert.All(imported, layout => Assert.Equal("other-device", layout.DeviceId));
        Assert.DoesNotContain(imported, layout => layout.Id is "p" or "p.page" or "p.folder"); // never replaces the original
        var root = imported[0];
        var importedFolder = imported.Single(layout => layout.Kind == ProfileKinds.Folder);
        Assert.Equal(root.Id, importedFolder.ParentId);
        Assert.Equal(importedFolder.Id, root.Bindings[0].Action.Settings.Get(FolderAction.FolderIdKey)); // the key opens the copy
        Assert.Equal([1, 2, 3], File.ReadAllBytes(root.Bindings[0].Appearance.ImagePath!));
    }

    [Fact]
    public void Other_files_are_refused_plainly()
    {
        var file = Path.Combine(_root, "not-a-profile" + ProfileBundle.Extension);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Assert.Contains("isn't a DeckHost profile", Assert.Throws<ProfileBundleException>(() => ProfileBundle.Import(file, "dev", _root)).Message);
    }

    [Fact]
    public void Profiles_are_backed_up_once_a_day()
    {
        var store = new ProfileStore(Path.Combine(_root, "profiles.json"));
        var document = new ProfileDocument(ProfileDocument.CurrentVersion, "p", [new DeviceProfile("p", "P", "dev", [])]);
        store.Save(document); // nothing to back up yet
        Assert.Empty(store.ListBackups());

        store.Save(document with { SelectedProfileId = "p2" });
        store.Save(document with { SelectedProfileId = "p3" }); // same day: one backup
        var backup = Assert.Single(store.ListBackups());
        Assert.Equal("p", ProfileStore.LoadFrom(backup.FullName)!.SelectedProfileId);
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

    [Fact]
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
        Assert.Equal(("Streaming", (string?)null), (root.Name, root.Kind));
        Assert.Contains(result.Layouts, layout => layout.Kind == ProfileKinds.Page && layout.ParentId == root.Id && layout.Bindings[0].Action.Settings.Get("mode") == "previous");

        var website = root.Bindings.Single(binding => binding.ControlId == "key.0");
        Assert.Equal(("system.open", "https://example.com", "Docs"), (website.Action.ActionId, website.Action.Settings.Get("target"), website.Appearance.Text));
        Assert.Equal("png-bytes", File.ReadAllText(website.Appearance.ImagePath!));
        Assert.Equal("media.mute", root.Bindings.Single(binding => binding.ControlId == "key.6").Action.ActionId); // column 1, row 1
        Assert.Equal("builtin.page", root.Bindings.Single(binding => binding.ControlId == "encoder.1").Action.ActionId);

        var toggle = root.Bindings.Single(binding => binding.ControlId == "key.2");
        Assert.Equal(ProfileActionKind.MultiActionSwitch, toggle.ActionKind);
        Assert.Equal([("media.playpause", StepGroups.First), ("media.next", StepGroups.Second)], toggle.Steps.Select(step => (step.Action.ActionId, step.Group)));

        var folderKey = root.Bindings.Single(binding => binding.ControlId == "key.4");
        var folder = result.Layouts.Single(layout => layout.Id == folderKey.Action.Settings.Get(FolderAction.FolderIdKey));
        Assert.Equal((ProfileKinds.Folder, root.Id, "Tools"), (folder.Kind, folder.ParentId, folder.Name));
        Assert.Equal([FolderBackAction.ActionId, "system.type-text"], folder.Bindings.OrderBy(binding => binding.ControlId).Select(binding => binding.Action.ActionId));

        // Unknown: kept as the key looked; a key past this device's grid: not placed.
        Assert.Equal("Sunny", root.Bindings.Single(binding => binding.ControlId == "key.3").Appearance.Text);
        Assert.Contains("Weather", result.Unavailable);
        Assert.Equal(1, result.NotPlaced);
    }

    [Fact]
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

        var root = Assert.Single(result.Layouts);
        Assert.Equal("system.launch", root.Bindings.Single(binding => binding.ControlId == "key.0").Action.ActionId);
        var multi = root.Bindings.Single(binding => binding.ControlId == "key.1");
        Assert.Equal((ProfileActionKind.MultiAction, 2), (multi.ActionKind, multi.Steps.Count));
        Assert.Equal(2, result.Imported);
    }
}
