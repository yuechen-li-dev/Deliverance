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

                // MVP: apply default compression uniformly
                ICompressionCodec codec = Options.DefaultCompression;

                // Ensure default codec is in registry (helps if user swapped DefaultCompression but forgot to register)
                Options.Codecs.Register(codec);

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

        // Build an initial directory with offsets = 0 just to measure prefix length.
        // (Lengths must be correct; offsets are fixed-size fields so they don't affect prefix size.)
        directory.Clear();
        for (int i = 0; i < keys.Length; i++)
        {
            directory.Add(new ChunkEntry(
                Key: keys[i],
                ModuleVersion: moduleVersions[i],
                CodecId: codecIds[i],
                Offset: 0,
                Length: payloads[i].Length
            ));
        }

        // Measure prefix length
        var measurePrefix = SaveContainerWriter.WritePrefixOnly(header, directory);
        long prefixLen = measurePrefix.LongLength;

        // Now patch offsets in-place (or rebuild entries) using the measured prefix length.
        long offset = prefixLen;
        for (int i = 0; i < directory.Count; i++)
        {
            var e = directory[i];
            directory[i] = e with { Offset = offset };
            offset += e.Length;
        }

        // IMPORTANT: write the real prefix with correct offsets
        var writePrefix = SaveContainerWriter.WritePrefixOnly(header, directory);

        if (Options.Store is IStreamingSaveStore streaming)
        {
            var segments = new ReadOnlyMemory<byte>[1 + payloads.Count];
            segments[0] = writePrefix;

            for (int i = 0; i < payloads.Count; i++)
                segments[i + 1] = payloads[i];

            await using var segmented = new Deliverance.Core.IO.SegmentedReadStream(segments);
            await streaming.WriteAsync(slotId, segmented, Options.BackupCopiesToKeep, ct).ConfigureAwait(false);
        }
        else
        {
            // Non-streaming fallback: still allocates a single array
            var finalBytes = SaveContainerWriter.Write(header, directory, payloads);
            await Options.Store.WriteSlotAsync(slotId, finalBytes, Options.BackupCopiesToKeep, ct).ConfigureAwait(false);
        }
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

            var payload = container.GetPayload(entry);

            var codec = ResolveCodec(entry.CodecId);
            var rawBytes = codec.Decompress(payload);
            ReadOnlyMemory<byte> raw = rawBytes;

            // Fast path: versions match
            if (entry.ModuleVersion == module.Version)
            {
                var r = new SaveReader(Options.Serializer, raw);
                module.Restore(r);
                continue;
            }

            // Version mismatch: attempt DTO migration if supported
            if (module is Deliverance.Core.Modules.IDtoMigratableSaveModule migratable)
            {
                var dtoType = migratable.GetDtoType(entry.ModuleVersion);
                object dto = Options.Serializer.Deserialize(dtoType, raw);

                int v = entry.ModuleVersion;
                while (v < module.Version)
                {
                    dto = migratable.UpgradeDto(dto, v);
                    v++;
                }

                if (module is Deliverance.Core.Modules.IDtoRestorableSaveModule restorable)
                {
                    restorable.RestoreFromDto(dto);
                }
                else
                {
                    var finalBytes = Options.Serializer.Serialize(dto, dto.GetType());
                    var r2 = new SaveReader(Options.Serializer, finalBytes);
                    module.Restore(r2);
                }

                continue;
            }

            HandleVersionMismatch(slotId, key, entry.ModuleVersion, module.Version);
            continue;
        }
        Diagnostics.EmitInfo($"Loaded slot '{slotId}' (container v{container.Header.ContainerVersion}).");
    }

    private ICompressionCodec ResolveCodec(byte codecId)
    {
        if (Options.Codecs.TryGet(codecId, out var codec))
            return codec;

        throw new NotSupportedException(
            $"Codec id '{codecId}' is not registered. " +
            $"Register the codec in DeliveranceOptions.Codecs to load this save.");
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

    private void HandleVersionMismatch(string slotId, string key, int fromVersion, int toVersion)
    {
        var msg = $"Slot '{slotId}' chunk '{key}' has version {fromVersion}, but module expects {toVersion}.";
        switch (Options.VersionMismatchPolicy)
        {
            case VersionMismatchPolicy.Ignore:
                return;
            case VersionMismatchPolicy.Warn:
                Diagnostics.EmitWarning(msg);
                return;
            case VersionMismatchPolicy.Error:
                throw new InvalidDataException(msg);
            default:
                Diagnostics.EmitWarning(msg);
                return;
        }
    }


    public async Task<SlotInspection> InspectSlotAsync(string slotId, CancellationToken ct = default)
    {
        var bytes = await Options.Store.ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        var container = SaveContainerReader.Read(bytes);

        var chunks = container.Directory
            .Select(e => new ChunkInfo(e.Key, e.ModuleVersion, e.CodecId, e.Offset, e.Length))
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToArray();

        return new SlotInspection(container.Header, chunks);
    }

    public async Task<ReadOnlyMemory<byte>> ExportChunkAsync(string slotId, string chunkKey, CancellationToken ct = default)
    {
        var bytes = await Options.Store.ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        var container = SaveContainerReader.Read(bytes);

        var entry = container.Directory.FirstOrDefault(e => string.Equals(e.Key, chunkKey, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(entry.Key))
            throw new KeyNotFoundException($"Chunk '{chunkKey}' not found in slot '{slotId}'.");

        var payload = container.GetPayload(entry);
        var codec = ResolveCodec(entry.CodecId);
        var raw = codec.Decompress(payload);
        return raw;
    }

    public async Task ImportChunkAsync(
    string slotId,
    string chunkKey,
    ReadOnlyMemory<byte> rawChunkPayload,
    int moduleVersion,
    byte codecId,
    CancellationToken ct = default)
    {
        // Load existing
        var bytes = await Options.Store.ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        var container = SaveContainerReader.Read(bytes);

        // Compress payload according to codecId
        var codec = ResolveCodec(codecId);
        var compressed = codec.Compress(rawChunkPayload);

        // Build a new directory + payload list
        var entries = container.Directory.ToList();
        var payloads = new List<ReadOnlyMemory<byte>>(entries.Count);

        // Replace or add entry
        var idx = entries.FindIndex(e => string.Equals(e.Key, chunkKey, StringComparison.Ordinal));
        if (idx >= 0)
            entries[idx] = entries[idx] with { ModuleVersion = moduleVersion, CodecId = codecId, Length = compressed.Length };
        else
            entries.Add(new ChunkEntry(chunkKey, moduleVersion, codecId, Offset: 0, Length: compressed.Length));

        // Sort by key for determinism
        entries = [.. entries.OrderBy(e => e.Key, StringComparer.Ordinal)];

        // Prepare payloads in same order as entries
        foreach (var e in entries)
        {
            if (string.Equals(e.Key, chunkKey, StringComparison.Ordinal))
            {
                payloads.Add(compressed);
            }
            else
            {
                var p = container.GetPayload(container.Directory.First(x => x.Key == e.Key));
                payloads.Add(p); // NOTE: this is still *compressed* bytes already stored
            }
        }

        // Measure prefix, compute offsets, write
        var header = container.Header;

        // offsets=0 for measure
        var dir = entries.Select(e => e with { Offset = 0 }).ToList();
        var measurePrefix = SaveContainerWriter.WritePrefixOnly(header, dir);
        long prefixLen = measurePrefix.LongLength;

        long off = prefixLen;
        for (int i = 0; i < dir.Count; i++)
        {
            var d = dir[i];
            dir[i] = d with { Offset = off };
            off += d.Length;
        }

        var writePrefix = SaveContainerWriter.WritePrefixOnly(header, dir);

        if (Options.Store is IStreamingSaveStore streaming)
        {
            var segments = new ReadOnlyMemory<byte>[1 + payloads.Count];
            segments[0] = writePrefix;
            for (int i = 0; i < payloads.Count; i++)
                segments[i + 1] = payloads[i];

            await using var segmented = new Deliverance.Core.IO.SegmentedReadStream(segments);
            await streaming.WriteAsync(slotId, segmented, Options.BackupCopiesToKeep, ct).ConfigureAwait(false);
        }
        else
        {
            var finalBytes = SaveContainerWriter.Write(header, dir, payloads);
            await Options.Store.WriteSlotAsync(slotId, finalBytes, Options.BackupCopiesToKeep, ct).ConfigureAwait(false);
        }
    }


}
