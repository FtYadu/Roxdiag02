namespace Rox.Transport.Can;

/// <summary>A classic CAN 2.0 data frame (up to 8 bytes). CAN-FD is out of scope for R11_Oversea.</summary>
public readonly struct CanFrame
{
    public uint Id { get; }
    public byte[] Data { get; }
    public bool Extended { get; }

    public CanFrame(uint id, byte[] data, bool extended = false)
    {
        if (data.Length > 8) throw new ArgumentException("Classic CAN frames carry at most 8 bytes.", nameof(data));
        Id = id;
        Data = data;
        Extended = extended;
    }

    public override string ToString() => $"{Id:X3}#{Convert.ToHexString(Data)}";
}

/// <summary>
/// A raw CAN channel: the vendor-adapter seam. PCAN/Kvaser/Vector implement this via P/Invoke;
/// the loopback implements it in memory. ISO-TP (<see cref="IsoTpChannel"/>) is layered on top.
/// </summary>
public interface ICanChannel : IAsyncDisposable
{
    string Name { get; }
    int Baudrate { get; }
    bool IsOpen { get; }

    Task OpenAsync(CancellationToken ct = default);
    Task CloseAsync(CancellationToken ct = default);
    Task WriteAsync(CanFrame frame, CancellationToken ct = default);
    Task<CanFrame> ReadAsync(CancellationToken ct = default);
}
