using Deliverance.Core.IO;

namespace Deliverance.Core.Modules;

/// <summary>
/// Code-first save participation. A module owns a chunk in the save container.
/// </summary>
public interface ISaveModule
{
    /// <summary>Stable chunk key, e.g. "kv", "meta", "quests", "party".</summary>
    string Key { get; }

    /// <summary>Schema version for THIS module's payload (not the container).</summary>
    int Version { get; }

    void Capture(ISaveWriter w);
    void Restore(ISaveReader r);
}
