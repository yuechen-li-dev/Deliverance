using MessagePack;
using MessagePack.Resolvers;

namespace Deliverance.Core.Serialization;

public sealed class MessagePackSaveSerializer(MessagePackSerializerOptions? options = null) : ISaveSerializer
{
    // MVP default: contractless resolver for convenience.
    // You can tighten this later (explicit [MessagePackObject]/[Key] DTOs) without changing Deliverance architecture.
    public MessagePackSerializerOptions Options { get; } = options ?? MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
            // Put explicit resolvers first if you add them later.
            ContractlessStandardResolver.Instance
            ));

    public byte[] Serialize<T>(T value)
        => MessagePackSerializer.Serialize(value, Options);

    public T Deserialize<T>(ReadOnlyMemory<byte> bytes)
        => MessagePackSerializer.Deserialize<T>(bytes, Options);

    public byte[] Serialize(object value, Type type)
    {
        return type is null ? throw new ArgumentNullException(nameof(type)) : MessagePackSerializer.Serialize(type, value, Options);
    }

    public object Deserialize(Type type, ReadOnlyMemory<byte> bytes)
    {
        return type is null
            ? throw new ArgumentNullException(nameof(type))
            : MessagePackSerializer.Deserialize(type, bytes, Options)
            ?? throw new InvalidDataException($"Deserializer returned null for type '{type}'.");
    }

}
