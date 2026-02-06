using Deliverance.Core.IO;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;

namespace Deliverance.Core.BuiltIns;

/// <summary>
/// Key/value chunk. Values are stored as raw serialized bytes using the configured ISaveSerializer.
/// Caller controls type at read time (explicit, no typeless magic).
/// </summary>
public sealed class KeyValueModule : ISaveModule
{
    public string Key => "kv";
    public int Version => 1;

    private readonly ISaveSerializer _serializer;

    // Store per-key serialized payloads; per-key value schema belongs to the caller.
    private readonly Dictionary<string, byte[]> _data = new(StringComparer.Ordinal);

    public KeyValueModule(ISaveSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public IReadOnlyCollection<string> Keys => _data.Keys;

    public void Set<T>(string key, T value)
        => _data[key] = _serializer.Serialize(value);

    public bool TryGet<T>(string key, out T value)
    {
        if (_data.TryGetValue(key, out var bytes))
        {
            value = _serializer.Deserialize<T>(bytes);
            return true;
        }

        value = default!;
        return false;
    }

    public bool Remove(string key) => _data.Remove(key);

    public void Capture(ISaveWriter w)
    {
        // Serialize the dictionary via the configured serializer, not MessagePack directly.
        var chunk = new KeyValueChunkV1 { Data = new Dictionary<string, byte[]>(_data, StringComparer.Ordinal) };
        w.Write(chunk);
    }

    public void Restore(ISaveReader r)
    {
        _data.Clear();
        var chunk = r.Read<KeyValueChunkV1>();

        // Be defensive: null-safe and force ordinal comparer.
        if (chunk?.Data is null) return;

        foreach (var (k, v) in chunk.Data)
            _data[k] = v;
    }
}
