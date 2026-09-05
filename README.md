# Deliverance

Deliverance is not an object serializer. It is a container and serializer workshop for explicitly captured application state.

The application owns semantic state, immutable snapshot capture, DTO schemas, migrations, validation, and commit. Deliverance owns deterministic module packing, codec metadata, compression, optional authenticated encryption, integrity checks, safe slots, and storage.

```csharp
var serializer = new MessagePackSaveSerializer();
var store = new FileSaveStore("saves");
var deliverance = new DeliveranceService(new DeliveranceOptions
{
    Store = store,
    Serializer = serializer,
});

SaveModulePayload world = SaveModulePayload.Create(
    "world",
    schemaVersion: 3,
    ModuleCriticality.Required,
    serializer,
    new NoCompressionCodec(),
    WorldSnapshot.Capture(state));

await deliverance.SaveAsync(
    "slot-1",
    new SaveRequest(
        new SaveApplicationMetadata(
            ApplicationId: "my-game",
            ApplicationVersion: "1.4.2",
            DefinitionHash: definitions.Hash,
            ApplicationSaveVersion: 2),
        [world]));

LoadedSaveCandidate candidate = await deliverance.LoadAsync(
    "slot-1",
    [WorldModule.Definition],
    new LoadCompatibility("my-game", definitions.Hash, ApplicationSaveVersion: 2));

WorldSnapshot loaded = candidate.Deserialize<WorldSnapshot>("world", deliverance.Options.Serializers);
Validate(loaded);
Commit(loaded);
```

The default extension is `.dlv` (suggested media type `application/vnd.deliverance.save`). `FileSaveStore` derives safe filenames from semantic slot IDs, writes and flushes a unique temporary file, atomically replaces the primary, and keeps a bounded deterministic `.bak1`, `.bak2`, ... chain. Backup promotion is an explicit application/recovery-tool decision; a corrupt primary is never silently replaced.

## Versions

- Container format version changes only when the `DLVR` header, directory, or payload layout changes. The current writer emits v2.
- Each module owns an independent positive schema version and consecutive forward migrations.
- Application save version is optional composition compatibility metadata.
- application/build strings, definition hash, and cadence hash are provenance/compatibility metadata, not container versions.
- Replay format belongs to the application replay envelope, not the save container.

The v2 payload pipeline is `explicit bytes -> compression -> AES-GCM when opted in -> store`; load reverses it. SHA-256 is recorded over the uncompressed semantic module bytes. AES-GCM uses a fresh 96-bit nonce and 128-bit tag per module. Callers provide 256-bit keys through `IEncryptionKeyProvider`; Deliverance never creates or persists a universal secret.

`Deliverance.Dominatus` supplies deferred `SaveSlotActuation` and `LoadSlotActuation` handlers. Snapshot capture occurs synchronously under application authority, work runs off-thread, and candidate commit/completion publication occurs when the application pumps the actuator on its authoritative thread.

```csharp
ActuationDispatchResult save = host.Dispatch(ctx, new SaveSlotActuation("slot-1"));
// Later, on the authoritative application thread:
persistenceActuator.PumpCompletions();

ActuationDispatchResult load = host.Dispatch(ctx, new LoadSlotActuation("slot-1"));
persistenceActuator.PumpCompletions(); // bridge validates and commits the candidate
```

Replay remains application semantics: capture a checkpoint, record ordered domain intents plus expected state hashes, and run those intents through the ordinary resolver. It may reuse Deliverance bytes or storage, but it is not a save-file mode.

The optional Stride connector remains a thin service-registration adapter. It does not define Core semantics.
