using Deliverance.Core.IO;
using Deliverance.Core.Modules;

namespace Deliverance.Core.Tests;

internal sealed class CounterModule : ISaveModule
{
    public string Key { get; }
    public int Version => 1;

    public int Value { get; set; }

    public CounterModule(string key) => Key = key;

    public void Capture(ISaveWriter w) => w.Write(Value);

    public void Restore(ISaveReader r) => Value = r.Read<int>();
}

internal sealed class StringModule : ISaveModule
{
    public string Key { get; }
    public int Version => 1;

    public string? Value { get; set; }

    public StringModule(string key) => Key = key;

    public void Capture(ISaveWriter w) => w.Write(Value);

    public void Restore(ISaveReader r) => Value = r.Read<string?>();
}
