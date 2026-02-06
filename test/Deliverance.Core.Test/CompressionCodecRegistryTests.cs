using Deliverance.Core;
using Deliverance.Core.Codecs;
using Deliverance.Core.Serialization;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class CompressionCodecRegistryTests
{
    [Fact]
    public async Task SaveLoad_WithGzipCodec_RoundTrips()
    {
        var store = new InMemorySaveStore();

        var gzip = new GzipCodec();

        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            Codecs = new DefaultCodecRegistry(),
            DefaultCompression = gzip,
        };

        // Explicitly register codec id=1 (even though DeliveranceService also registers DefaultCompression defensively)
        options.Codecs.Register(gzip);

        var save = new DeliveranceService(options);

        var counter = new CounterModule("counter") { Value = 777 };
        save.Register(counter);

        save.KV.Set("hello", "world");

        await save.SaveSlotAsync("slot1");

        // Mutate
        counter.Value = 0;
        save.KV.Set("hello", "nope");

        await save.LoadSlotAsync("slot1");

        Assert.Equal(777, counter.Value);
        Assert.Equal("world", save.KV.Get("hello", "???"));
    }
}
