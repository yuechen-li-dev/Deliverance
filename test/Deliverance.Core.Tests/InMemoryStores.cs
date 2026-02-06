using Deliverance.Core.Storage;

namespace Deliverance.Core.Tests;

internal class InMemorySaveStore : ISaveStore
{
    private sealed class Entry
    {
        public byte[] Bytes = Array.Empty<byte>();
        public DateTimeOffset LastModifiedUtc;
    }

    private readonly Dictionary<string, Entry> _slots = new(StringComparer.Ordinal);

    public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default)
        => Task.FromResult(_slots.ContainsKey(slotId));

    public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_slots.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray());

    public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default)
    {
        if (!_slots.TryGetValue(slotId, out var e))
            return Task.FromResult<SlotInfo?>(null);

        return Task.FromResult<SlotInfo?>(new SlotInfo(slotId, e.LastModifiedUtc, e.Bytes.LongLength));
    }

    public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default)
    {
        var infos = _slots
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp =>
            {
                var e = kvp.Value;
                return new SlotInfo(kvp.Key, e.LastModifiedUtc, e.Bytes.LongLength);
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<SlotInfo>>(infos);
    }

    public Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default)
    {
        if (!_slots.TryGetValue(slotId, out var e))
            throw new FileNotFoundException("Slot not found.", slotId);

        return Task.FromResult<ReadOnlyMemory<byte>>(e.Bytes);
    }

    public Task WriteSlotAsync(string slotId, ReadOnlyMemory<byte> bytes, int keepBackups, CancellationToken ct = default)
    {
        _slots[slotId] = new Entry
        {
            Bytes = bytes.ToArray(),
            LastModifiedUtc = DateTimeOffset.UtcNow
        };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string slotId, CancellationToken ct = default)
    {
        _slots.Remove(slotId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryStreamingSaveStore : InMemorySaveStore, IStreamingSaveStore
{
    public int OpenReadCalls { get; private set; }
    public int WriteCalls { get; private set; }

    public async Task<Stream> OpenReadAsync(string slotId, CancellationToken ct = default)
    {
        OpenReadCalls++;
        var bytes = await ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        return new MemoryStream(bytes.ToArray(), writable: false);
    }

    public async Task WriteAsync(string slotId, Stream data, int keepBackups, CancellationToken ct = default)
    {
        WriteCalls++;
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, 128 * 1024, ct).ConfigureAwait(false);
        await WriteSlotAsync(slotId, ms.ToArray(), keepBackups, ct).ConfigureAwait(false);
    }
}
