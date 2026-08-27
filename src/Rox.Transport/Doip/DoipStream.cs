using System.Net.Sockets;

namespace Rox.Transport.Doip;

/// <summary>Reads and writes framed <see cref="DoipMessage"/>s over a TCP stream.</summary>
internal static class DoipStream
{
    public static async Task WriteMessageAsync(Stream stream, DoipMessage msg, CancellationToken ct)
    {
        var bytes = msg.ToBytes();
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<DoipMessage?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[DoipMessage.HeaderLength];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return null;
        var (version, type, len) = DoipMessage.ParseHeader(header);
        if (len > 16 * 1024 * 1024) throw new DoipException($"DoIP payload length {len} exceeds sane bound.");
        var payload = new byte[len];
        if (len > 0 && !await ReadExactAsync(stream, payload, ct).ConfigureAwait(false))
            throw new DoipException("Truncated DoIP payload.");
        return new DoipMessage { ProtocolVersion = version, PayloadType = type, Payload = payload };
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int off = 0;
        while (off < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(off), ct).ConfigureAwait(false);
            if (n == 0) return off != 0 ? throw new DoipException("Connection closed mid-message.") : false;
            off += n;
        }
        return true;
    }
}
