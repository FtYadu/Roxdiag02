namespace Rox.Core;

/// <summary>Parsed UDS response: positive vs negative, with NRC extraction.</summary>
public sealed class UdsResponse
{
    public required byte[] Raw { get; init; }
    public bool IsNegative { get; init; }
    public byte RequestSid { get; init; }
    public byte Nrc { get; init; }

    public bool IsPending => IsNegative && Nrc == Core.Nrc.ResponsePending;
    public bool IsPositive => !IsNegative;
    public byte ResponseSid => Raw.Length > 0 ? Raw[0] : (byte)0;
    public ReadOnlySpan<byte> Payload => Raw.Length > 1 ? Raw.AsSpan(1) : ReadOnlySpan<byte>.Empty;

    public static UdsResponse Parse(byte[] raw)
    {
        if (raw is null || raw.Length == 0)
            return new UdsResponse { Raw = raw ?? Array.Empty<byte>(), IsNegative = true, Nrc = Core.Nrc.GeneralReject };
        if (raw[0] == UdsServices.NegativeResponseCode && raw.Length >= 3)
            return new UdsResponse { Raw = raw, IsNegative = true, RequestSid = raw[1], Nrc = raw[2] };
        return new UdsResponse { Raw = raw, IsNegative = false };
    }
}
