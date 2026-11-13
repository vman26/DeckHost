namespace DeckHost.Device.Hid;

/// <summary>
/// Works out where a key or knob shows up in a device's reports, from what the device sent while
/// the user left it alone (<c>idle</c>) and while they pressed the key or turned the knob
/// (<c>active</c>). Bytes that change on their own (sensors, counters, sticks) are ignored.
/// </summary>
public static class HidLearner
{
    /// <summary>The input of a key that was pressed during <paramref name="active"/>; null when nothing changed.</summary>
    public static HidInput? LearnKey(IReadOnlyList<byte[]> idle, IReadOnlyList<byte[]> active)
    {
        if (FirstChange(idle, active) is not var (baseline, pressed, index))
        {
            return null;
        }

        var before = baseline[index];
        var after = pressed[index];
        var steady = ~NoisyBits([.. idle.Where(report => report.Length == pressed.Length && report[0] == pressed[0])], pressed.Length)[index];
        var changed = (before ^ after) & steady;
        var reportId = ReportIdOf(pressed);
        if (System.Numerics.BitOperations.PopCount((uint)changed) == 1)
        {
            // One bit: pressed when it's set (or, for buttons wired the other way, when it's clear).
            return new HidInput(HidInputKind.Bit, index, Mask: changed, ReportId: reportId, Invert: (after & changed) == 0);
        }

        return before == 0 || after != 0
            ? new HidInput(HidInputKind.Value, index, Value: after, ReportId: reportId)
            : new HidInput(HidInputKind.Value, index, Value: before, ReportId: reportId, Invert: true);
    }

    /// <summary>
    /// The input of a knob turned clockwise (to the right) during <paramref name="active"/>: steps when
    /// the byte goes back to 0 between reports with small values, else a position.
    /// </summary>
    public static HidInput? LearnKnob(IReadOnlyList<byte[]> idle, IReadOnlyList<byte[]> active)
    {
        if (FirstChange(idle, active) is not var (baseline, _, index))
        {
            return null;
        }

        var reportId = ReportIdOf(active[0]);
        var values = active.Where(report => index < report.Length && (reportId is null || report[0] == reportId)).Select(report => report[index]).ToList();
        var relative = baseline[index] == 0 && values.Where(static value => value != 0).All(static value => Math.Abs((sbyte)value) <= 16);
        if (relative)
        {
            var sum = values.Sum(static value => (sbyte)value);
            return new HidInput(HidInputKind.Relative, index, ReportId: reportId, Invert: sum < 0);
        }

        var total = 0;
        var last = baseline[index];
        foreach (var value in values)
        {
            total += (sbyte)(byte)(value - last);
            last = value;
        }

        return new HidInput(HidInputKind.Absolute, index, ReportId: reportId, Invert: total < 0);
    }

    /// <summary>The report id a numbered report starts with; null for devices without them (first byte 0).</summary>
    private static int? ReportIdOf(byte[] report) => report.Length > 0 && report[0] != 0 ? report[0] : null;

    /// <summary>
    /// The idle report, the first active report that differs and the first byte with a steady bit that
    /// changed. Noise is tracked per bit, so a key in the same byte as one pressed just before (or a
    /// sensor's low bits) doesn't hide another.
    /// </summary>
    private static (byte[] Baseline, byte[] Changed, int Index)? FirstChange(IReadOnlyList<byte[]> idle, IReadOnlyList<byte[]> active)
    {
        if (active.Count == 0)
        {
            return null;
        }

        foreach (var report in active)
        {
            var reportId = report.Length > 0 ? report[0] : -1;
            var sameKind = idle.Where(candidate => candidate.Length == report.Length && candidate[0] == reportId).ToList();
            var baseline = sameKind.LastOrDefault() ?? new byte[report.Length];
            var noisy = NoisyBits(sameKind, report.Length);
            for (var index = 1; index < report.Length; index++)
            {
                if (((report[index] ^ baseline[index]) & ~noisy[index]) != 0)
                {
                    return (baseline, report, index);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Per byte, the bits that changed while the device was left alone. A byte where more than one bit
    /// moved is a reading (a sensor, a stick): all of it is noise. One bit is a key pressed meanwhile.
    /// </summary>
    internal static int[] NoisyBits(IReadOnlyList<byte[]> reports, int length)
    {
        var noisy = new int[length];
        for (var i = 1; i < reports.Count; i++)
        {
            for (var index = 0; index < length && index < reports[i].Length; index++)
            {
                noisy[index] |= reports[i][index] ^ reports[i - 1][index];
            }
        }

        for (var index = 0; index < length; index++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)noisy[index]) > 1)
            {
                noisy[index] = 0xFF;
            }
        }

        return noisy;
    }
}
