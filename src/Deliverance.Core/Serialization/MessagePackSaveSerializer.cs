using MessagePack;
using MessagePack.Resolvers;

namespace Deliverance.Core.Serialization;

public sealed class MessagePackSaveSerializer : ISaveSerializer
{
    public MessagePackSerializerOptions Options { get; }

    public MessagePackSaveSerializer(MessagePackSerializerOptions? options = null)
    {
        // MVP default: contractless resolver for convenience.
        // You can tighten this later (explicit [MessagePackObject]/[Key] DTOs) without changing Deliverance architecture.
        Options = options ?? MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                // Put explicit resolvers first if you add them later.
                ContractlessStandardResolver.Instance
            ));
    }

    public byte[] Serialize<T>(T value)
        => MessagePackSerializer.Serialize(value, Options);

    public T Deserialize<T>(ReadOnlyMemory<byte> bytes)
        => MessagePackSerializer.Deserialize<T>(bytes, Options);
}
