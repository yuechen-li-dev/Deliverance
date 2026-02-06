using MessagePack;

namespace Deliverance.Core.BuiltIns;

[MessagePackObject]
public sealed class SaveMeta
{
    [Key(0)] public long UtcUnixSeconds { get; set; }
    [Key(1)] public string? BuildId { get; set; }
}
