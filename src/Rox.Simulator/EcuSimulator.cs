using Rox.Core;
using Rox.FlowEngine;

namespace Rox.Simulator;

/// <summary>In-process UDS/ECU server so the whole suite runs end-to-end with no vehicle.</summary>
public sealed class EcuSimulator : IEcuServiceExecutor
{
    private bool _securityGranted;
    private byte[]? _lastSeed;
    private int _keyCount = 2;
    private int _blockCounter;
    private readonly List<(byte[] dtc, byte status)> _dtcs = new()
    {
        (new byte[] { 0x03, 0x01, 0x00 }, 0x08), // P0301 confirmed
        (new byte[] { 0xC1, 0x23, 0x00 }, 0x04), // pending
    };

    public int KeyCount => _keyCount;
    public bool SecurityGranted => _securityGranted;

    public byte[] Execute(string? ecu, byte[] request)
    {
        if (request.Length == 0) return Neg(0x00, Nrc.GeneralReject);
        byte sid = request[0];
        return sid switch
        {
            UdsServices.DiagnosticSessionControl => Positive(sid, request.Length > 1 ? request[1] : (byte)0x01),
            UdsServices.TesterPresent            => Positive(sid, 0x00),
            UdsServices.ControlDtcSetting        => Positive(sid, request.Length > 1 ? request[1] : (byte)0x00),
            UdsServices.ReadDtcInformation       => ReadDtc(request),
            UdsServices.ClearDiagnosticInformation => ClearDtc(),
            UdsServices.SecurityAccess           => Security(request),
            UdsServices.ReadDataByIdentifier     => ReadDid(request),
            UdsServices.WriteDataByIdentifier    => request.Length >= 3 ? Positive(sid, request[1], request[2]) : Neg(sid, Nrc.IncorrectMessageLengthOrInvalidFormat),
            UdsServices.RoutineControl           => Routine(request),
            UdsServices.RequestDownload          => RequestDownload(),
            UdsServices.TransferData             => TransferData(request),
            UdsServices.RequestTransferExit      => Positive(sid),
            _ => Neg(sid, Nrc.ServiceNotSupported)
        };
    }

    private byte[] ReadDtc(byte[] req)
    {
        if (req.Length < 2 || req[1] != 0x02) return Neg(UdsServices.ReadDtcInformation, Nrc.SubFunctionNotSupported);
        var o = new List<byte> { 0x59, 0x02, 0xFF };
        foreach (var (dtc, status) in _dtcs) { o.AddRange(dtc); o.Add(status); }
        return o.ToArray();
    }

    private byte[] ClearDtc()
    {
        _dtcs.Clear();
        _dtcs.Add((new byte[] { 0x03, 0x01, 0x00 }, 0x08)); // P0301 re-sets immediately => a live fault
        return Positive(UdsServices.ClearDiagnosticInformation);
    }

    private byte[] Security(byte[] req)
    {
        if (req.Length < 2) return Neg(UdsServices.SecurityAccess, Nrc.IncorrectMessageLengthOrInvalidFormat);
        byte sub = req[1];
        if ((sub & 0x01) == 1) { _lastSeed = new byte[] { 0x11, 0x22, 0x33, 0x44 }; return Positive(UdsServices.SecurityAccess, Prepend(sub, _lastSeed)); }
        var key = req.Skip(2).ToArray();
        if (key.SequenceEqual(TestSeedKey.Compute(_lastSeed ?? Array.Empty<byte>()))) { _securityGranted = true; return Positive(UdsServices.SecurityAccess, sub); }
        return Neg(UdsServices.SecurityAccess, Nrc.InvalidKey);
    }

    private byte[] ReadDid(byte[] req)
    {
        if (req.Length < 3) return Neg(UdsServices.ReadDataByIdentifier, Nrc.IncorrectMessageLengthOrInvalidFormat);
        byte hi = req[1], lo = req[2];
        byte value = (hi == 0xF1 && lo == 0x8C) ? (byte)_keyCount : (byte)0x00; // 0xF18C = key count DID
        return Positive(UdsServices.ReadDataByIdentifier, hi, lo, value);
    }

