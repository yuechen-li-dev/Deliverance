namespace Deliverance.Core.Storage;

public interface ISaveStore
{
    Task<bool> ExistsAsync(string slotId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default);

    Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default);

    /// <summary>Write the entire slot bytes atomically.</summary>
    Task WriteSlotAsync(string slotId, ReadOnlyMemory<byte> bytes, int keepBackups, CancellationToken ct = default);

    Task DeleteAsync(string slotId, CancellationToken ct = default);
}

public interface IStreamingSaveStore : ISaveStore
{
    Task<Stream> OpenReadAsync(string slotId, CancellationToken ct = default);
    Task WriteAsync(string slotId, Stream data, int keepBackups, CancellationToken ct = default);
}