using System.Security.Cryptography;

namespace Deliverance.Core.Encryption;

public sealed class AesGcmEncryptionCodec : IEncryptionCodec
{
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public byte Id => 1;
    public string Name => "aes-256-gcm";

    public EncryptionResult Encrypt(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> key,
        ReadOnlySpan<byte> associatedData)
    {
        ValidateKey(key);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key.Span, TagSize);
        aes.Encrypt(nonce, plaintext.Span, ciphertext, tag, associatedData);

        byte[] metadata = new byte[NonceSize + TagSize];
        nonce.CopyTo(metadata, 0);
        tag.CopyTo(metadata, NonceSize);
        return new EncryptionResult(ciphertext, metadata);
    }

    public byte[] Decrypt(
        ReadOnlyMemory<byte> ciphertext,
        ReadOnlyMemory<byte> metadata,
        ReadOnlyMemory<byte> key,
        ReadOnlySpan<byte> associatedData)
    {
        ValidateKey(key);
        if (metadata.Length != NonceSize + TagSize)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.InvalidContainer,
                "AES-GCM metadata must contain a 12-byte nonce and 16-byte authentication tag.");
        }

        byte[] plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key.Span, TagSize);
            aes.Decrypt(
                metadata.Span[..NonceSize],
                ciphertext.Span,
                metadata.Span[NonceSize..],
                plaintext,
                associatedData);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException exception)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.AuthenticationFailed,
                "AES-GCM authentication failed. The key is wrong or the save was tampered with.",
                exception);
        }
    }

    private static void ValidateKey(ReadOnlyMemory<byte> key)
    {
        if (key.Length != 32)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.EncryptionKeyUnavailable,
                "AES-256-GCM requires a caller-provided 32-byte key.");
        }
    }
}
