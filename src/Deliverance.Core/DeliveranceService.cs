using System.Security.Cryptography;
using System.Text;
using Deliverance.Core.BuiltIns;
using Deliverance.Core.Codecs;
using Deliverance.Core.Encryption;
using Deliverance.Core.Format;
using Deliverance.Core.IO;
using Deliverance.Core.Modules;
using Deliverance.Core.Storage;

namespace Deliverance.Core;

public sealed class DeliveranceService : IDeliverance
{
    private const byte Sha256HashId = 1;
    private readonly Dictionary<string, ISaveModule> legacyModules = new(StringComparer.Ordinal);
    private readonly KeyValueModule keyValueModule;

    public DeliveranceOptions Options { get; }
    public SaveDiagnostics Diagnostics { get; } = new();
    public KeyValueStore KV { get; }

    public DeliveranceService(DeliveranceOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.RegisterDefaults();
        keyValueModule = new KeyValueModule(Options.Serializer);
        KV = new KeyValueStore(keyValueModule);
        legacyModules.Add(keyValueModule.Key, keyValueModule);
        var metaModule = new MetaModule(
            () => new SaveMeta { UtcUnixSeconds = 0, BuildId = Options.BuildId },
            _ => { });
        legacyModules.Add(metaModule.Key, metaModule);
    }

