using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Contracts.Models
{
    public readonly record struct Rect(
        double X,
        double Y,
        double Width,
        double Height);
}
