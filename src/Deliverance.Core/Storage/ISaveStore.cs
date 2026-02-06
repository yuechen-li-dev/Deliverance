namespace Deliverance.Core.Storage;

public interface ISaveStore
{
    Task<bool> ExistsAsync(string slotId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default);

        /// <summary>Returns metadata without downloading the whole save payload (when possible).</summary>
    Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default);

    /// <summary>Returns metadata for all slots (when possible).</summary>
    Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default);

    Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default);

    /// <summary>Write the entire slot bytes atomically.</summary>
    Task WriteSlotAsync(string slotId, ReadOnlyMemory<byte> bytes, int keepBackups, CancellationToken ct = default);

    Task DeleteAsync(string slotId, CancellationToken ct = default);
}

