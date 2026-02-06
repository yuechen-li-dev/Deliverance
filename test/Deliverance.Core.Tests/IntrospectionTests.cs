using Deliverance.Core.Serialization;

namespace Deliverance.Core.Tests;

public sealed class IntrospectionTests
{
    [Fact]
    public async Task InspectSlotAsync_ListsExpectedChunks()
    {
        var store = new InMemorySaveStore();
        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
        };

        var d = new DeliveranceService(options);

        var counter = new CounterModule("counter") { Value = 123 };
        d.Register(counter);

        d.KV.Set("k", "v");

        await d.SaveSlotAsync("slot1");

        var inspection = await d.InspectSlotAsync("slot1");

        // Basic sanity
        Assert.True(inspection.Header.ContainerVersion > 0);
        Assert.NotNull(inspection.Chunks);

        // Expect built-ins + our module
        Assert.Contains(inspection.Chunks, c => c.Key == "meta");
        Assert.Contains(inspection.Chunks, c => c.Key == "kv");
        Assert.Contains(inspection.Chunks, c => c.Key == "counter");

        // Deterministic: key order is nice (if you sorted)
        // If you didn't sort, remove this.
        var keys = inspection.Chunks.Select(c => c.Key).ToArray();
        var sorted = keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(sorted, keys);
    }

    [Fact]
    public async Task ExportChunkAsync_ReturnsDecompressedRawPayload()
    {
        var store = new InMemorySaveStore();
        var serializer = new MessagePackSaveSerializer();

        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
        };

        var d = new DeliveranceService(options);

        var counter = new CounterModule("counter") { Value = 42 };
        d.Register(counter);

        await d.SaveSlotAsync("slot1");

        var raw = await d.ExportChunkAsync("slot1", "counter");

        // CounterModule writes an int as the chunk root
        var value = serializer.Deserialize<int>(raw);
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task ImportChunkAsync_ReplacesChunk_AndLoadReflectsNewValue()
    {
        var store = new InMemorySaveStore();
        var serializer = new MessagePackSaveSerializer();

        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
        };

        var d = new DeliveranceService(options);

        var counter = new CounterModule("counter") { Value = 1 };
        d.Register(counter);

        await d.SaveSlotAsync("slot1");

        // Build a new raw payload for the counter chunk (uncompressed)
        var newRawPayload = serializer.Serialize(999);

        // Replace chunk in the existing slot
        await d.ImportChunkAsync(
            slotId: "slot1",
            chunkKey: "counter",
            rawChunkPayload: newRawPayload,
            moduleVersion: 1,   // CounterModule.Version is 1 in our tests
            codecId: 0          // none
        );

        // Mutate then reload to verify replacement stuck
        counter.Value = 0;
        await d.LoadSlotAsync("slot1");

        Assert.Equal(999, counter.Value);
    }
}

