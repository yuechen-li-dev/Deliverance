using Deliverance.Core.Codecs;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;
using Deliverance.Core.Encryption;

namespace Deliverance.Core;

public sealed class DeliveranceOptions
{
    /// <summary>Format version for Deliverance container. Bump only when container structure changes.</summary>
    public const int CurrentContainerFormatVersion = 2;

    /// <summary>Container layout identity. Applications must not use this as a domain schema version.</summary>
    public int ContainerVersion { get; set; } = CurrentContainerFormatVersion;

    public MissingChunkPolicy MissingChunkPolicy { get; set; } = MissingChunkPolicy.Warn;

    /// <summary>Optional build string you can stamp into saves (e.g. git sha / semantic version).</summary>
    public string? BuildId { get; set; }

    public required ISaveStore Store { get; init; }
    public required ISaveSerializer Serializer { get; init; }

    public ISaveSerializerRegistry Serializers { get; set; } = new DefaultSaveSerializerRegistry();

    /// <summary>
    /// Registry used to resolve CodecId → codec instance when reading saves.
    /// Must include codec 0 (none).
    /// </summary>
    public ICodecRegistry Codecs { get; set; } = new DefaultCodecRegistry();

    /// <summary>
    /// Default codec used when saving chunks (unless you later add per-module overrides).
    /// </summary>
    public ICompressionCodec DefaultCompression { get; set; } = new NoCompressionCodec();

    public IEncryptionCodecRegistry EncryptionCodecs { get; set; } = new DefaultEncryptionCodecRegistry();

    /// <summary>Optional authenticated encryption. Null keeps saves unencrypted.</summary>
    public IEncryptionCodec? DefaultEncryption { get; set; }

    /// <summary>Keys are application/profile/slot policy and are never invented or persisted by Deliverance.</summary>
    public IEncryptionKeyProvider? EncryptionKeyProvider { get; set; }

    /// <summary>Optional extra backup copies to keep when saving.</summary>
    public int BackupCopiesToKeep { get; set; } = 2;

    public VersionMismatchPolicy VersionMismatchPolicy { get; set; } = VersionMismatchPolicy.Error;

    public DeliveranceOptions RegisterDefaults()
    {
        Serializers.Register(Serializer);
        Codecs.Register(DefaultCompression);
        if (DefaultEncryption is not null)
        {
            EncryptionCodecs.Register(DefaultEncryption);
        }
        return this;
    }
}

public enum MissingChunkPolicy
{
    Ignore = 0,
    Warn = 1,
    Error = 2,
}

public enum VersionMismatchPolicy
{
    Ignore = 0,   // Skip restoring that module
    Warn = 1,     // Warn and skip
    Error = 2,    // Throw
}
