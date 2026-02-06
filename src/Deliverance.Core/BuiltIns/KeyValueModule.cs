using Deliverance.Core.IO;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using MessagePack;

namespace Deliverance.Core.BuiltIns;

/// <summary>
/// MVP key/value chunk. Values are stored as raw serialized bytes.
/// Caller controls type at read time (explicit, no typeless magic).
/// </summary>
public sealed class KeyValueModule : ISaveModule
{
    public string Key => "kv";
    public int Version => 1;

    private readonly ISaveSerializer _serializer;

    // Store serialized payloads; per-key value schema belongs to the caller.
    private readonly Dictionary<string, byte[]> _data = new(StringComparer.Ordinal);

    public KeyValueModule(ISaveSerializer serializer)
    {
        _serializer = serializer;
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
        => w.WriteBytes(MessagePackSerializer.Serialize(_data));

    public void Restore(ISaveReader r)
    {
        _data.Clear();
        var dict = MessagePackSerializer.Deserialize<Dictionary<string, byte[]>>(r.ReadBytes());
        foreach (var (k, v) in dict) _data[k] = v;
    }
}
