namespace Deliverance.Core.BuiltIns;

/// <summary>
/// User-facing convenience wrapper around the KV module.
/// </summary>
public sealed class KeyValueStore
{
    private readonly KeyValueModule _module;

    internal KeyValueStore(KeyValueModule module) => _module = module;

    public void Set<T>(string key, T value) => _module.Set(key, value);

    public T Get<T>(string key, T defaultValue = default!)
        => _module.TryGet<T>(key, out var v) ? v : defaultValue;

    public bool TryGet<T>(string key, out T value) => _module.TryGet(key, out value);

    public bool Remove(string key) => _module.Remove(key);

    public IReadOnlyCollection<string> Keys => _module.Keys;
}
