namespace Deliverance.Core.Encryption;

public readonly record struct EncryptionKeyContext(
    string SlotId,
    string? ApplicationId,
    string ModuleId);

public interface IEncryptionKeyProvider
{
    ValueTask<ReadOnlyMemory<byte>> GetKeyAsync(
        EncryptionKeyContext context,
        CancellationToken cancellationToken = default);
}

public readonly record struct EncryptionResult(byte[] Ciphertext, byte[] Metadata);

public interface IEncryptionCodec
{
    byte Id { get; }
    string Name { get; }

    EncryptionResult Encrypt(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> key,
        ReadOnlySpan<byte> associatedData);

    byte[] Decrypt(
        ReadOnlyMemory<byte> ciphertext,
        ReadOnlyMemory<byte> metadata,
        ReadOnlyMemory<byte> key,
        ReadOnlySpan<byte> associatedData);
}

public interface IEncryptionCodecRegistry
{
    bool TryGet(byte id, out IEncryptionCodec codec);
    void Register(IEncryptionCodec codec);
}

public sealed class DefaultEncryptionCodecRegistry : IEncryptionCodecRegistry
{
    private readonly Dictionary<byte, IEncryptionCodec> codecs = new();

    public bool TryGet(byte id, out IEncryptionCodec codec)
    {
        return codecs.TryGetValue(id, out codec!);
    }

    public void Register(IEncryptionCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (codec.Id == 0)
        {
            throw new ArgumentException("Encryption id 0 is reserved for no encryption.", nameof(codec));
        }
        codecs[codec.Id] = codec;
    }
}
