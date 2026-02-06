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

    /// <summary>MVP default is none. You can swap later per-chunk if you want.</summary>
    public ICompressionCodec DefaultCompression { get; set; } = new NoCompressionCodec();

    /// <summary>Optional extra backup copies to keep when saving.</summary>
    public int BackupCopiesToKeep { get; set; } = 2;
}

public enum MissingChunkPolicy
{
    Ignore = 0,
    Warn = 1,
    Error = 2,
}
