using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Profiles;
using DeckHost.Device.Elgato;

namespace DeckHost.Tests;

public class LayoutMapperTests
{
    private static readonly IReadOnlyList<PhysicalControl> Mini = StreamDeckModel.Find(0x0063)!.Controls();      // 3 × 2
    private static readonly IReadOnlyList<PhysicalControl> Mk2 = StreamDeckModel.Find(0x0080)!.Controls();       // 5 × 3
    private static readonly IReadOnlyList<PhysicalControl> Plus = StreamDeckModel.Find(0x0084)!.Controls();      // 4 × 2 + dials

    private static ControlBinding On(string controlId, string name) =>
        new(controlId, ProfileActionKind.Single, new ActionReference("p", "Plugin", name, name, InputCapabilities.Press, OutputCapabilities.None, new ActionSettings([])), new ControlAppearance(), []);

    private static DeviceProfile Profile(params ControlBinding[] bindings) => new("p1", "Work", "from", bindings);

    [Test]
    public void Columns_are_counted_from_the_first_row()
    {
        Assert.That(LayoutMapper.ColumnsOf(Mini), Is.EqualTo(3));
        Assert.That(LayoutMapper.ColumnsOf(Mk2), Is.EqualTo(5));
        Assert.That(LayoutMapper.ColumnsOf(Plus), Is.EqualTo(4));
    }

    [Test]
    public void Keys_keep_row_and_column_on_a_bigger_grid()
    {
        // Mini key.3 is row 1, column 0: on a 5-wide deck that's key.5.
        var result = LayoutMapper.Map([Profile(On("key.3", "a"), On("key.2", "b"))], Mk2, sourceColumns: 3, static n => $"page{n}");

        var bindings = result.Layouts[0].Bindings.ToDictionary(static binding => binding.Action.ActionName, static binding => binding.ControlId);
        Assert.That(bindings["a"], Is.EqualTo("key.5"));
        Assert.That(bindings["b"], Is.EqualTo("key.2"));
        Assert.That(result.Placed, Is.EqualTo(2));
    }

    [Test]
    public void Keys_that_do_not_fit_go_to_added_pages_and_dials_the_target_lacks_are_left_out()
    {
        var source = Profile([.. Enumerable.Range(0, 15).Select(index => On($"key.{index}", $"k{index}")), On("encoder.0", "dial"), On("touch.swipe", "swipe")]);

        var result = LayoutMapper.Map([source], Mini, sourceColumns: null, static n => $"page{n}");

        Assert.That(result.Layouts[0].Bindings.Count, Is.EqualTo(6));          // keys 0–5 in reading order
        Assert.That(result.MovedToNewPages, Is.EqualTo(9));
        Assert.That(result.PagesAdded, Is.EqualTo(2));
        var page = result.Layouts[1];
        Assert.That((page.Id, page.Kind, page.ParentId), Is.EqualTo(("page1", ProfileKinds.Page, "p1")));
        Assert.That(page.Bindings.Select(static binding => binding.Action.ActionName), Is.EqualTo(new[] { "k6", "k7", "k8", "k9", "k10", "k11" }));
        Assert.That(result.Layouts[2].Bindings.Select(static binding => binding.ControlId), Is.EqualTo(new[] { "key.0", "key.1", "key.2" }));
        Assert.That(result.Dropped, Is.EqualTo(2));
        Assert.That(result.Summary, Does.Contain("left out"));
    }

    [Test]
    public void Dials_and_touch_zones_keep_their_number_where_they_exist()
    {
        var result = LayoutMapper.Map([Profile(On("encoder.2", "dial"), On("touch.1", "zone"), On("encoder.3", "last"))], Plus, sourceColumns: 5, static n => $"page{n}");
        Assert.That(result.Layouts[0].Bindings.Select(static binding => binding.ControlId), Is.EqualTo(new[] { "encoder.2", "touch.1", "encoder.3" }));
    }

    [Test]
    public void A_copy_gets_new_ids_for_its_device_and_folder_keys_follow()
    {
        var folderKey = On("key.0", "Folder") with { Action = new ActionReference("p", "Plugin", "folder", "Folder", InputCapabilities.Press, OutputCapabilities.None, new ActionSettings([new("folder", "f1")])) };
        var copy = ProfileBundle.Copy([Profile(folderKey), new DeviceProfile("f1", "Folder", "from", []) { ParentId = "p1", Kind = ProfileKinds.Folder }], "to");

        Expect.All(copy, static layout => Assert.That(layout.DeviceId, Is.EqualTo("to")));
        Assert.That(copy[0].Id, Is.Not.EqualTo("p1"));
        Assert.That(copy[0].Bindings[0].Action.EffectiveSettings.Get("folder"), Is.EqualTo(copy[1].Id));
        Assert.That(copy[1].ParentId, Is.EqualTo(copy[0].Id));
    }
}
