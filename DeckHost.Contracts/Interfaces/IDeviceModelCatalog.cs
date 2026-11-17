using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Optional for an <see cref="IDevicePlugin"/>: every model the plugin can drive, each described as
/// an unplugged device (its controls, displays, capabilities). DeckHost's device simulator
/// (<c>DeckHost --simulator</c>) offers these models and behaves like the hardware: it shows the
/// controls, sends their input and shows the images and lights DeckHost sends. A plugin that
/// describes a device it can't see yet (a model table, a saved layout) is a good fit.
/// </summary>
public interface IDeviceModelCatalog
{
    /// <summary>The models, as unplugged devices. Ids only need to be unique within the plugin.</summary>
    IReadOnlyList<DeviceDefinition> DescribeModels();
}
