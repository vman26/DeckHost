using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Contracts.Models;

public sealed record DeviceDefinition(
    string Id,
    string Name,
    IReadOnlyCollection<PhysicalControl> Controls,
    int ScreenWidth = 0,
    int ScreenHeight = 0,
    DeviceCapabilities? Capabilities = null)
{
    /// <summary>
    /// Maps a logical key index (0-based) to the actual SDK key ID.
    /// </summary>
    public byte GetSdkKeyId(int logicalKeyIndex)
    {
        if (Capabilities == null)
            return (byte)logicalKeyIndex; // Fallback if no capabilities

        return (byte)(Capabilities.MinKeyId + logicalKeyIndex);
    }
}
