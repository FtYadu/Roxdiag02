namespace Rox.Transport.Can;

/// <summary>
/// ISO 15765-2 (ISO-TP) transport over a raw <see cref="ICanChannel"/>. Implemented in-house per the
/// PRD (§8.1): DTC lists, DIDs and flash blocks exceed a single 8-byte CAN frame, so First/Consecutive
/// frames, Flow-Control frames, and BlockSize / STmin handling are mandatory.
///
/// PCI (Protocol Control Information) nibble in byte 0:
///   0x0n  Single Frame   — n = length (0..7)
///   0x1n  First Frame    — 12-bit length across bytes 0..1, then 6 data bytes
///   0x2n  Consecutive    — n = sequence number 0x0..0xF (wraps)
///   0x3n  Flow Control   — n = flow status (0 CTS / 1 WAIT / 2 OVFLW), byte1 = BS, byte2 = STmin
/// This is the classic-addressing variant (no extended/mixed addressing byte).
/// </summary>
public sealed class IsoTpChannel
{
    private readonly ICanChannel _can;
    private readonly uint _txId;
    private readonly uint _rxId;
    private readonly IsoTpConfig _cfg;

    public IsoTpChannel(ICanChannel can, uint txId, uint rxId, IsoTpConfig? cfg = null)
    {
        _can = can;
        _txId = txId;
        _rxId = rxId;
        _cfg = cfg ?? new IsoTpConfig();
    }

    private const byte PciSingle = 0x00;
    private const byte PciFirst = 0x10;
    private const byte PciConsecutive = 0x20;
    private const byte PciFlowControl = 0x30;

    private const byte FsClearToSend = 0x0;
    private const byte FsWait = 0x1;
    private const byte FsOverflow = 0x2;

