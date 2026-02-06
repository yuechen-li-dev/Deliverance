using System.Buffers.Binary;
using System.Text;

namespace Deliverance.Core.Format;

internal static class SaveContainerWriter
{
    // Simple container:
    // magic(4) = "DLVR"
    // containerVersion(int32)
    // utcUnixSeconds(int64)
    // buildIdLen(int32) + utf8 bytes (or -1 for null)
    // chunkCount(int32)
    // directory entries:
    //   keyLen(int32) + key utf8
    //   moduleVersion(int32)
    //   codecId(byte)
    //   offset(int64)
    //   length(int32)
    // payload blobs concatenated

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DLVR");

    public static byte[] Write(SaveHeader header, IReadOnlyList<ChunkEntry> directory, IReadOnlyList<ReadOnlyMemory<byte>> payloads)
    {
        if (directory.Count != payloads.Count)
            throw new InvalidOperationException("Directory/payload count mismatch.");

        using var ms = new MemoryStream(capacity: 1024);

        // prefix
        var prefix = WritePrefixOnly(header, directory);
        ms.Write(prefix);

        // payloads
        for (int i = 0; i < payloads.Count; i++)
            ms.Write(payloads[i].Span);

        return ms.ToArray();
    }

    public static byte[] WritePrefixOnly(SaveHeader header, IReadOnlyList<ChunkEntry> directory)
    {
        using var ms = new MemoryStream(capacity: 512);

        WriteBytes(ms, Magic);
        WriteInt32(ms, header.ContainerVersion);
        WriteInt64(ms, header.UtcUnixSeconds);
        WriteString(ms, header.BuildId);

        WriteInt32(ms, directory.Count);

        for (int i = 0; i < directory.Count; i++)
        {
            var e = directory[i];
            WriteString(ms, e.Key);
            WriteInt32(ms, e.ModuleVersion);
            ms.WriteByte(e.CodecId);
            WriteInt64(ms, e.Offset);
            WriteInt32(ms, e.Length);
        }

        return ms.ToArray();
    }

    private static void WriteBytes(Stream s, ReadOnlySpan<byte> bytes) => s.Write(bytes);

    private static void WriteInt32(Stream s, int v)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, v);
        s.Write(buf);
    }

    private static void WriteInt64(Stream s, long v)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, v);
        s.Write(buf);
    }

    private static void WriteString(Stream s, string? str)
    {
        if (str is null)
        {
            WriteInt32(s, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(str);
        WriteInt32(s, bytes.Length);
        s.Write(bytes);
    }
}
