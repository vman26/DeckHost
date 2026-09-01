using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Contracts.Models
{
    public sealed record DeviceDefinition(
        string Id,
        string Name,
        IReadOnlyCollection<PhysicalControl> Controls);
}
