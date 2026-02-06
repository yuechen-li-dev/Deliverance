using Deliverance.Core;
using Deliverance.Core.Serialization;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class MissingChunkPolicyTests
{
    [Fact]
    public async Task MissingChunk_Ignore_DoesNotThrow()
    {
        var store = new InMemorySaveStore();

        // First save with only built-ins
        var saveA = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            MissingChunkPolicy = MissingChunkPolicy.Ignore,
        });

        await saveA.SaveSlotAsync("slot1");

        // Now load with a NEW module registered (chunk missing)
        var saveB = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            MissingChunkPolicy = MissingChunkPolicy.Ignore,
        });

        var counter = new CounterModule("counter") { Value = 999 };
        saveB.Register(counter);

        await saveB.LoadSlotAsync("slot1"); // should not throw
        Assert.Equal(999, counter.Value);   // unchanged (since chunk missing)
    }

    [Fact]
    public async Task MissingChunk_Warn_EmitsWarning()
    {
        var store = new InMemorySaveStore();

        var saveA = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            MissingChunkPolicy = MissingChunkPolicy.Warn,
        });

        await saveA.SaveSlotAsync("slot1");

        var saveB = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            MissingChunkPolicy = MissingChunkPolicy.Warn,
        });

        var warned = false;
        saveB.Diagnostics.Warning += _ => warned = true;

        saveB.Register(new CounterModule("counter"));

        await saveB.LoadSlotAsync("slot1");
        Assert.True(warned);
    }

    [Fact]
    public async Task MissingChunk_Error_Throws()
    {
        var store = new InMemorySaveStore();

        var saveA = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            MissingChunkPolicy = MissingChunkPolicy.Warn,
        });

        await saveA.SaveSlotAsync("slot1");

        var saveB = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            MissingChunkPolicy = MissingChunkPolicy.Error,
        });

        saveB.Register(new CounterModule("counter"));

        await Assert.ThrowsAsync<InvalidDataException>(() => saveB.LoadSlotAsync("slot1"));
    }
}
