using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Contracts.Models
{
    public enum ControlType
    {
        Key,
        Encoder,
        TouchSurface,
        Display,
        Slider,

        /// <summary>A physical button without a display of its own.</summary>
        Button,

        /// <summary>A joystick or directional pad.</summary>
        Joystick,

        /// <summary>A light only (an indicator or a lighting zone), no input.</summary>
        Led,
    }
}
