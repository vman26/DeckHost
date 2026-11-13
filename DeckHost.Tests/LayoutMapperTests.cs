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

    [Fact]
    public void Columns_are_counted_from_the_first_row()
    {
        Assert.Equal(3, LayoutMapper.ColumnsOf(Mini));
        Assert.Equal(5, LayoutMapper.ColumnsOf(Mk2));
        Assert.Equal(4, LayoutMapper.ColumnsOf(Plus));
    }

    [Fact]
    public void Keys_keep_row_and_column_on_a_bigger_grid()
    {
        // Mini key.3 is row 1, column 0: on a 5-wide deck that's key.5.
        var result = LayoutMapper.Map([Profile(On("key.3", "a"), On("key.2", "b"))], Mk2, sourceColumns: 3, static n => $"page{n}");

        var bindings = result.Layouts[0].Bindings.ToDictionary(static binding => binding.Action.ActionName, static binding => binding.ControlId);
        Assert.Equal("key.5", bindings["a"]);
        Assert.Equal("key.2", bindings["b"]);
        Assert.Equal(2, result.Placed);
    }

    [Fact]
    public void Keys_that_do_not_fit_go_to_added_pages_and_dials_the_target_lacks_are_left_out()
    {
        var source = Profile([.. Enumerable.Range(0, 15).Select(index => On($"key.{index}", $"k{index}")), On("encoder.0", "dial"), On("touch.swipe", "swipe")]);

        var result = LayoutMapper.Map([source], Mini, sourceColumns: null, static n => $"page{n}");

        Assert.Equal(6, result.Layouts[0].Bindings.Count);          // keys 0–5 in reading order
        Assert.Equal(9, result.MovedToNewPages);
        Assert.Equal(2, result.PagesAdded);
        var page = result.Layouts[1];
        Assert.Equal(("page1", ProfileKinds.Page, "p1"), (page.Id, page.Kind, page.ParentId));
        Assert.Equal(["k6", "k7", "k8", "k9", "k10", "k11"], page.Bindings.Select(static binding => binding.Action.ActionName));
        Assert.Equal(["key.0", "key.1", "key.2"], result.Layouts[2].Bindings.Select(static binding => binding.ControlId));
        Assert.Equal(2, result.Dropped);
        Assert.Contains("left out", result.Summary);
    }

    [Fact]
    public void Dials_and_touch_zones_keep_their_number_where_they_exist()
    {
        var result = LayoutMapper.Map([Profile(On("encoder.2", "dial"), On("touch.1", "zone"), On("encoder.3", "last"))], Plus, sourceColumns: 5, static n => $"page{n}");
        Assert.Equal(["encoder.2", "touch.1", "encoder.3"], result.Layouts[0].Bindings.Select(static binding => binding.ControlId));
    }

    [Fact]
    public void A_copy_gets_new_ids_for_its_device_and_folder_keys_follow()
    {
        var folderKey = On("key.0", "Folder") with { Action = new ActionReference("p", "Plugin", "folder", "Folder", InputCapabilities.Press, OutputCapabilities.None, new ActionSettings([new("folder", "f1")])) };
        var copy = ProfileBundle.Copy([Profile(folderKey), new DeviceProfile("f1", "Folder", "from", []) { ParentId = "p1", Kind = ProfileKinds.Folder }], "to");

        Assert.All(copy, static layout => Assert.Equal("to", layout.DeviceId));
        Assert.NotEqual("p1", copy[0].Id);
        Assert.Equal(copy[1].Id, copy[0].Bindings[0].Action.EffectiveSettings.Get("folder"));
        Assert.Equal(copy[0].Id, copy[1].ParentId);
    }
}
