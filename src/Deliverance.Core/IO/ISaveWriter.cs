namespace Deliverance.Core.IO;

public interface ISaveWriter
{
    /// <summary>
    /// Write a single root object for this module chunk.
    /// </summary>
    void Write<T>(T value);

    /// <summary>
    /// Write raw bytes as the payload (advanced / escape hatch).
    /// </summary>
    void WriteBytes(ReadOnlyMemory<byte> bytes);
}
