namespace Deliverance.Core.Storage;

public sealed record SlotInfo(
    string SlotId,
    DateTimeOffset? LastModifiedUtc,
    long? SizeBytes
);