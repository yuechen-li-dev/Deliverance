using System.Buffers.Binary;
using System.Text;
using Deliverance.Core.Modules;

namespace Deliverance.Core.Format;

internal sealed class SaveContainerReader
{
    private const int MaximumChunkCount = 100_000;
    private readonly ReadOnlyMemory<byte> allBytes;

    public SaveHeader Header { get; }
    public IReadOnlyList<ChunkEntry> Directory { get; }

    private SaveContainerReader(SaveHeader header, List<ChunkEntry> directory, ReadOnlyMemory<byte> allBytes)
    {
        Header = header;
        Directory = directory;
        this.allBytes = allBytes;
    }

    public ReadOnlyMemory<byte> GetPayload(ChunkEntry entry)
    {
        if (entry.Offset < 0 || entry.Length < 0 || entry.Offset > int.MaxValue || entry.Offset + entry.Length > allBytes.Length)
        {
            throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, $"Module '{entry.Key}' points outside the save container.");
        }
        return allBytes.Slice((int)entry.Offset, entry.Length);
    }

    public static SaveContainerReader Read(ReadOnlyMemory<byte> bytes)
    {
        ReadOnlySpan<byte> span = bytes.Span;
        int position = 0;
        if (!ReadBytes(span, ref position, 4).SequenceEqual("DLVR"u8))
        {
            throw new DeliveranceException(SaveDiagnosticCode.BadMagic, "Not a Deliverance save container: expected DLVR magic.");
        }

        int containerVersion = ReadInt32(span, ref position);
        return containerVersion switch
        {
            1 => ReadV1(bytes, span, ref position),
            DeliveranceOptions.CurrentContainerFormatVersion => ReadV2(bytes, span, ref position),
            _ => throw new DeliveranceException(
                SaveDiagnosticCode.UnsupportedContainerVersion,
                $"Unsupported Deliverance container format version '{containerVersion}'.")
        };
    }

    private static SaveContainerReader ReadV1(ReadOnlyMemory<byte> bytes, ReadOnlySpan<byte> span, ref int position)
    {
        long created = ReadInt64(span, ref position);
        string? buildId = ReadString(span, ref position);
        int count = ReadChunkCount(span, ref position);
        var directory = new List<ChunkEntry>(count);
        for (int index = 0; index < count; index++)
        {
            string key = ReadRequiredString(span, ref position, "module id");
            int schemaVersion = ReadInt32(span, ref position);
            byte compressionId = ReadByte(span, ref position);
            long offset = ReadInt64(span, ref position);
            int length = ReadInt32(span, ref position);
            directory.Add(new ChunkEntry(
                key,
                schemaVersion,
                ModuleCriticality.Required,
                SerializerId: 1,
                compressionId,
                EncryptionId: 0,
                HashId: 0,
                offset,
                length,
                EncryptionMetadata: null,
                HashBytes: null));
        }

        ValidateDirectory(directory, bytes.Length, position);
        return new SaveContainerReader(new SaveHeader(1, created, buildId), directory, bytes);
    }

    private static SaveContainerReader ReadV2(ReadOnlyMemory<byte> bytes, ReadOnlySpan<byte> span, ref int position)
    {
        long created = ReadInt64(span, ref position);
        string? applicationId = ReadString(span, ref position);
        string? applicationVersion = ReadString(span, ref position);
        int applicationSaveVersion = ReadInt32(span, ref position);
        string? buildId = ReadString(span, ref position);
        string? definitionHash = ReadString(span, ref position);
        string? cadenceHash = ReadString(span, ref position);
        byte flags = ReadByte(span, ref position);
        int count = ReadChunkCount(span, ref position);
        var directory = new List<ChunkEntry>(count);

        for (int index = 0; index < count; index++)
        {
            string key = ReadRequiredString(span, ref position, "module id");
            int schemaVersion = ReadInt32(span, ref position);
            ModuleCriticality criticality = (ModuleCriticality)ReadByte(span, ref position);
            if (!Enum.IsDefined(criticality))
            {
                throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, $"Module '{key}' has invalid criticality '{(byte)criticality}'.");
            }

            byte serializerId = ReadByte(span, ref position);
            byte compressionId = ReadByte(span, ref position);
            byte encryptionId = ReadByte(span, ref position);
            byte hashId = ReadByte(span, ref position);
            long offset = ReadInt64(span, ref position);
            int length = ReadInt32(span, ref position);
            byte[]? encryptionMetadata = ReadBlob(span, ref position);
            byte[]? hashBytes = ReadBlob(span, ref position);
            directory.Add(new ChunkEntry(
                key,
                schemaVersion,
                criticality,
                serializerId,
                compressionId,
                encryptionId,
                hashId,
                offset,
                length,
                encryptionMetadata,
                hashBytes));
        }

        ValidateDirectory(directory, bytes.Length, position);
        var header = new SaveHeader(
            DeliveranceOptions.CurrentContainerFormatVersion,
            created,
            buildId,
            applicationId,
            applicationVersion,
            applicationSaveVersion,
            definitionHash,
            cadenceHash,
            flags);
        return new SaveContainerReader(header, directory, bytes);
    }

    private static void ValidateDirectory(
        IReadOnlyList<ChunkEntry> directory,
        int fileLength,
        int minimumPayloadOffset)
    {
        if (directory.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != directory.Count)
        {
            throw new DeliveranceException(SaveDiagnosticCode.DuplicateModule, "The Deliverance directory contains duplicate module ids.");
        }

        long previousEnd = minimumPayloadOffset;
        foreach (ChunkEntry entry in directory.OrderBy(item => item.Offset))
        {
            if (entry.ModuleVersion < 1
                || entry.Offset < previousEnd
                || entry.Length < 0
                || entry.Offset > fileLength - entry.Length)
            {
                throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, $"Module '{entry.Key}' has invalid or overlapping bounds.");
            }
            previousEnd = entry.Offset + entry.Length;
        }
    }

    private static int ReadChunkCount(ReadOnlySpan<byte> span, ref int position)
    {
        int count = ReadInt32(span, ref position);
        if (count < 0 || count > MaximumChunkCount)
        {
            throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, "Unreasonable module count.");
        }
        return count;
    }

    private static byte ReadByte(ReadOnlySpan<byte> span, ref int position)
    {
        return ReadBytes(span, ref position, 1)[0];
    }

    private static int ReadInt32(ReadOnlySpan<byte> span, ref int position)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(span, ref position, 4));
    }

    private static long ReadInt64(ReadOnlySpan<byte> span, ref int position)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(span, ref position, 8));
    }

    private static string? ReadString(ReadOnlySpan<byte> span, ref int position)
    {
        int length = ReadInt32(span, ref position);
        if (length == -1)
        {
            return null;
        }
        if (length < 0)
        {
            throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, "Negative string length.");
        }
        return Encoding.UTF8.GetString(ReadBytes(span, ref position, length));
    }

    private static string ReadRequiredString(ReadOnlySpan<byte> span, ref int position, string description)
    {
        return ReadString(span, ref position)
            ?? throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, $"Container {description} cannot be null.");
    }

    private static byte[]? ReadBlob(ReadOnlySpan<byte> span, ref int position)
    {
        int length = ReadInt32(span, ref position);
        if (length < 0)
        {
            throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, "Negative blob length.");
        }
        return length == 0 ? null : ReadBytes(span, ref position, length).ToArray();
    }

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> span, ref int position, int length)
    {
        if (length < 0 || position < 0 || position > span.Length - length)
        {
            throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, "Unexpected end of Deliverance container.");
        }
        ReadOnlySpan<byte> result = span.Slice(position, length);
        position += length;
        return result;
    }
}
