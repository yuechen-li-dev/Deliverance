namespace Deliverance.Core.Format;

public sealed record ChunkInfo(
    string Key,
    int ModuleVersion,
    byte CodecId,
    long Offset,
    int Length
);
