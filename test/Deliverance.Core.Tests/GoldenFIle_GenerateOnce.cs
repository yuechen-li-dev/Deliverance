using Deliverance.Core;
using Deliverance.Core.Serialization;
using Deliverance.Core.Storage;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class GoldenFile_GenerateOnce
{
    [Fact(Skip = "Run locally once to (re)generate golden file, then commit TestData/golden_v1.dlv_b.")]
    public async Task Generate_Golden_V1()
    {
        var root = GetProjectTestDataDir();

        var slotId = "golden_v1";
        var store = new FileSaveStore(root); // default extension .dlv_b

        var options = new DeliveranceOptions
        {
            Store = store,
            Serializer = new MessagePackSaveSerializer(),
            BuildId = "golden-test-build",
            BackupCopiesToKeep = 0, // IMPORTANT: don't create .bak files for golden generation
        };

        var save = new DeliveranceService(options);

        var counter = new CounterModule("counter") { Value = 12345 };
        var text = new StringModule("text") { Value = "hello-golden" };
        save.Register(counter);
        save.Register(text);

        save.KV.Set("settings.volume", 0.25f);
        save.KV.Set("profile.name", "GoldenUser");

        await save.SaveSlotAsync(slotId);

        Assert.True(File.Exists(Path.Combine(root, slotId + ".dlv_b")));
    }

    private static string GetProjectTestDataDir()
    {
        // We start at bin/Debug/net8.0/ and walk upwards until we find the test csproj.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var csproj = Path.Combine(dir.FullName, "Deliverance.Core.Tests.csproj");
            if (File.Exists(csproj))
            {
                var testData = Path.Combine(dir.FullName, "TestData");
                Directory.CreateDirectory(testData);
                return testData;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Deliverance.Core.Tests.csproj by walking up from AppContext.BaseDirectory.");
    }
}
