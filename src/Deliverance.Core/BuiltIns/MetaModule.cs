using Deliverance.Core.IO;
using Deliverance.Core.Modules;

namespace Deliverance.Core.BuiltIns;

public sealed class MetaModule(Func<SaveMeta> capture, Action<SaveMeta> restore) : ISaveModule
{
    public string Key => "meta";
    public int Version => 1;

    public void Capture(ISaveWriter w) => w.Write(capture());

    public void Restore(ISaveReader r) => restore(r.Read<SaveMeta>());
}
