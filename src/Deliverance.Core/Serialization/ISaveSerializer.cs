namespace Deliverance.Core.Serialization;

public interface ISaveSerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(ReadOnlyMemory<byte> bytes);


    // Type-based API (needed for DTO migrations)
    byte[] Serialize(object value, Type type);
    object Deserialize(Type type, ReadOnlyMemory<byte> bytes);
}
