namespace Deliverance.Core.BuiltIns;

public sealed class KeyValueChunkV1
{
    public Dictionary<string, byte[]> Data { get; set; } = new(StringComparer.Ordinal);
}

