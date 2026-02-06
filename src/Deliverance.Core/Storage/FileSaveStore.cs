using Deliverance.Core.Storage;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Deliverance.Core.Storage;

public sealed class FileSaveStore : ISaveStore, IStreamingSaveStore
{
    private readonly string _rootDir;
    private readonly string _extension;

    public FileSaveStore(string rootDir, string extension = ".dlv")
    {
        _rootDir = rootDir;
        _extension = extension.StartsWith('.') ? extension : "." + extension;
        Directory.CreateDirectory(_rootDir);
    }

    public Task<bool> ExistsAsync(string slotId, CancellationToken ct = default)
        => Task.FromResult(File.Exists(PathFor(slotId)));

    public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default)
    {
        var path = PathFor(slotId);
        if (!File.Exists(path)) return Task.FromResult<SlotInfo?>(null);
        var fi = new FileInfo(path);
        var info = new SlotInfo(
            SlotId: slotId,
            LastModifiedUtc: fi.LastWriteTimeUtc == default ? null : new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
            SizeBytes: fi.Length
        );
        return Task.FromResult<SlotInfo?>(info);
    }

    public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_rootDir);
        var files = Directory.GetFiles(_rootDir, "*" + _extension, SearchOption.TopDirectoryOnly);
        var infos = files.Select(f =>
        {
            var fi = new FileInfo(f);
            var slot = Path.GetFileNameWithoutExtension(f);
            return new SlotInfo(
                SlotId: slot,
                LastModifiedUtc: fi.LastWriteTimeUtc == default ? null : new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
                SizeBytes: fi.Length
            );
        }).ToArray();

        return Task.FromResult<IReadOnlyList<SlotInfo>>(infos);
    }


    public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_rootDir);
        var files = Directory.GetFiles(_rootDir, "*" + _extension, SearchOption.TopDirectoryOnly);
        var slots = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
        return Task.FromResult<IReadOnlyList<string>>(slots);
    }

    public async Task<ReadOnlyMemory<byte>> ReadSlotAsync(string slotId, CancellationToken ct = default)
    {
        var path = PathFor(slotId);
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        return bytes;
    }

    public async Task WriteSlotAsync(string slotId, ReadOnlyMemory<byte> bytes, int keepBackups, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_rootDir);

        var path = PathFor(slotId);
        var tmp = path + ".tmp";

        // Rotate backups: slot.dlv.bak1, bak2, ...
        if (File.Exists(path) && keepBackups > 0)
        {
            for (int i = keepBackups; i >= 1; i--)
            {
                var from = BackupPath(path, i);
                var to = BackupPath(path, i + 1);
                if (File.Exists(from))
                {
                    if (i == keepBackups) File.Delete(from);
                    else File.Move(from, to, overwrite: true);
                }
            }
            File.Copy(path, BackupPath(path, 1), overwrite: true);
        }

        await File.WriteAllBytesAsync(tmp, bytes.ToArray(), ct).ConfigureAwait(false);

        // Atomic replace where possible
        if (OperatingSystem.IsWindows() && File.Exists(path))
        {
            File.Replace(tmp, path, null);
        }
        else
        {
            File.Move(tmp, path, overwrite: true);
        }
    }

        public Task<Stream> OpenReadAsync(string slotId, CancellationToken ct = default)
    {
        var path = PathFor(slotId);
        Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        return Task.FromResult(s);
    }

    public async Task WriteAsync(string slotId, Stream data, int keepBackups, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, 128 * 1024, ct).ConfigureAwait(false);
        await WriteSlotAsync(slotId, ms.ToArray(), keepBackups, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string slotId, CancellationToken ct = default)
    {
        var path = PathFor(slotId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string slotId)
    {
        var safe = MakeFileNameSafe(slotId);
        return Path.Combine(_rootDir, safe + _extension);
    }

    private static string BackupPath(string basePath, int index) => $"{basePath}.bak{index}";

    private static string MakeFileNameSafe(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString();
    }
}