    private byte[] Routine(byte[] req)
    {
        if (req.Length < 4) return Neg(UdsServices.RoutineControl, Nrc.IncorrectMessageLengthOrInvalidFormat);
        byte type = req[1], rHi = req[2], rLo = req[3];
        if (type == 0x01 && rHi == 0x02 && rLo == 0x01) // key-learn routine (pairing / duplication)
        {
            if (!_securityGranted) return Neg(UdsServices.RoutineControl, Nrc.SecurityAccessDenied);
            _keyCount++;
            return Positive(UdsServices.RoutineControl, type, rHi, rLo);
        }
        if (type == 0x01 && rHi == 0x02 && rLo == 0x02) // key-delete routine (additive extension)
        {
            if (!_securityGranted) return Neg(UdsServices.RoutineControl, Nrc.SecurityAccessDenied);
            if (_keyCount > 0) _keyCount--;
            return Positive(UdsServices.RoutineControl, type, rHi, rLo);
        }
        if (type == 0x01 && rHi == 0xFF && rLo == 0x01) // checkMemory routine (additive): compare provided sum32
        {
            if (req.Length >= 8)
            {
                uint expected = (uint)((req[4] << 24) | (req[5] << 16) | (req[6] << 8) | req[7]);
                if (expected != FlashChecksum) return Neg(UdsServices.RoutineControl, Nrc.GeneralProgrammingFailure);
            }
            return Positive(UdsServices.RoutineControl, type, rHi, rLo, 0x00); // routineInfo 0x00 = OK
        }
        if (type == 0x03) return Positive(UdsServices.RoutineControl, type, rHi, rLo, 0x00); // complete
        return Positive(UdsServices.RoutineControl, type, rHi, rLo);
    }

    private readonly List<byte> _flashBuffer = new(); // additive: accumulates transferred firmware for checkMemory

    /// <summary>Additive 32-bit sum of all transferred bytes (matches Rox.Reflash's checksum).</summary>
    public uint FlashChecksum { get { uint s = 0; foreach (var b in _flashBuffer) s += b; return s; } }

    private byte[] RequestDownload()
    {
        if (!_securityGranted) return Neg(UdsServices.RequestDownload, Nrc.SecurityAccessDenied);
        _blockCounter = 1;
        _flashBuffer.Clear();
        return Positive(UdsServices.RequestDownload, 0x20, 0x01, 0x02); // maxNumberOfBlockLength = 0x0102
    }

    private byte[] TransferData(byte[] req)
    {
        if (req.Length < 2) return Neg(UdsServices.TransferData, Nrc.IncorrectMessageLengthOrInvalidFormat);
        if (req[1] != (byte)_blockCounter) return Neg(UdsServices.TransferData, Nrc.WrongBlockSequenceCounter);
        byte bsc = req[1];
        if (req.Length > 2) _flashBuffer.AddRange(req.Skip(2)); // store payload for checksum verification
        _blockCounter = (_blockCounter + 1) & 0xFF;
        return Positive(UdsServices.TransferData, bsc);
    }

    private static byte[] Positive(byte sid, params byte[] tail)
    {
        var r = new byte[1 + tail.Length];
        r[0] = (byte)(sid + UdsServices.PositiveResponseOffset);
        Array.Copy(tail, 0, r, 1, tail.Length);
        return r;
    }
    private static byte[] Neg(byte sid, byte nrc) => new byte[] { UdsServices.NegativeResponseCode, sid, nrc };
    private static byte[] Prepend(byte first, byte[] rest) { var r = new byte[1 + rest.Length]; r[0] = first; Array.Copy(rest, 0, r, 1, rest.Length); return r; }
}
