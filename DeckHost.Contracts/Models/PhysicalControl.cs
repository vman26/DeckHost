using DeckHost.Contracts.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Contracts.Models
{
    public sealed record PhysicalControl(
        string Id,
        string DisplayName,
        ControlType Type,
        Rect Bounds,
        InputCapabilities Inputs,
        OutputCapabilities Outputs);
}
