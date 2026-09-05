using Deliverance.Core;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class GoldenFile_CompatibilityTests
{
    [Fact]
    public async Task Golden_V1_Loads_And_Restores_State()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData");
        var goldenPath = Path.Combine(root, "golden_v1.dlv_b");
        Assert.True(File.Exists(goldenPath), $"Missing golden file: {goldenPath}");

        // Use FileSaveStore pointing at TestData folder.
        var store = new FileSaveStore(root, ".dlv_b");

        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            MissingChunkPolicy = MissingChunkPolicy.Error, // strict: format regressions should fail loudly
        };

        var save = new DeliveranceService(options);

        var counter = new CounterModule("counter");
        var text = new StringModule("text");
        save.Register(counter);
        save.Register(text);

        // NOTE: FileSaveStore expects slotId without extension
        await save.LoadSlotAsync("golden_v1");

        Assert.Equal(12345, counter.Value);
        Assert.Equal("hello-golden", text.Value);

        Assert.Equal(0.25f, save.KV.Get("settings.volume", -1f));
        Assert.Equal("GoldenUser", save.KV.Get("profile.name", "???"));
    }
}
