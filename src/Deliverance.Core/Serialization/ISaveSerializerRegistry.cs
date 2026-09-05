namespace Deliverance.Core.Serialization;

public interface ISaveSerializerRegistry
{
    bool TryGet(byte id, out ISaveSerializer serializer);
    void Register(ISaveSerializer serializer);
}

public sealed class DefaultSaveSerializerRegistry : ISaveSerializerRegistry
{
    private readonly Dictionary<byte, ISaveSerializer> serializers = new();

    public bool TryGet(byte id, out ISaveSerializer serializer)
    {
        return serializers.TryGetValue(id, out serializer!);
    }

    public void Register(ISaveSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        if (serializer.Id == 0)
        {
            throw new ArgumentException("Serializer id 0 is reserved for raw bytes.", nameof(serializer));
        }
        serializers[serializer.Id] = serializer;
    }
}