    /// <summary>Segment and transmit a full UDS PDU, honouring the receiver's flow control.</summary>
    public async Task SendPduAsync(byte[] pdu, CancellationToken ct = default)
    {
        if (pdu.Length <= 7)
        {
            var sf = new byte[Math.Max(pdu.Length + 1, 1)];
            sf[0] = (byte)(PciSingle | (pdu.Length & 0x0F));
            Array.Copy(pdu, 0, sf, 1, pdu.Length);
            await _can.WriteAsync(new CanFrame(_txId, Pad(sf)), ct).ConfigureAwait(false);
            return;
        }

        // First Frame: 12-bit length + first 6 bytes.
        if (pdu.Length > 0xFFF) throw new NotSupportedException("PDU exceeds ISO-TP 12-bit length (use 0x36 block sizing to stay under 4095B).");
        var ff = new byte[8];
        ff[0] = (byte)(PciFirst | ((pdu.Length >> 8) & 0x0F));
        ff[1] = (byte)(pdu.Length & 0xFF);
        Array.Copy(pdu, 0, ff, 2, 6);
        await _can.WriteAsync(new CanFrame(_txId, ff), ct).ConfigureAwait(false);

        int offset = 6;
        byte seq = 1;
        // Wait for the initial Flow Control from the receiver.
        var (fs, bs, stMin) = await ReadFlowControlAsync(ct).ConfigureAwait(false);
        if (fs == FsOverflow) throw new IsoTpException("Receiver reported buffer overflow (FC OVFLW).");

        int framesUntilFc = bs == 0 ? int.MaxValue : bs;
        while (offset < pdu.Length)
        {
            if (fs == FsWait)
            {
                (fs, bs, stMin) = await ReadFlowControlAsync(ct).ConfigureAwait(false);
                framesUntilFc = bs == 0 ? int.MaxValue : bs;
                continue;
            }

            int chunk = Math.Min(7, pdu.Length - offset);
            var cf = new byte[8];
            cf[0] = (byte)(PciConsecutive | (seq & 0x0F));
            Array.Copy(pdu, offset, cf, 1, chunk);
            await _can.WriteAsync(new CanFrame(_txId, cf), ct).ConfigureAwait(false);
            offset += chunk;
            seq = (byte)((seq + 1) & 0x0F);

            if (--framesUntilFc <= 0 && offset < pdu.Length)
            {
                (fs, bs, stMin) = await ReadFlowControlAsync(ct).ConfigureAwait(false);
                if (fs == FsOverflow) throw new IsoTpException("Receiver reported buffer overflow mid-transfer.");
                framesUntilFc = bs == 0 ? int.MaxValue : bs;
            }
            else if (stMin > 0)
            {
                await DelayStMinAsync(stMin, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Receive and reassemble a full UDS PDU, emitting Flow Control as required.</summary>
    public async Task<byte[]> ReceivePduAsync(CancellationToken ct = default)
    {
        var first = await ReadFrameAsync(ct).ConfigureAwait(false);
        byte pci = (byte)(first.Data[0] & 0xF0);

        if (pci == PciSingle)
        {
            int len = first.Data[0] & 0x0F;
            return first.Data.Skip(1).Take(len).ToArray();
        }

        if (pci != PciFirst) throw new IsoTpException($"Expected SF or FF, got PCI 0x{pci:X2}.");

        int total = ((first.Data[0] & 0x0F) << 8) | first.Data[1];
        var buffer = new byte[total];
        int got = Math.Min(6, total);
        Array.Copy(first.Data, 2, buffer, 0, got);

        // Send Flow Control: Clear To Send, our configured BS/STmin.
        await SendFlowControlAsync(FsClearToSend, _cfg.BlockSize, _cfg.StMin, ct).ConfigureAwait(false);

        byte expectedSeq = 1;
        int sinceFc = 0;
        while (got < total)
        {
            var cf = await ReadFrameAsync(ct).ConfigureAwait(false);
            if ((cf.Data[0] & 0xF0) != PciConsecutive) throw new IsoTpException("Expected a Consecutive Frame.");
            if ((cf.Data[0] & 0x0F) != expectedSeq) throw new IsoTpException($"Out-of-order CF: expected {expectedSeq}, got {cf.Data[0] & 0x0F}.");
            int chunk = Math.Min(7, total - got);
            Array.Copy(cf.Data, 1, buffer, got, chunk);
            got += chunk;
            expectedSeq = (byte)((expectedSeq + 1) & 0x0F);

            if (_cfg.BlockSize != 0 && ++sinceFc >= _cfg.BlockSize && got < total)
            {
                await SendFlowControlAsync(FsClearToSend, _cfg.BlockSize, _cfg.StMin, ct).ConfigureAwait(false);
                sinceFc = 0;
            }
        }
        return buffer;
    }

    private async Task<(byte fs, byte bs, byte stMin)> ReadFlowControlAsync(CancellationToken ct)
    {
        var f = await ReadFrameAsync(ct).ConfigureAwait(false);
        if ((f.Data[0] & 0xF0) != PciFlowControl) throw new IsoTpException($"Expected Flow Control, got 0x{f.Data[0]:X2}.");
        return ((byte)(f.Data[0] & 0x0F), f.Data[1], f.Data[2]);
    }

    private Task SendFlowControlAsync(byte fs, byte bs, byte stMin, CancellationToken ct)
    {
        var fc = new byte[8];
        fc[0] = (byte)(PciFlowControl | (fs & 0x0F));
        fc[1] = bs;
        fc[2] = stMin;
        return _can.WriteAsync(new CanFrame(_txId, fc), ct);
    }

    private async Task<CanFrame> ReadFrameAsync(CancellationToken ct)
    {
        // Filter to our rx id; ignore foreign traffic on a shared bus.
        while (true)
        {
            var f = await _can.ReadAsync(ct).ConfigureAwait(false);
            if (f.Id == _rxId && f.Data.Length > 0) return f;
        }
    }

    private static async Task DelayStMinAsync(byte stMin, CancellationToken ct)
    {
        // 0x00-0x7F = milliseconds; 0xF1-0xF9 = 100-900 microseconds. Round sub-ms up to ~0.
        if (stMin <= 0x7F) { if (stMin > 0) await Task.Delay(stMin, ct).ConfigureAwait(false); }
        else await Task.Yield();
    }

    private static byte[] Pad(byte[] data)
    {
        if (data.Length == 8) return data;
        var p = new byte[8];
        Array.Copy(data, p, data.Length);
        // ISO-TP padding byte is typically 0xCC or 0x00; use 0x00 (both are common, ECU-dependent).
        return p;
    }
}

public sealed class IsoTpConfig
{
    /// <summary>Flow-control BlockSize we advertise as a receiver. 0 = send all without further FC.</summary>
    public byte BlockSize { get; init; } = 0;

    /// <summary>Flow-control STmin (min separation) we advertise as a receiver. 0 = as fast as possible.</summary>
    public byte StMin { get; init; } = 0;
}

public sealed class IsoTpException : Exception
{
    public IsoTpException(string message) : base(message) { }
}
