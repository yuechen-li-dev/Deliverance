using Deliverance.Core.Serialization;

namespace Deliverance.Core.IO;

internal sealed class SaveWriter : ISaveWriter
{
    private readonly ISaveSerializer _serializer;
    private byte[]? _payload;

    public SaveWriter(ISaveSerializer serializer)
    {
        _serializer = serializer;
    }

    public void Write<T>(T value)
    {
        EnsureEmpty();
        _payload = _serializer.Serialize(value);
    }

    public void WriteBytes(ReadOnlyMemory<byte> bytes)
    {
        EnsureEmpty();
        _payload = bytes.ToArray();
    }

    public byte[] GetPayloadOrEmpty() => _payload ?? Array.Empty<byte>();

    private void EnsureEmpty()
    {
        if (_payload is not null)
            throw new InvalidOperationException("This module writer already contains a payload. Write exactly one root object per module.");
    }
}

