namespace Deliverance.Core.Format;

public sealed record ChunkInfo(
    string Key,
    int ModuleVersion,
    byte CompressionId,
    long Offset,
    int Length,
    byte SerializerId = 0,
    byte EncryptionId = 0,
    byte HashId = 0,
    Modules.ModuleCriticality Criticality = Modules.ModuleCriticality.Required
);
