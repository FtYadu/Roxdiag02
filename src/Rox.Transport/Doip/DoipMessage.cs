using System.Buffers.Binary;

namespace Rox.Transport.Doip;

/// <summary>ISO 13400 (DoIP) payload types used by ROX.</summary>
public static class DoipPayloadTypes
{
    public const ushort GenericNegativeAck = 0x0000;
    public const ushort VehicleIdentificationRequest = 0x0001;
    public const ushort VehicleIdentificationResponse = 0x0004; // announcement / identification
    public const ushort RoutingActivationRequest = 0x0005;
    public const ushort RoutingActivationResponse = 0x0006;
    public const ushort AliveCheckRequest = 0x0007;
    public const ushort AliveCheckResponse = 0x0008;
    public const ushort DiagnosticMessage = 0x8001;
    public const ushort DiagnosticMessagePositiveAck = 0x8002;
    public const ushort DiagnosticMessageNegativeAck = 0x8003;
}

/// <summary>Routing-activation response codes (subset).</summary>
public static class DoipRoutingResponse
{
    public const byte Success = 0x10;
    public const byte UnknownSourceAddress = 0x00;
    public const byte AllSocketsRegistered = 0x01;
    public const byte SourceAddressMismatch = 0x02;
    public const byte RoutingActivationDenied = 0x06;
}

/// <summary>
/// A single DoIP message: an 8-byte header (protocol version + inverse, 16-bit payload type,
/// 32-bit payload length) followed by the payload. Big-endian on the wire per ISO 13400.
/// </summary>
public sealed class DoipMessage
{
    public const int HeaderLength = 8;

    public byte ProtocolVersion { get; init; } = 0x02; // ISO 13400-2:2012
    public ushort PayloadType { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();

    public byte[] ToBytes()
    {
        var buf = new byte[HeaderLength + Payload.Length];
        buf[0] = ProtocolVersion;
        buf[1] = (byte)~ProtocolVersion;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), PayloadType);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), (uint)Payload.Length);
        Array.Copy(Payload, 0, buf, HeaderLength, Payload.Length);
        return buf;
    }

    /// <summary>Parse a header; returns the declared payload length so the caller can read the body.</summary>
    public static (byte version, ushort payloadType, uint payloadLength) ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength) throw new DoipException("Short DoIP header.");
        byte version = header[0];
        byte inverse = header[1];
        if ((byte)~version != inverse) throw new DoipException("DoIP header protocol-version / inverse mismatch.");
        ushort type = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2, 2));
        uint len = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        return (version, type, len);
    }

    // ---- Payload builders ----

    public static DoipMessage RoutingActivationRequest(ushort sourceAddress, byte activationType = 0x00, byte version = 0x02)
    {
        // sourceAddress(2) + activationType(1) + reserved(4) [+ oem(4) optional, omitted]
        var p = new byte[7];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0, 2), sourceAddress);
        p[2] = activationType;
        return new DoipMessage { ProtocolVersion = version, PayloadType = DoipPayloadTypes.RoutingActivationRequest, Payload = p };
    }

    public static DoipMessage RoutingActivationResponse(ushort testerAddr, ushort entityAddr, byte code, byte version = 0x02)
    {
        // testerAddr(2) + entityAddr(2) + code(1) + reserved(4)
        var p = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0, 2), testerAddr);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2, 2), entityAddr);
        p[4] = code;
        return new DoipMessage { ProtocolVersion = version, PayloadType = DoipPayloadTypes.RoutingActivationResponse, Payload = p };
    }

    public static DoipMessage Diagnostic(ushort source, ushort target, byte[] uds, byte version = 0x02)
    {
        var p = new byte[4 + uds.Length];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0, 2), source);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2, 2), target);
        Array.Copy(uds, 0, p, 4, uds.Length);
        return new DoipMessage { ProtocolVersion = version, PayloadType = DoipPayloadTypes.DiagnosticMessage, Payload = p };
    }

    public static DoipMessage DiagnosticAck(ushort source, ushort target, byte ackCode, bool positive, byte version = 0x02)
    {
        var p = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0, 2), source);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2, 2), target);
        p[4] = ackCode;
        return new DoipMessage
        {
            ProtocolVersion = version,
            PayloadType = positive ? DoipPayloadTypes.DiagnosticMessagePositiveAck : DoipPayloadTypes.DiagnosticMessageNegativeAck,
            Payload = p
        };
    }

    /// <summary>Extract the UDS bytes from a 0x8001 diagnostic-message payload (drops source/target).</summary>
    public byte[] DiagnosticUds() => Payload.Length <= 4 ? Array.Empty<byte>() : Payload[4..];
}

public sealed class DoipException : Exception
{
    public DoipException(string message) : base(message) { }
}
