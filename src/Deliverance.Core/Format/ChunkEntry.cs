namespace Deliverance.Core.Format;

public readonly record struct ChunkEntry(
    string Key,
    int ModuleVersion,
    byte CodecId,
    long Offset,
    int Length
);
