namespace Deliverance.Core.Format;

public readonly record struct ChunkEntry(
    string Key,
    int ModuleVersion,
    Modules.ModuleCriticality Criticality,

    byte SerializerId,
    byte CompressionId,
    byte EncryptionId,
    byte HashId,

    long Offset,
    int Length,

    byte[]? EncryptionMetadata,
    byte[]? HashBytes
);
