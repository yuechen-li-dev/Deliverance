using System.Collections.Concurrent;
using System.Text;

namespace Deliverance.Core.Storage;

public sealed class FileSaveStore : ISaveStore, IStreamingSaveStore
{
    public const string DefaultExtension = ".dlv";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SlotLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string rootDirectory;
    private readonly string extension;

    public FileSaveStore(string rootDirectory, string extension = DefaultExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.extension = NormalizeExtension(extension);
        Directory.CreateDirectory(this.rootDirectory);
    }

    public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(PathFor(slotId)));
    }

    public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string path = PathFor(slotId);
        if (!File.Exists(path))
        {
            return Task.FromResult<SlotInfo?>(null);
        }
        return Task.FromResult<SlotInfo?>(ToInfo(path));
    }

    public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<SlotInfo> result = EnumeratePaths().Select(ToInfo).ToArray();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<string> result = EnumeratePaths()
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default)
    {
        return await File.ReadAllBytesAsync(PathFor(slotId), ct).ConfigureAwait(false);
    }

    public async Task<Stream> OpenReadAsync(string slotId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await Task.FromResult<Stream>(new FileStream(
            PathFor(slotId),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan)).ConfigureAwait(false);
    }

    public async Task WriteSlotAsync(
        string slotId,
        ReadOnlyMemory<byte> bytes,
        int keepBackups,
        CancellationToken ct = default)
    {
        await using var source = new MemoryStream(bytes.ToArray(), writable: false);
        await WriteAsync(slotId, source, keepBackups, ct).ConfigureAwait(false);
    }

    public async Task WriteAsync(
        string slotId,
        Stream data,
        int keepBackups,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (keepBackups < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepBackups));
        }

        string path = PathFor(slotId);
        SemaphoreSlim slotLock = SlotLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await slotLock.WaitAsync(ct).ConfigureAwait(false);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(rootDirectory);
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await data.CopyToAsync(output, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            Commit(path, temporaryPath, keepBackups);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.AtomicWriteFailed,
                $"Atomic write failed for slot '{slotId}'.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            slotLock.Release();
        }
    }

    public async Task DeleteAsync(string slotId, CancellationToken ct = default)
    {
        string path = PathFor(slotId);
        SemaphoreSlim slotLock = SlotLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await slotLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            File.Delete(path);
        }
        finally
        {
            slotLock.Release();
        }
    }

    public string GetPathForSlot(string slotId) => PathFor(slotId);

    private void Commit(string path, string temporaryPath, int keepBackups)
    {
        if (!File.Exists(path))
        {
            File.Move(temporaryPath, path);
            return;
        }

        RotateOlderBackups(path, keepBackups);
        if (OperatingSystem.IsWindows())
        {
            File.Replace(temporaryPath, path, keepBackups > 0 ? BackupPath(path, 1) : null);
            return;
        }

        if (keepBackups > 0)
        {
            File.Copy(path, BackupPath(path, 1), overwrite: true);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void RotateOlderBackups(string path, int keepBackups)
    {
        if (keepBackups == 0)
        {
            return;
        }
        File.Delete(BackupPath(path, keepBackups));
        for (int index = keepBackups - 1; index >= 1; index--)
        {
            string source = BackupPath(path, index);
            if (File.Exists(source))
            {
                File.Move(source, BackupPath(path, index + 1), overwrite: true);
            }
        }
    }

    private IEnumerable<string> EnumeratePaths()
    {
        Directory.CreateDirectory(rootDirectory);
        return Directory.EnumerateFiles(rootDirectory, "*" + extension, SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal);
    }

    private SlotInfo ToInfo(string path)
    {
        var file = new FileInfo(path);
        return new SlotInfo(
            Path.GetFileNameWithoutExtension(path),
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            file.Length);
    }

    private string PathFor(string slotId)
    {
        string safe = MakeFileNameSafe(slotId);
        string path = Path.GetFullPath(Path.Combine(rootDirectory, safe + extension));
        if (!string.Equals(Path.GetDirectoryName(path), rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Slot id escaped the configured save directory.", nameof(slotId));
        }
        return path;
    }

    private static string MakeFileNameSafe(string slotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        char[] invalid = [.. Path.GetInvalidFileNameChars(), Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        var builder = new StringBuilder(slotId.Length);
        foreach (char character in slotId.Trim())
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        }
        string result = builder.ToString().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(result) || result is "." or "..")
        {
            throw new ArgumentException("Slot id does not contain a usable filename.", nameof(slotId));
        }
        return result;
    }

    private static string NormalizeExtension(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string result = value.StartsWith('.') ? value : "." + value;
        if (result.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || result.Contains(Path.DirectorySeparatorChar) || result.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Save extension must be one filename extension.", nameof(value));
        }
        return result;
    }

    private static string BackupPath(string path, int index) => $"{path}.bak{index}";
}
