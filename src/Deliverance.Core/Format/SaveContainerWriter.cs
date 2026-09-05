using System.Buffers.Binary;
using System.Text;

namespace Deliverance.Core.Format;

internal static class SaveContainerWriter
{
    public static byte[] Write(SaveHeader header, IReadOnlyList<ChunkEntry> directory, IReadOnlyList<ReadOnlyMemory<byte>> payloads)
    {
        if (directory.Count != payloads.Count)
        {
            throw new InvalidOperationException("Directory and payload counts differ.");
        }

        using var stream = new MemoryStream();
        stream.Write(WritePrefixOnly(header, directory));
        foreach (ReadOnlyMemory<byte> payload in payloads)
        {
            stream.Write(payload.Span);
        }
        return stream.ToArray();
    }

    public static byte[] WritePrefixOnly(SaveHeader header, IReadOnlyList<ChunkEntry> directory)
    {
        if (header.ContainerVersion != DeliveranceOptions.CurrentContainerFormatVersion)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.UnsupportedContainerVersion,
                $"The vNext writer only emits container format {DeliveranceOptions.CurrentContainerFormatVersion}.");
        }

        using var stream = new MemoryStream();
        stream.Write("DLVR"u8);
        WriteInt32(stream, header.ContainerVersion);
        WriteInt64(stream, header.UtcUnixSeconds);
        WriteString(stream, header.ApplicationId);
        WriteString(stream, header.ApplicationVersion);
        WriteInt32(stream, header.ApplicationSaveVersion);
        WriteString(stream, header.BuildId);
        WriteString(stream, header.DefinitionHash);
        WriteString(stream, header.CadenceConfigHash);
        stream.WriteByte(header.Flags);
        WriteInt32(stream, directory.Count);

        foreach (ChunkEntry entry in directory)
        {
            WriteString(stream, entry.Key);
            WriteInt32(stream, entry.ModuleVersion);
            stream.WriteByte((byte)entry.Criticality);
            stream.WriteByte(entry.SerializerId);
            stream.WriteByte(entry.CompressionId);
            stream.WriteByte(entry.EncryptionId);
            stream.WriteByte(entry.HashId);
            WriteInt64(stream, entry.Offset);
            WriteInt32(stream, entry.Length);
            WriteBlob(stream, entry.EncryptionMetadata);
            WriteBlob(stream, entry.HashBytes);
        }
        return stream.ToArray();
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteString(Stream stream, string? value)
    {
        if (value is null)
        {
            WriteInt32(stream, -1);
            return;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteBlob(Stream stream, byte[]? value)
    {
        if (value is null || value.Length == 0)
        {
            WriteInt32(stream, 0);
            return;
        }
        WriteInt32(stream, value.Length);
        stream.Write(value);
    }
}
