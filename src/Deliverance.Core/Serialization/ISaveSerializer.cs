namespace Deliverance.Core.Serialization;

public interface ISaveSerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(ReadOnlyMemory<byte> bytes);
}
