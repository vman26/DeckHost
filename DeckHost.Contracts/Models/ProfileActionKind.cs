namespace DeckHost.Contracts.Models
{
    public enum ProfileActionKind
    {
        Single,
        MultiAction,

        /// <summary>Two sets of steps; each press runs the next set (Elgato's Multi Action Switch).</summary>
        MultiActionSwitch,

        /// <summary>Steps for a press, a double press and holding the key down (Elgato's Key Logic).</summary>
        KeyLogic,
    }
}
