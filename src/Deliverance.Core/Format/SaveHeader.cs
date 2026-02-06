namespace Deliverance.Core.Format;

public readonly record struct SaveHeader(
    int ContainerVersion,
    long UtcUnixSeconds,
    string? BuildId
);
