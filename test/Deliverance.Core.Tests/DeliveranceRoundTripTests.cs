using Deliverance.Core;
using Deliverance.Core.Serialization;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class DeliveranceRoundTripTests
{
    [Fact]
    public async Task SaveLoad_RoundTrips_Modules_And_KV()
    {
        var store = new InMemorySaveStore();
        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            BuildId = "test-build",
        };

        var save = new DeliveranceService(options);

        var counter = new CounterModule("counter") { Value = 123 };
        var text = new StringModule("text") { Value = "hello" };

        save.Register(counter);
        save.Register(text);

        save.KV.Set("settings.volume", 0.75f);
        save.KV.Set("profile.name", "Yuechen");

        await save.SaveSlotAsync("slot1");

        // Mutate state to ensure restore really happens
        counter.Value = 0;
        text.Value = null;

        // KV mutation too
        save.KV.Set("settings.volume", 0f);

        await save.LoadSlotAsync("slot1");

        Assert.Equal(123, counter.Value);
        Assert.Equal("hello", text.Value);

        Assert.Equal(0.75f, save.KV.Get("settings.volume", -1f));
        Assert.Equal("Yuechen", save.KV.Get("profile.name", "???"));
    }

    [Fact]
    public async Task ListSlots_And_SlotExists_Work()
    {
        var store = new InMemorySaveStore();
        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
        };

        var save = new DeliveranceService(options);

        Assert.False(await save.SlotExistsAsync("a"));
        Assert.Empty(await save.ListSlotsAsync());

        await save.SaveSlotAsync("a");
        await save.SaveSlotAsync("b");

        Assert.True(await save.SlotExistsAsync("a"));
        Assert.Equal(new[] { "a", "b" }, await save.ListSlotsAsync());

        await save.DeleteSlotAsync("a");
        Assert.False(await save.SlotExistsAsync("a"));
    }
}
