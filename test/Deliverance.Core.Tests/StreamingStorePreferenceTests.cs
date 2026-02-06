using Deliverance.Core;
using Deliverance.Core.Serialization;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class StreamingStorePreferenceTests
{
    [Fact]
    public async Task WhenStoreImplementsStreaming_CoreUsesIt()
    {
        var store = new InMemoryStreamingSaveStore();

        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
        };

        var save = new DeliveranceService(options);
        save.Register(new CounterModule("counter") { Value = 42 });

        await save.SaveSlotAsync("slot1");
        Assert.True(store.WriteCalls > 0);

        await save.LoadSlotAsync("slot1");
        Assert.True(store.OpenReadCalls > 0);
    }
}
