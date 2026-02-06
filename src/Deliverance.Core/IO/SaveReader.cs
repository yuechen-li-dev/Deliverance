using Deliverance.Core.Serialization;

namespace Deliverance.Core.IO;

internal sealed class SaveReader : ISaveReader
{
    private readonly ISaveSerializer _serializer;
    private readonly ReadOnlyMemory<byte> _payload;

    public SaveReader(ISaveSerializer serializer, ReadOnlyMemory<byte> payload)
    {
        _serializer = serializer;
        _payload = payload;
    }

    public T Read<T>() => _serializer.Deserialize<T>(_payload);

    public ReadOnlyMemory<byte> ReadBytes() => _payload;
}
