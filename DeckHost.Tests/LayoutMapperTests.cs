using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Profiles;
using DeckHost.Device.Elgato;

namespace DeckHost.Tests;

public class LayoutMapperTests
{
    private const int MiniProductId = 0x0063;
    private const int Mk2ProductId = 0x0080;
    private const int PlusProductId = 0x0084;
    private const int MiniColumns = 3;
    private const int Mk2Columns = 5;
    private const int PlusColumns = 4;
    private const int SourceDeviceKeyCount = 15;
    private const string FolderId = "f1";
    private const string ProfileId = "p1";
    private const string FromDevice = "from";
    private const string ToDevice = "to";

    private static readonly IReadOnlyList<PhysicalControl> Mini = StreamDeckModel.Find(MiniProductId)!.Controls();      // 3 × 2
    private static readonly IReadOnlyList<PhysicalControl> Mk2 = StreamDeckModel.Find(Mk2ProductId)!.Controls();       // 5 × 3
    private static readonly IReadOnlyList<PhysicalControl> Plus = StreamDeckModel.Find(PlusProductId)!.Controls();      // 4 × 2 + dials

    private static ControlBinding On(string controlId, string name) =>
        new(controlId, ProfileActionKind.Single, new ActionReference("p", "Plugin", name, name, InputCapabilities.Press, OutputCapabilities.None, new ActionSettings([])), new ControlAppearance(), []);

    private static DeviceProfile Profile(params ControlBinding[] bindings) => new(ProfileId, "Work", FromDevice, bindings);

    private static string PageName(int number) => $"page{number}";

    // ---------------------------------------------------------------- columns

    [TestCase(MiniProductId, MiniColumns)]
    [TestCase(Mk2ProductId, Mk2Columns)]
    [TestCase(PlusProductId, PlusColumns)]
    public void ColumnsOf_ADevicesControls_CountsTheFirstRow(int productId, int expected)
    {
        // Arrange
        var controls = StreamDeckModel.Find(productId)!.Controls();

        // Act
        var columns = LayoutMapper.ColumnsOf(controls);

        // Assert
        Assert.That(columns, Is.EqualTo(expected));
    }

    // ---------------------------------------------------------------- keys

    [Test]
    public void Map_KeysOnABiggerGrid_KeepTheirRowAndColumn()
    {
        // Arrange (Mini key.3 is row 1, column 0: on a 5-wide deck that's key.5)
        var source = Profile(On("key.3", "a"), On("key.2", "b"));

        // Act
        var result = LayoutMapper.Map([source], Mk2, sourceColumns: MiniColumns, PageName);

        // Assert
        var bindings = result.Layouts[0].Bindings.ToDictionary(static binding => binding.Action.ActionName, static binding => binding.ControlId);
        Assert.That(bindings["a"], Is.EqualTo("key.5"));
        Assert.That(bindings["b"], Is.EqualTo("key.2"));
        Assert.That(result.Placed, Is.EqualTo(2));
    }

    [Test]
    public void Map_MoreKeysThanTheTargetHas_PutsTheFirstOnesInReadingOrderOnTheFirstPage()
    {
        // Arrange
        var source = Profile([.. Enumerable.Range(0, SourceDeviceKeyCount).Select(index => On($"key.{index}", $"k{index}"))]);

        // Act
        var result = LayoutMapper.Map([source], Mini, sourceColumns: null, PageName);

        // Assert
        Assert.That(result.Layouts[0].Bindings.Count, Is.EqualTo(Mini.Count));
    }

    [Test]
    public void Map_MoreKeysThanTheTargetHas_MovesTheRestOntoAddedPages()
    {
        // Arrange
        var source = Profile([.. Enumerable.Range(0, SourceDeviceKeyCount).Select(index => On($"key.{index}", $"k{index}"))]);

        // Act
        var result = LayoutMapper.Map([source], Mini, sourceColumns: null, PageName);

        // Assert
        var page = result.Layouts[1];
        Assert.That(result.MovedToNewPages, Is.EqualTo(9));
        Assert.That(result.PagesAdded, Is.EqualTo(2));
        Assert.That((page.Id, page.Kind, page.ParentId), Is.EqualTo(("page1", ProfileKinds.Page, ProfileId)));
        Assert.That(page.Bindings.Select(static binding => binding.Action.ActionName), Is.EqualTo(new[] { "k6", "k7", "k8", "k9", "k10", "k11" }));
        Assert.That(result.Layouts[2].Bindings.Select(static binding => binding.ControlId), Is.EqualTo(new[] { "key.0", "key.1", "key.2" }));
    }

    [Test]
    public void Map_DialsAndSwipesTheTargetLacks_AreLeftOutAndSaidSo()
    {
        // Arrange
        var source = Profile(On("key.0", "k0"), On("encoder.0", "dial"), On("touch.swipe", "swipe"));

        // Act
        var result = LayoutMapper.Map([source], Mini, sourceColumns: null, PageName);

        // Assert
        Assert.That(result.Dropped, Is.EqualTo(2));
        Assert.That(result.Summary, Does.Contain("left out"));
    }

    [Test]
    public void Map_DialsAndTouchZones_KeepTheirNumberWhereTheyExist()
    {
        // Arrange
        var source = Profile(On("encoder.2", "dial"), On("touch.1", "zone"), On("encoder.3", "last"));

        // Act
        var result = LayoutMapper.Map([source], Plus, sourceColumns: Mk2Columns, PageName);

        // Assert
        Assert.That(result.Layouts[0].Bindings.Select(static binding => binding.ControlId), Is.EqualTo(new[] { "encoder.2", "touch.1", "encoder.3" }));
    }

    // ---------------------------------------------------------------- copying to another device

    private static (ControlBinding FolderKey, DeviceProfile Folder) ProfileWithAFolder()
    {
        var folderKey = On("key.0", "Folder") with { Action = new ActionReference("p", "Plugin", "folder", "Folder", InputCapabilities.Press, OutputCapabilities.None, new ActionSettings([new("folder", FolderId)])) };
        return (folderKey, new DeviceProfile(FolderId, "Folder", FromDevice, []) { ParentId = ProfileId, Kind = ProfileKinds.Folder });
    }

    [Test]
    public void Copy_AProfileWithAFolder_MovesEveryLayoutToTheNewDeviceWithNewIds()
    {
        // Arrange
        var (folderKey, folder) = ProfileWithAFolder();

        // Act
        var copy = ProfileBundle.Copy([Profile(folderKey), folder], ToDevice);

        // Assert
        Expect.All(copy, static layout => Assert.That(layout.DeviceId, Is.EqualTo(ToDevice)));
        Assert.That(copy[0].Id, Is.Not.EqualTo(ProfileId));
    }

    [Test]
    public void Copy_AFolderKey_PointsAtTheCopiedFolder()
    {
        // Arrange
        var (folderKey, folder) = ProfileWithAFolder();

        // Act
        var copy = ProfileBundle.Copy([Profile(folderKey), folder], ToDevice);

        // Assert
        Assert.That(copy[0].Bindings[0].Action.EffectiveSettings.Get("folder"), Is.EqualTo(copy[1].Id));
        Assert.That(copy[1].ParentId, Is.EqualTo(copy[0].Id));
    }
}
