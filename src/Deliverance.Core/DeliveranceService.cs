using Deliverance.Core.BuiltIns;
using Deliverance.Core.Codecs;
using Deliverance.Core.Format;
using Deliverance.Core.IO;
using Deliverance.Core.Modules;
using Deliverance.Core.Storage;

namespace Deliverance.Core;

public sealed class DeliveranceService : IDeliverance
{
    private readonly Dictionary<string, ISaveModule> _modules = new(StringComparer.Ordinal);

    public DeliveranceOptions Options { get; }
    public SaveDiagnostics Diagnostics { get; } = new();

    private readonly KeyValueModule _kvModule;
    public KeyValueStore KV { get; }

    public DeliveranceService(DeliveranceOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));

        // Built-ins
        _kvModule = new KeyValueModule(Options.Serializer);
        KV = new KeyValueStore(_kvModule);

        Register(_kvModule);

        // Optional meta module: always present by default.
        Register(new MetaModule(
            capture: () => new SaveMeta
            {
                UtcUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                BuildId = Options.BuildId,
            },
            restore: _ => { /* MVP: no-op; you can consume meta externally if you want */ }
        ));
    }

    public void Register(ISaveModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Key)) throw new ArgumentException("Module key must be non-empty.", nameof(module));

        _modules[module.Key] = module;
    }

    public bool Unregister(string key) => _modules.Remove(key);

    public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default)
        => Options.Store.ListSlotsAsync(ct);

    public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default)
    => Options.Store.ListSlotInfosAsync(ct);

    public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default)
        => Options.Store.GetSlotInfoAsync(slotId, ct);

    public Task<bool> SlotExistsAsync(string slotId, CancellationToken ct = default)
        => Options.Store.ExistsAsync(slotId, ct);

    public Task DeleteSlotAsync(string slotId, CancellationToken ct = default)
        => Options.Store.DeleteAsync(slotId, ct);

    public async Task SaveSlotAsync(string slotId, CancellationToken ct = default)
    {
        // Capture payloads first
        var keys = _modules.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var payloads = new List<ReadOnlyMemory<byte>>(keys.Length);
        var directory = new List<ChunkEntry>(keys.Length);

        // We'll compute offsets after we know directory size; easiest is two-pass:
        // 1) capture/compress payloads
        // 2) compute header+dir byte length, then offsets
        var moduleVersions = new int[keys.Length];
        var codecIds = new byte[keys.Length];

        for (int i = 0; i < keys.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var key = keys[i];
            var module = _modules[key];

            try
            {
                var w = new SaveWriter(Options.Serializer);
                module.Capture(w);

                var raw = (ReadOnlyMemory<byte>)w.GetPayloadOrEmpty();

                // MVP: apply default compression uniformly (currently none)
                ICompressionCodec codec = Options.DefaultCompression;
                var compressed = codec.Compress(raw);

                payloads.Add(compressed);
                moduleVersions[i] = module.Version;
                codecIds[i] = codec.Id;
            }
            catch (Exception ex)
            {
                Diagnostics.EmitError($"Save failed in module '{key}'.", ex);
                throw;
            }
        }

        // Compute offsets:
        // We must know how large the header + directory will be, to set the first payload offset.
        // We'll do a "dry write" of just header+directory with zero offsets to get the prefix length.
        var header = new SaveHeader(
            ContainerVersion: Options.ContainerVersion,
            UtcUnixSeconds: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            BuildId: Options.BuildId
        );

        // Build placeholder directory to measure size
        var placeholderDir = new List<ChunkEntry>(keys.Length);
        for (int i = 0; i < keys.Length; i++)
            placeholderDir.Add(new ChunkEntry(keys[i], moduleVersions[i], codecIds[i], Offset: 0, Length: payloads[i].Length));

        // Measure prefix size by writing container with empty payloads (payloads not appended)
        // We'll approximate by writing a container and subtracting payloads; simple and safe for MVP.
        var prefixBytes = SaveContainerWriter.WritePrefixOnly(header, placeholderDir);
        long prefixLen = prefixBytes.LongLength;

        long offset = prefixLen;
        directory.Clear();
        for (int i = 0; i < keys.Length; i++)
        {
            directory.Add(new ChunkEntry(keys[i], moduleVersions[i], codecIds[i], offset, payloads[i].Length));
            offset += payloads[i].Length;
        }

        var finalBytes = SaveContainerWriter.Write(header, directory, payloads);

        if (Options.Store is IStreamingSaveStore streaming)
        {
            using var ms = new MemoryStream(finalBytes, writable: false);
            await streaming.WriteAsync(slotId, ms, Options.BackupCopiesToKeep, ct).ConfigureAwait(false);
        }
        else
        {
            await Options.Store.WriteSlotAsync(slotId, finalBytes, Options.BackupCopiesToKeep, ct).ConfigureAwait(false);
        }

        Diagnostics.EmitInfo($"Saved slot '{slotId}' with {directory.Count} chunks.");
    }

    public async Task LoadSlotAsync(string slotId, CancellationToken ct = default)
    {
        ReadOnlyMemory<byte> bytes;
        if (Options.Store is IStreamingSaveStore streaming)
        {
            await using var s = await streaming.OpenReadAsync(slotId, ct).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, 128 * 1024, ct).ConfigureAwait(false);
            bytes = ms.ToArray();
        }
        else
        {
            bytes = await Options.Store.ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        var container = SaveContainerReader.Read(bytes);

        // Map chunks
        var chunkMap = container.Directory.ToDictionary(e => e.Key, e => e, StringComparer.Ordinal);

        // Restore modules in key order for determinism (you can add dependency ordering later if you want)
        var keys = _modules.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        for (int i = 0; i < keys.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var key = keys[i];
            var module = _modules[key];

            if (!chunkMap.TryGetValue(key, out var entry))
            {
                HandleMissingChunk(slotId, key);
                continue;
            }

            try
            {
                var payload = container.GetPayload(entry);

                // MVP: use default codec to decompress by id 0 only.
                // Future: registry of codecs by Id.
                ReadOnlyMemory<byte> raw =
                    entry.CodecId == 0
                        ? payload
                        : throw new NotSupportedException($"Codec id '{entry.CodecId}' not supported in MVP (only 0=none).");

                var r = new SaveReader(Options.Serializer, raw);
                module.Restore(r);
            }
            catch (Exception ex)
            {
                Diagnostics.EmitError($"Load failed in module '{key}'.", ex);
                throw;
            }
        }

        Diagnostics.EmitInfo($"Loaded slot '{slotId}' (container v{container.Header.ContainerVersion}).");
    }

    private void HandleMissingChunk(string slotId, string key)
    {
        var msg = $"Slot '{slotId}' is missing chunk '{key}'.";
        switch (Options.MissingChunkPolicy)
        {
            case MissingChunkPolicy.Ignore:
                return;
            case MissingChunkPolicy.Warn:
                Diagnostics.EmitWarning(msg);
                return;
            case MissingChunkPolicy.Error:
                throw new InvalidDataException(msg);
            default:
                Diagnostics.EmitWarning(msg);
                return;
        }
    }
}
