using Deliverance.Core.Storage;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class FileSaveStoreMetadataTests
{
    [Fact]
    public async Task FileSaveStore_ListSlotInfos_ReturnsSizeAndModified()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeliveranceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new FileSaveStore(root);
            await store.WriteSlotAsync("slotA", new byte[] { 1, 2, 3 }, keepBackups: 0);
            await store.WriteSlotAsync("slotB", new byte[] { 9, 9 }, keepBackups: 0);

            var infos = await store.ListSlotInfosAsync();
            Assert.Contains(infos, i => i.SlotId == "slotA" && i.SizeBytes == 3 && i.LastModifiedUtc.HasValue);
            Assert.Contains(infos, i => i.SlotId == "slotB" && i.SizeBytes == 2 && i.LastModifiedUtc.HasValue);

            var one = await store.GetSlotInfoAsync("slotA");
            Assert.NotNull(one);
            Assert.Equal("slotA", one!.SlotId);
            Assert.Equal(3, one.SizeBytes);
            Assert.True(one.LastModifiedUtc.HasValue);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
