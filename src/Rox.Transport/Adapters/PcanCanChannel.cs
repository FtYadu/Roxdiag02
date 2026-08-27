using System.Runtime.InteropServices;
using Rox.Transport.Can;

namespace Rox.Transport.Adapters;

/// <summary>
/// Reference vendor CAN adapter: PEAK PCAN-Basic (<c>PCANBasic.dll</c>) via P/Invoke. This is the
/// documented reference path (PRD §8.1). It compiles on any OS — the DLL is only bound at call time —
/// but real traffic requires PCAN hardware, its driver, and <c>PCANBasic.dll</c> on Windows.
///
/// TODO(hardware): validate against a physical PCAN-USB adapter on the R11_Oversea CAN bus
/// (see docs/EXTERNAL_INPUTS.md). The ISO-TP layer above (<see cref="IsoTpCanTransport"/>) is
/// hardware-independent and is validated on the loopback fabric.
/// </summary>
public sealed class PcanCanChannel : ICanChannel
{
    private readonly ushort _handle;      // TPCANHandle, e.g. PCAN_USBBUS1 = 0x51
    private readonly ushort _baudCode;    // TPCANBaudrate, e.g. PCAN_BAUD_500K = 0x001C
    private readonly int _pollDelayMs;

    public PcanCanChannel(ushort channelHandle = Pcan.PCAN_USBBUS1, int baudrate = 500_000, int pollDelayMs = 1)
    {
        _handle = channelHandle;
        _baudCode = Pcan.BaudCode(baudrate);
        Baudrate = baudrate;
        _pollDelayMs = pollDelayMs;
    }

    public string Name => $"PCAN-USB (handle 0x{_handle:X2})";
    public int Baudrate { get; }
    public bool IsOpen { get; private set; }

    public Task OpenAsync(CancellationToken ct = default)
    {
        var status = Pcan.CAN_Initialize(_handle, _baudCode, 0, 0, 0);
        if (status != Pcan.PCAN_ERROR_OK)
            throw new InvalidOperationException($"PCAN_Initialize failed: 0x{status:X}. Is PCANBasic.dll present and the adapter connected?");
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        if (IsOpen) { Pcan.CAN_Uninitialize(_handle); IsOpen = false; }
        return Task.CompletedTask;
    }

    public Task WriteAsync(CanFrame frame, CancellationToken ct = default)
    {
        var msg = new Pcan.TPCANMsg { ID = frame.Id, MSGTYPE = frame.Extended ? Pcan.PCAN_MESSAGE_EXTENDED : Pcan.PCAN_MESSAGE_STANDARD, LEN = (byte)frame.Data.Length, DATA = new byte[8] };
        Array.Copy(frame.Data, msg.DATA, frame.Data.Length);
        var status = Pcan.CAN_Write(_handle, ref msg);
        if (status != Pcan.PCAN_ERROR_OK) throw new InvalidOperationException($"PCAN_Write failed: 0x{status:X}.");
        return Task.CompletedTask;
    }

    public async Task<CanFrame> ReadAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var status = Pcan.CAN_Read(_handle, out var msg, out _);
            if (status == Pcan.PCAN_ERROR_OK)
            {
                var data = new byte[msg.LEN];
                Array.Copy(msg.DATA, data, msg.LEN);
                return new CanFrame(msg.ID, data, (msg.MSGTYPE & Pcan.PCAN_MESSAGE_EXTENDED) != 0);
            }
            if ((status & Pcan.PCAN_ERROR_QRCVEMPTY) == 0)
                throw new InvalidOperationException($"PCAN_Read failed: 0x{status:X}.");
            await Task.Delay(_pollDelayMs, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}

/// <summary>Minimal PCAN-Basic P/Invoke surface (subset used by ROX).</summary>
internal static class Pcan
{
    public const ushort PCAN_USBBUS1 = 0x51;
    public const uint PCAN_ERROR_OK = 0x00000;
    public const uint PCAN_ERROR_QRCVEMPTY = 0x00020;
    public const byte PCAN_MESSAGE_STANDARD = 0x00;
    public const byte PCAN_MESSAGE_EXTENDED = 0x02;

    // TPCANBaudrate codes (Btr0Btr1).
    public const ushort PCAN_BAUD_1M = 0x0014;
    public const ushort PCAN_BAUD_500K = 0x001C;
    public const ushort PCAN_BAUD_250K = 0x011C;
    public const ushort PCAN_BAUD_125K = 0x031C;

    public static ushort BaudCode(int baudrate) => baudrate switch
    {
        1_000_000 => PCAN_BAUD_1M,
        500_000 => PCAN_BAUD_500K,
        250_000 => PCAN_BAUD_250K,
        125_000 => PCAN_BAUD_125K,
        _ => PCAN_BAUD_500K
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct TPCANMsg
    {
        public uint ID;
        public byte MSGTYPE;
        public byte LEN;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] DATA;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TPCANTimestamp
    {
        public uint millis;
        public ushort millis_overflow;
        public ushort micros;
    }

    // These bind at call time; absent on non-Windows / without the driver, they throw only when invoked.
    [DllImport("PCANBasic.dll")] public static extern uint CAN_Initialize(ushort channel, ushort btr0btr1, byte hwType, uint ioPort, ushort interrupt);
    [DllImport("PCANBasic.dll")] public static extern uint CAN_Uninitialize(ushort channel);
    [DllImport("PCANBasic.dll")] public static extern uint CAN_Read(ushort channel, out TPCANMsg msg, out TPCANTimestamp ts);
    [DllImport("PCANBasic.dll")] public static extern uint CAN_Write(ushort channel, ref TPCANMsg msg);
}
