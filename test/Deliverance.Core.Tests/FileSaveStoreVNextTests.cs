using System.Text;
using Deliverance.Core.Storage;

namespace Deliverance.Core.Tests;

public sealed class FileSaveStoreVNextTests
{
    [Fact]
    public async Task DefaultExtension_SafeSlotEnumerationDeleteAndBackupRotationWork()
    {
        string root = Path.Combine(Path.GetTempPath(), "deliverance-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSaveStore(root);
            await store.WriteSlotAsync("autosave/../1", "first"u8.ToArray(), 2);
            await store.WriteSlotAsync("autosave/../1", "second"u8.ToArray(), 2);
            await store.WriteSlotAsync("autosave/../1", "third"u8.ToArray(), 2);

            string path = store.GetPathForSlot("autosave/../1");
            Assert.EndsWith("autosave_.._1.dlv", path, StringComparison.Ordinal);
            Assert.Equal(["autosave_.._1"], await store.ListSlotsAsync());
            Assert.Equal("third", Encoding.UTF8.GetString((await store.ReadSlotAsync("autosave/../1")).Span));
            Assert.Equal("second", await File.ReadAllTextAsync(path + ".bak1"));
            Assert.Equal("first", await File.ReadAllTextAsync(path + ".bak2"));

            await store.DeleteAsync("autosave/../1");
            Assert.False(await store.ExistsAsync("autosave/../1"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentWritesRemainWhole_AndCancelledWritePreservesPrimary()
    {
        string root = Path.Combine(Path.GetTempPath(), "deliverance-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSaveStore(root);
            byte[] left = Enumerable.Repeat((byte)'L', 100_000).ToArray();
            byte[] right = Enumerable.Repeat((byte)'R', 100_000).ToArray();
            await Task.WhenAll(
                store.WriteSlotAsync("slot", left, 0),
                store.WriteSlotAsync("slot", right, 0));
            byte[] actual = (await store.ReadSlotAsync("slot")).ToArray();
            Assert.True(actual.SequenceEqual(left) || actual.SequenceEqual(right));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.WriteSlotAsync("slot", "broken"u8.ToArray(), 0, cancellation.Token));
            Assert.Equal(actual, (await store.ReadSlotAsync("slot")).ToArray());
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