    [Obsolete("Use SaveAsync with explicit immutable SaveModulePayload values.")]
    public void Register(ISaveModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Key))
        {
            throw new ArgumentException("Module key must be non-empty.", nameof(module));
        }
        legacyModules[module.Key] = module;
    }

    [Obsolete("The explicit module API has no mutable registry.")]
    public bool Unregister(string key) => legacyModules.Remove(key);

    public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken ct = default) => Options.Store.ListSlotsAsync(ct);
    public Task<IReadOnlyList<SlotInfo>> ListSlotInfosAsync(CancellationToken ct = default) => Options.Store.ListSlotInfosAsync(ct);
    public Task<SlotInfo?> GetSlotInfoAsync(string slotId, CancellationToken ct = default) => Options.Store.GetSlotInfoAsync(slotId, ct);
    public Task<bool> SlotExistsAsync(string slotId, CancellationToken ct = default) => Options.Store.ExistsAsync(slotId, ct);
    public Task DeleteSlotAsync(string slotId, CancellationToken ct = default) => Options.Store.DeleteAsync(slotId, ct);

    public async Task SaveAsync(string slotId, SaveRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(request);
        if (Options.ContainerVersion != DeliveranceOptions.CurrentContainerFormatVersion)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.UnsupportedContainerVersion,
                $"New saves must use container format {DeliveranceOptions.CurrentContainerFormatVersion}.");
        }

        SaveModulePayload[] modules = request.Modules.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (modules.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != modules.Length)
        {
            throw new DeliveranceException(SaveDiagnosticCode.DuplicateModule, "A save request contains duplicate module ids.");
        }

        var storedPayloads = new List<ReadOnlyMemory<byte>>(modules.Length);
        var directory = new List<ChunkEntry>(modules.Length);
        bool anyEncrypted = false;
        foreach (SaveModulePayload module in modules)
        {
            ct.ThrowIfCancellationRequested();
            SaveModulePayload.ValidateIdentity(module.Id, module.SchemaVersion);
            if (!Options.Codecs.TryGet(module.CompressionId, out ICompressionCodec compression))
            {
                throw new DeliveranceException(
                    SaveDiagnosticCode.CompressionUnavailable,
                    $"Compression id '{module.CompressionId}' required by module '{module.Id}' is unavailable.");
            }

            byte[] semanticHash = SHA256.HashData(module.Bytes.Span);
            byte[] stored;
            try
            {
                stored = compression.Compress(module.Bytes);
            }
            catch (Exception exception) when (exception is not DeliveranceException)
            {
                throw new DeliveranceException(
                    SaveDiagnosticCode.CompressionFailed,
                    $"Compression failed for module '{module.Id}'.",
                    exception);
            }
            byte encryptionId = Options.DefaultEncryption?.Id ?? 0;
            byte[]? encryptionMetadata = null;
            if (Options.DefaultEncryption is IEncryptionCodec encryption)
            {
                anyEncrypted = true;
                if (Options.EncryptionKeyProvider is null)
                {
                    throw new DeliveranceException(
                        SaveDiagnosticCode.EncryptionKeyUnavailable,
                        "Encryption was requested but no key provider was configured.");
                }
                ReadOnlyMemory<byte> key = await GetKeyAsync(slotId, request.Metadata.ApplicationId, module.Id, ct).ConfigureAwait(false);
                EncryptionResult encrypted;
                try
                {
                    encrypted = encryption.Encrypt(stored, key, BuildAssociatedData(module, semanticHash));
                }
                catch (Exception exception) when (exception is not DeliveranceException)
                {
                    throw new DeliveranceException(
                        SaveDiagnosticCode.EncryptionFailed,
                        $"Encryption failed for module '{module.Id}'.",
                        exception);
                }
                stored = encrypted.Ciphertext;
                encryptionMetadata = encrypted.Metadata;
            }

            storedPayloads.Add(stored);
            directory.Add(new ChunkEntry(
                module.Id,
                module.SchemaVersion,
                module.Criticality,
                module.SerializerId,
                module.CompressionId,
                encryptionId,
                Sha256HashId,
                Offset: 0,
                Length: stored.Length,
                encryptionMetadata,
                semanticHash));
        }

        var header = new SaveHeader(
            DeliveranceOptions.CurrentContainerFormatVersion,
            request.CreatedUtcUnixSeconds,
            request.Metadata.BuildId,
            request.Metadata.ApplicationId,
            request.Metadata.ApplicationVersion,
            request.Metadata.ApplicationSaveVersion ?? 0,
            request.Metadata.DefinitionHash,
            request.Metadata.CadenceConfigHash,
            Flags: anyEncrypted ? (byte)1 : (byte)0);
        SetOffsets(header, directory);
        byte[] prefix = SaveContainerWriter.WritePrefixOnly(header, directory);
        await WriteContainerAsync(slotId, header, directory, storedPayloads, prefix, ct).ConfigureAwait(false);
        Diagnostics.EmitInfo($"Saved slot '{slotId}' with {modules.Length} explicit module(s).");
    }

    public async Task<LoadedSaveCandidate> LoadAsync(
        string slotId,
        IReadOnlyList<SaveModuleDefinition> definitions,
        LoadCompatibility? compatibility = null,
        CancellationToken ct = default)
    {
        SaveContainerReader container = await ReadContainerAsync(slotId, ct).ConfigureAwait(false);
        VerifyCompatibility(container.Header, compatibility);
        Dictionary<string, SaveModuleDefinition> expected = definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var candidates = new List<LoadedModuleCandidate>();

        foreach (ChunkEntry entry in container.Directory.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(entry.Key, out SaveModuleDefinition? definition))
            {
                if (entry.Criticality == ModuleCriticality.Required)
                {
                    throw new DeliveranceException(
                        SaveDiagnosticCode.UnknownRequiredModule,
                        $"Slot '{slotId}' contains unknown required module '{entry.Key}'.");
                }
                Diagnostics.EmitWarning($"Skipped unknown optional module '{entry.Key}'.");
                continue;
            }

            if (entry.ModuleVersion > definition.CurrentSchemaVersion)
            {
                if (definition.Criticality == ModuleCriticality.Required || entry.Criticality == ModuleCriticality.Required)
                {
                    throw new DeliveranceException(
                        SaveDiagnosticCode.NewerModuleSchema,
                        $"Module '{entry.Key}' schema {entry.ModuleVersion} is newer than runtime schema {definition.CurrentSchemaVersion}.");
                }
                Diagnostics.EmitWarning($"Skipped optional module '{entry.Key}' with newer schema {entry.ModuleVersion}.");
                continue;
            }

            ReadOnlyMemory<byte> raw = await DecodePayloadAsync(slotId, container.Header, container, entry, ct).ConfigureAwait(false);
            ReadOnlyMemory<byte> current = entry.ModuleVersion == definition.CurrentSchemaVersion
                ? raw
                : definition.Upgrade(entry.ModuleVersion, raw);
            definition.ValidateCurrentPayload?.Invoke(current);
            candidates.Add(new LoadedModuleCandidate(
                entry.Key,
                definition.CurrentSchemaVersion,
                definition.Criticality,
                entry.SerializerId,
                entry.CompressionId,
                current,
                Convert.ToHexString(SHA256.HashData(current.Span)).ToLowerInvariant()));
            expected.Remove(entry.Key);
        }

        SaveModuleDefinition? missing = expected.Values.FirstOrDefault(item => item.Criticality == ModuleCriticality.Required);
        if (missing is not null)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.MissingRequiredModule,
                $"Slot '{slotId}' is missing required module '{missing.Id}'.");
        }
        foreach (SaveModuleDefinition optional in expected.Values)
        {
            Diagnostics.EmitWarning($"Slot '{slotId}' does not contain optional module '{optional.Id}'.");
        }

        var metadata = new SaveApplicationMetadata(
            container.Header.ApplicationId,
            container.Header.ApplicationVersion,
            container.Header.BuildId,
            container.Header.DefinitionHash,
            container.Header.CadenceConfigHash,
            container.Header.ApplicationSaveVersion == 0 ? null : container.Header.ApplicationSaveVersion);
        Diagnostics.EmitInfo($"Loaded slot '{slotId}' as a candidate; the application still owns commit.");
        return new LoadedSaveCandidate(metadata, container.Header.UtcUnixSeconds, candidates);
    }

    [Obsolete("Use SaveAsync with explicit immutable SaveModulePayload values.")]
    public async Task SaveSlotAsync(string slotId, CancellationToken ct = default)
    {
        var payloads = new List<SaveModulePayload>();
        foreach (ISaveModule module in legacyModules.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var writer = new SaveWriter(Options.Serializer);
            module.Capture(writer);
            payloads.Add(new SaveModulePayload(
                module.Key,
                module.Version,
                ModuleCriticality.Required,
                Options.Serializer.Id,
                Options.DefaultCompression.Id,
                writer.GetPayloadOrEmpty()));
        }
        await SaveAsync(
            slotId,
            new SaveRequest(new SaveApplicationMetadata(BuildId: Options.BuildId), payloads),
            ct).ConfigureAwait(false);
    }

    [Obsolete("Use LoadAsync, validate the candidate, then explicitly commit it.")]
    public async Task LoadSlotAsync(string slotId, CancellationToken ct = default)
    {
        SaveContainerReader container = await ReadContainerAsync(slotId, ct).ConfigureAwait(false);
        Dictionary<string, ChunkEntry> entries = container.Directory.ToDictionary(item => item.Key, StringComparer.Ordinal);
        foreach (ISaveModule module in legacyModules.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!entries.TryGetValue(module.Key, out ChunkEntry entry))
            {
                HandleMissingChunk(slotId, module.Key);
                continue;
            }

            ReadOnlyMemory<byte> raw = await DecodePayloadAsync(slotId, container.Header, container, entry, ct).ConfigureAwait(false);
            if (entry.ModuleVersion == module.Version)
            {
                module.Restore(new SaveReader(Options.Serializer, raw));
                continue;
            }
            if (entry.ModuleVersion < module.Version && module is IDtoMigratableSaveModule migratable)
            {
                object dto = Options.Serializer.Deserialize(migratable.GetDtoType(entry.ModuleVersion), raw);
                int version = entry.ModuleVersion;
                while (version < module.Version)
                {
                    dto = migratable.UpgradeDto(dto, version);
                    version++;
                }
                if (module is IDtoRestorableSaveModule restorable)
                {
                    restorable.RestoreFromDto(dto);
                }
                else
                {
                    module.Restore(new SaveReader(Options.Serializer, Options.Serializer.Serialize(dto, dto.GetType())));
                }
                continue;
            }
            HandleVersionMismatch(slotId, module.Key, entry.ModuleVersion, module.Version);
        }
        Diagnostics.EmitInfo($"Loaded legacy mutable modules from slot '{slotId}'.");
    }

    public async Task<SlotInspection> InspectSlotAsync(string slotId, CancellationToken ct = default)
    {
        SaveContainerReader container = await ReadContainerAsync(slotId, ct).ConfigureAwait(false);
        ChunkInfo[] chunks = container.Directory
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new ChunkInfo(
                item.Key,
                item.ModuleVersion,
                item.CompressionId,
                item.Offset,
                item.Length,
                item.SerializerId,
                item.EncryptionId,
                item.HashId,
                item.Criticality))
            .ToArray();
        return new SlotInspection(container.Header, chunks);
    }

    public async Task<ReadOnlyMemory<byte>> ExportChunkAsync(string slotId, string chunkKey, CancellationToken ct = default)
    {
        SaveContainerReader container = await ReadContainerAsync(slotId, ct).ConfigureAwait(false);
        ChunkEntry entry = container.Directory.FirstOrDefault(item => string.Equals(item.Key, chunkKey, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(entry.Key))
        {
            throw new KeyNotFoundException($"Module '{chunkKey}' was not found in slot '{slotId}'.");
        }
        return await DecodePayloadAsync(slotId, container.Header, container, entry, ct).ConfigureAwait(false);
    }

    public async Task ImportChunkAsync(
        string slotId,
        string chunkKey,
        ReadOnlyMemory<byte> rawChunkPayload,
        int moduleVersion,
        byte codecId,
        CancellationToken ct = default)
    {
        SaveContainerReader container = await ReadContainerAsync(slotId, ct).ConfigureAwait(false);
        var modules = new List<SaveModulePayload>();
        foreach (ChunkEntry entry in container.Directory)
        {
            if (entry.EncryptionId != 0)
            {
                throw new NotSupportedException("Raw chunk import is intentionally unavailable for encrypted containers.");
            }
            ReadOnlyMemory<byte> raw = entry.Key == chunkKey
                ? rawChunkPayload
                : await DecodePayloadAsync(slotId, container.Header, container, entry, ct).ConfigureAwait(false);
            modules.Add(new SaveModulePayload(
                entry.Key,
                entry.Key == chunkKey ? moduleVersion : entry.ModuleVersion,
                entry.Criticality,
                entry.SerializerId,
                entry.Key == chunkKey ? codecId : entry.CompressionId,
                raw));
        }
        if (modules.All(item => item.Id != chunkKey))
        {
            modules.Add(new SaveModulePayload(chunkKey, moduleVersion, ModuleCriticality.Required, 0, codecId, rawChunkPayload));
        }
        var metadata = new SaveApplicationMetadata(
            container.Header.ApplicationId,
            container.Header.ApplicationVersion,
            container.Header.BuildId,
            container.Header.DefinitionHash,
            container.Header.CadenceConfigHash,
            container.Header.ApplicationSaveVersion == 0 ? null : container.Header.ApplicationSaveVersion);
        await SaveAsync(slotId, new SaveRequest(metadata, modules, container.Header.UtcUnixSeconds), ct).ConfigureAwait(false);
    }

    private async Task<ReadOnlyMemory<byte>> DecodePayloadAsync(
        string slotId,
        SaveHeader header,
        SaveContainerReader container,
        ChunkEntry entry,
        CancellationToken ct)
    {
        ReadOnlyMemory<byte> stored = container.GetPayload(entry);
        byte[] compressed;
        if (entry.EncryptionId == 0)
        {
            compressed = stored.ToArray();
        }
        else
        {
            if (!Options.EncryptionCodecs.TryGet(entry.EncryptionId, out IEncryptionCodec encryption))
            {
                throw new DeliveranceException(SaveDiagnosticCode.EncryptionUnavailable, $"Encryption id '{entry.EncryptionId}' is unavailable.");
            }
            if (Options.EncryptionKeyProvider is null)
            {
                throw new DeliveranceException(SaveDiagnosticCode.EncryptionKeyUnavailable, "The encrypted save requires a key provider.");
            }
            ReadOnlyMemory<byte> key = await GetKeyAsync(slotId, header.ApplicationId, entry.Key, ct).ConfigureAwait(false);
            try
            {
                compressed = encryption.Decrypt(
                    stored,
                    entry.EncryptionMetadata ?? ReadOnlyMemory<byte>.Empty,
                    key,
                    BuildAssociatedData(entry));
            }
            catch (Exception exception) when (exception is not DeliveranceException)
            {
                throw new DeliveranceException(
                    SaveDiagnosticCode.DecryptionFailed,
                    $"Decryption failed for module '{entry.Key}'.",
                    exception);
            }
        }

        if (!Options.Codecs.TryGet(entry.CompressionId, out ICompressionCodec compression))
        {
            throw new DeliveranceException(SaveDiagnosticCode.CompressionUnavailable, $"Compression id '{entry.CompressionId}' is unavailable.");
        }
        byte[] raw;
        try
        {
            raw = compression.Decompress(compressed);
        }
        catch (Exception exception) when (exception is not DeliveranceException)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.DecompressionFailed,
                $"Decompression failed for module '{entry.Key}'.",
                exception);
        }
        if (entry.HashId == Sha256HashId)
        {
            byte[] actual = SHA256.HashData(raw);
            if (entry.HashBytes is null || !CryptographicOperations.FixedTimeEquals(actual, entry.HashBytes))
            {
                throw new DeliveranceException(SaveDiagnosticCode.CorruptPayload, $"Module '{entry.Key}' failed its SHA-256 integrity check.");
            }
        }
        else if (entry.HashId != 0)
        {
            throw new DeliveranceException(SaveDiagnosticCode.InvalidContainer, $"Unknown hash id '{entry.HashId}'.");
        }
        return raw;
    }

    private async ValueTask<ReadOnlyMemory<byte>> GetKeyAsync(
        string slotId,
        string? applicationId,
        string moduleId,
        CancellationToken ct)
    {
        try
        {
            return await Options.EncryptionKeyProvider!
                .GetKeyAsync(new EncryptionKeyContext(slotId, applicationId, moduleId), ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not DeliveranceException)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.EncryptionKeyUnavailable,
                $"The key provider failed for module '{moduleId}'.",
                exception);
        }
    }

    private static byte[] BuildAssociatedData(SaveModulePayload module, byte[] hash)
    {
        return Encoding.UTF8.GetBytes($"{module.Id}\n{module.SchemaVersion}\n{module.SerializerId}\n{module.CompressionId}\n{Convert.ToHexString(hash)}");
    }

    private static byte[] BuildAssociatedData(ChunkEntry entry)
    {
        return Encoding.UTF8.GetBytes($"{entry.Key}\n{entry.ModuleVersion}\n{entry.SerializerId}\n{entry.CompressionId}\n{Convert.ToHexString(entry.HashBytes ?? [])}");
    }

    private static void VerifyCompatibility(SaveHeader header, LoadCompatibility? expected)
    {
        if (expected is null)
        {
            return;
        }
        if (expected.ApplicationId is not null && !string.Equals(expected.ApplicationId, header.ApplicationId, StringComparison.Ordinal))
        {
            throw new DeliveranceException(SaveDiagnosticCode.ApplicationMismatch, $"Application id mismatch: save '{header.ApplicationId}', runtime '{expected.ApplicationId}'.");
        }
        if (expected.ApplicationSaveVersion is int applicationSaveVersion
            && applicationSaveVersion != header.ApplicationSaveVersion)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.ApplicationMismatch,
                $"Application save version mismatch: save '{header.ApplicationSaveVersion}', runtime '{applicationSaveVersion}'.");
        }
        if (expected.DefinitionHash is not null && !string.Equals(expected.DefinitionHash, header.DefinitionHash, StringComparison.Ordinal))
        {
            throw new DeliveranceException(SaveDiagnosticCode.DefinitionMismatch, $"Definition hash mismatch: save '{header.DefinitionHash}', runtime '{expected.DefinitionHash}'.");
        }
        if (expected.RequireCadenceMatch && expected.CadenceConfigHash is not null && !string.Equals(expected.CadenceConfigHash, header.CadenceConfigHash, StringComparison.Ordinal))
        {
            throw new DeliveranceException(SaveDiagnosticCode.CadenceMismatch, $"Cadence hash mismatch: save '{header.CadenceConfigHash}', runtime '{expected.CadenceConfigHash}'.");
        }
    }

    private static void SetOffsets(SaveHeader header, List<ChunkEntry> directory)
    {
        byte[] prefix = SaveContainerWriter.WritePrefixOnly(header, directory);
        long offset = prefix.LongLength;
        for (int index = 0; index < directory.Count; index++)
        {
            directory[index] = directory[index] with { Offset = offset };
            offset += directory[index].Length;
        }
    }

    private async Task WriteContainerAsync(
        string slotId,
        SaveHeader header,
        IReadOnlyList<ChunkEntry> directory,
        IReadOnlyList<ReadOnlyMemory<byte>> payloads,
        byte[] prefix,
        CancellationToken ct)
    {
        if (Options.Store is IStreamingSaveStore streaming)
        {
            var segments = new ReadOnlyMemory<byte>[payloads.Count + 1];
            segments[0] = prefix;
            for (int index = 0; index < payloads.Count; index++)
            {
                segments[index + 1] = payloads[index];
            }
            await using var stream = new SegmentedReadStream(segments);
            await streaming.WriteAsync(slotId, stream, Options.BackupCopiesToKeep, ct).ConfigureAwait(false);
            return;
        }
        await Options.Store.WriteSlotAsync(
            slotId,
            SaveContainerWriter.Write(header, directory, payloads),
            Options.BackupCopiesToKeep,
            ct).ConfigureAwait(false);
    }

    private async Task<SaveContainerReader> ReadContainerAsync(string slotId, CancellationToken ct)
    {
        if (Options.Store is IStreamingSaveStore streaming)
        {
            await using Stream stream = await streaming.OpenReadAsync(slotId, ct).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct).ConfigureAwait(false);
            return SaveContainerReader.Read(memory.ToArray());
        }
        return SaveContainerReader.Read(await Options.Store.ReadSlotAsync(slotId, ct).ConfigureAwait(false));
    }

    private void HandleMissingChunk(string slotId, string key)
    {
        string message = $"Slot '{slotId}' is missing module '{key}'.";
        if (Options.MissingChunkPolicy == MissingChunkPolicy.Error)
        {
            throw new InvalidDataException(message);
        }
        if (Options.MissingChunkPolicy == MissingChunkPolicy.Warn)
        {
            Diagnostics.EmitWarning(message);
        }
    }

    private void HandleVersionMismatch(string slotId, string key, int fromVersion, int toVersion)
    {
        string message = $"Slot '{slotId}' module '{key}' has schema {fromVersion}, but runtime expects {toVersion}.";
        if (Options.VersionMismatchPolicy == VersionMismatchPolicy.Error)
        {
            throw new InvalidDataException(message);
        }
        if (Options.VersionMismatchPolicy == VersionMismatchPolicy.Warn)
        {
            Diagnostics.EmitWarning(message);
        }
    }
}
