namespace Deliverance.Core.Format;

public sealed record SlotInspection(
    SaveHeader Header,
    IReadOnlyList<ChunkInfo> Chunks
);
