using Deliverance.Core.Codecs;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;

namespace Deliverance.Core;

public sealed class DeliveranceOptions
{
    /// <summary>Format version for Deliverance container. Bump only when container structure changes.</summary>
    public int ContainerVersion { get; set; } = 1;

    public MissingChunkPolicy MissingChunkPolicy { get; set; } = MissingChunkPolicy.Warn;

    /// <summary>Optional build string you can stamp into saves (e.g. git sha / semantic version).</summary>
    public string? BuildId { get; set; }

    public required ISaveStore Store { get; init; }
    public required ISaveSerializer Serializer { get; init; }

    /// <summary>
    /// Registry used to resolve CodecId → codec instance when reading saves.
    /// Must include codec 0 (none).
    /// </summary>
    public ICodecRegistry Codecs { get; set; } = new DefaultCodecRegistry();

    /// <summary>
    /// Default codec used when saving chunks (unless you later add per-module overrides).
    /// </summary>
    public ICompressionCodec DefaultCompression { get; set; } = new NoCompressionCodec();

    /// <summary>Optional extra backup copies to keep when saving.</summary>
    public int BackupCopiesToKeep { get; set; } = 2;

    public VersionMismatchPolicy VersionMismatchPolicy { get; set; } = VersionMismatchPolicy.Error;
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
