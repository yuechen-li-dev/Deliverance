namespace Deliverance.Core.Storage;

/// <summary>
/// Optional capability interface for stores which can stream reads/writes (cloud-friendly).
/// Core can prefer this when present, while keeping byte[] APIs for MVP simplicity.
/// </summary>
public interface IStreamingSaveStore
{
    Task<Stream> OpenReadAsync(string slotId, CancellationToken ct = default);

    /// <summary>
    /// Store implementation is responsible for durability/atomicity/versioning semantics appropriate for the backend.
    /// </summary>
    Task WriteAsync(string slotId, Stream data, int keepBackups, CancellationToken ct = default);
}
