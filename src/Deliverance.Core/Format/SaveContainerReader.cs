using System.Buffers.Binary;
using System.Text;

namespace Deliverance.Core.Format;

internal sealed class SaveContainerReader
{
    public SaveHeader Header { get; }
    public IReadOnlyList<ChunkEntry> Directory { get; }

    private readonly ReadOnlyMemory<byte> _allBytes;

    private SaveContainerReader(SaveHeader header, List<ChunkEntry> directory, ReadOnlyMemory<byte> allBytes)
    {
        Header = header;
        Directory = directory;
        _allBytes = allBytes;
    }

    public ReadOnlyMemory<byte> GetPayload(ChunkEntry entry)
    {
        if (entry.Offset < 0 || entry.Offset + entry.Length > _allBytes.Length)
            throw new InvalidDataException($"Chunk '{entry.Key}' points outside the save container.");
        return _allBytes.Slice((int)entry.Offset, entry.Length);
    }

    public static SaveContainerReader Read(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        int pos = 0;

        ReadOnlySpan<byte> magic = ReadBytes(span, ref pos, 4);
        if (!(magic.Length == 4 && magic[0] == (byte)'D' && magic[1] == (byte)'L' && magic[2] == (byte)'V' && magic[3] == (byte)'R'))
            throw new InvalidDataException("Not a Deliverance save container (bad magic).");

        int containerVersion = ReadInt32(span, ref pos);
        long utc = ReadInt64(span, ref pos);
        string? buildId = ReadString(span, ref pos);

        int chunkCount = ReadInt32(span, ref pos);
        if (chunkCount < 0 || chunkCount > 1_000_000) throw new InvalidDataException("Unreasonable chunk count.");

        var dir = new List<ChunkEntry>(chunkCount);
        for (int i = 0; i < chunkCount; i++)
        {
            var key = ReadString(span, ref pos) ?? throw new InvalidDataException("Chunk key cannot be null.");
            int moduleVersion = ReadInt32(span, ref pos);
            byte codecId = span[pos++];
            long offset = ReadInt64(span, ref pos);
            int length = ReadInt32(span, ref pos);

            dir.Add(new ChunkEntry(key, moduleVersion, codecId, offset, length));
        }

        var header = new SaveHeader(containerVersion, utc, buildId);
        return new SaveContainerReader(header, dir, bytes);
    }

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> span, ref int pos, int len)
    {
        if (pos + len > span.Length) throw new EndOfStreamException();
        var slice = span.Slice(pos, len);
        pos += len;
        return slice;
    }

    private static int ReadInt32(ReadOnlySpan<byte> span, ref int pos)
    {
        var slice = ReadBytes(span, ref pos, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(slice);
    }

    private static long ReadInt64(ReadOnlySpan<byte> span, ref int pos)
    {
        var slice = ReadBytes(span, ref pos, 8);
        return BinaryPrimitives.ReadInt64LittleEndian(slice);
    }

    private static string? ReadString(ReadOnlySpan<byte> span, ref int pos)
    {
        int len = ReadInt32(span, ref pos);
        if (len == -1) return null;
        if (len < 0) throw new InvalidDataException("Negative string length.");
        var bytes = ReadBytes(span, ref pos, len);
        return Encoding.UTF8.GetString(bytes);
    }
}
