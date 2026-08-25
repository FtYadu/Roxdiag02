namespace Rox.Core;

/// <summary>A decoded Diagnostic Trouble Code with its status bitfield.</summary>
public sealed record Dtc(string Code, byte[] Bytes, byte StatusByte)
{
    public bool TestFailed => (StatusByte & 0x01) != 0;
    public bool Pending => (StatusByte & 0x04) != 0;
    public bool Confirmed => (StatusByte & 0x08) != 0;
    public bool WarningIndicatorRequested => (StatusByte & 0x80) != 0;

    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (Confirmed) parts.Add("Confirmed");
            if (Pending) parts.Add("Pending");
            if (TestFailed) parts.Add("TestFailed");
            if (WarningIndicatorRequested) parts.Add("MIL");
            return parts.Count == 0 ? "None" : string.Join("|", parts);
        }
    }

    public string RawHex => Convert.ToHexString(Bytes);
}

public static class DtcDecoder
{
    /// <summary>Decode the first two DTC bytes to a SAE J2012 5-char code (e.g. P0301).</summary>
    public static string ToJ2012(byte b0, byte b1)
    {
        char letter = ((b0 & 0xC0) >> 6) switch { 0 => 'P', 1 => 'C', 2 => 'B', _ => 'U' };
        int d1 = (b0 & 0x30) >> 4;
        int d2 = b0 & 0x0F;
        return $"{letter}{d1}{d2:X}{b1:X2}";
    }

    /// <summary>Parse a UDS 0x19 0x02 (reportDTCByStatusMask) response into DTCs.</summary>
    public static IReadOnlyList<Dtc> ParseReadDtcByStatusMask(ReadOnlySpan<byte> response)
    {
        var list = new List<Dtc>();
        if (response.Length < 3 || response[0] != 0x59 || response[1] != 0x02) return list;
        int i = 3; // skip 59 02 <availabilityMask>
        while (i + 4 <= response.Length)
        {
            byte b0 = response[i], b1 = response[i + 1], b2 = response[i + 2], status = response[i + 3];
            string code = ToJ2012(b0, b1);
            if (b2 != 0) code += $"-{b2:X2}";
            list.Add(new Dtc(code, new[] { b0, b1, b2 }, status));
            i += 4;
        }
        return list;
    }
}
