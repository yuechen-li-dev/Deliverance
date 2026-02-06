namespace Deliverance.Core.IO;

public interface ISaveReader
{
    /// <summary>Read the module's root object.</summary>
    T Read<T>();

    /// <summary>Read raw bytes (advanced / escape hatch).</summary>
    ReadOnlyMemory<byte> ReadBytes();
}
