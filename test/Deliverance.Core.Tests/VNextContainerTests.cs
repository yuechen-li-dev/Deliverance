using System.Buffers.Binary;
using Deliverance.Core.Codecs;
using Deliverance.Core.Format;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;

namespace Deliverance.Core.Tests;

public sealed class VNextContainerTests
{
    [Fact]
    public async Task ExplicitSnapshot_RoundTripsAsCandidate_WithSeparatedMetadata()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService deliverance = Create(store, serializer);
        var gzip = new GzipCodec();
        deliverance.Options.Codecs.Register(gzip);
        SaveModulePayload world = SaveModulePayload.Create(
            "world",
            3,
            ModuleCriticality.Required,
            serializer,
            gzip,
            new WorldDto(17, "farm"));

        await deliverance.SaveAsync(
            "slot-a",
            new SaveRequest(
                new SaveApplicationMetadata("tiny-farm", "6", "abc", "definitions-1", "cadence-1", 2),
                [world],
                CreatedUtcUnixSeconds: 123));

        LoadedSaveCandidate candidate = await deliverance.LoadAsync(
            "slot-a",
            [new SaveModuleDefinition("world", 3, ModuleCriticality.Required)],
            new LoadCompatibility("tiny-farm", "definitions-1", "cadence-1", ApplicationSaveVersion: 2));

        Assert.Equal(new WorldDto(17, "farm"), candidate.Deserialize<WorldDto>("world", deliverance.Options.Serializers));
        Assert.Equal("abc", candidate.Metadata.BuildId);
        Assert.Equal(2, candidate.Metadata.ApplicationSaveVersion);
        Assert.Equal(123, candidate.CreatedUtcUnixSeconds);
        Assert.Equal(64, candidate.GetModule("world").SemanticHash.Length);
        SlotInspection inspection = await deliverance.InspectSlotAsync("slot-a");
        Assert.Equal(serializer.Id, inspection.Chunks.Single().SerializerId);
        Assert.Equal(gzip.Id, inspection.Chunks.Single().CompressionId);
    }

    [Fact]
    public async Task UnencryptedSave_IsByteDeterministic_WhenProvenanceIsStable()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService deliverance = Create(store, serializer);
        SaveModulePayload payload = SaveModulePayload.Create(
            "world", 1, ModuleCriticality.Required, serializer, new NoCompressionCodec(), new WorldDto(1, "same"));
        SaveRequest request = new(new SaveApplicationMetadata("app", "1"), [payload], 42);

        await deliverance.SaveAsync("left", request);
        await deliverance.SaveAsync("right", request);

        Assert.Equal(store.GetBytes("left"), store.GetBytes("right"));
    }

    [Fact]
    public async Task ContainerRejectsBadMagicUnsupportedVersionAndPayloadCorruption()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService deliverance = Create(store, serializer);
        SaveModuleDefinition definition = new("world", 1, ModuleCriticality.Required);

        store.SetBytes("bad-magic", "NOPE"u8.ToArray());
        DeliveranceException badMagic = await Assert.ThrowsAsync<DeliveranceException>(
            () => deliverance.LoadAsync("bad-magic", [definition]));
        Assert.Equal(SaveDiagnosticCode.BadMagic, badMagic.Code);

        byte[] unsupported = new byte[8];
        "DLVR"u8.CopyTo(unsupported);
        BinaryPrimitives.WriteInt32LittleEndian(unsupported.AsSpan(4), 99);
        store.SetBytes("unsupported", unsupported);
        DeliveranceException version = await Assert.ThrowsAsync<DeliveranceException>(
            () => deliverance.LoadAsync("unsupported", [definition]));
        Assert.Equal(SaveDiagnosticCode.UnsupportedContainerVersion, version.Code);

        SaveModulePayload payload = SaveModulePayload.Create(
            "world", 1, ModuleCriticality.Required, serializer, new NoCompressionCodec(), new WorldDto(1, "ok"));
        await deliverance.SaveAsync("corrupt", new SaveRequest(new SaveApplicationMetadata(), [payload]));
        byte[] corrupt = store.GetBytes("corrupt");
        corrupt[^1] ^= 0x40;
        store.SetBytes("corrupt", corrupt);
        DeliveranceException checksum = await Assert.ThrowsAsync<DeliveranceException>(
            () => deliverance.LoadAsync("corrupt", [definition]));
        Assert.Equal(SaveDiagnosticCode.CorruptPayload, checksum.Code);
    }

    [Fact]
    public async Task RequiredOptionalAndNewerSchemaLawsAreExplicit()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService deliverance = Create(store, serializer);
        SaveModulePayload optional = SaveModulePayload.Create(
            "optional", 2, ModuleCriticality.Optional, serializer, new NoCompressionCodec(), 7);
        await deliverance.SaveAsync("slot", new SaveRequest(new SaveApplicationMetadata(), [optional]));

        LoadedSaveCandidate skipped = await deliverance.LoadAsync("slot", []);
        Assert.Empty(skipped.Modules);

        DeliveranceException missing = await Assert.ThrowsAsync<DeliveranceException>(
            () => deliverance.LoadAsync("slot", [new SaveModuleDefinition("required", 1, ModuleCriticality.Required)]));
        Assert.Equal(SaveDiagnosticCode.MissingRequiredModule, missing.Code);

        SaveModuleDefinition olderRequired = new("optional", 1, ModuleCriticality.Required);
        DeliveranceException newer = await Assert.ThrowsAsync<DeliveranceException>(
            () => deliverance.LoadAsync("slot", [olderRequired]));
        Assert.Equal(SaveDiagnosticCode.NewerModuleSchema, newer.Code);
    }

    [Fact]
    public async Task ModuleOwnedMigration_AdvancesV1ThroughV2ToV3()
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService deliverance = Create(store, serializer);
        SaveModulePayload v1 = SaveModulePayload.Create(
            "counter", 1, ModuleCriticality.Required, serializer, new NoCompressionCodec(), 5);
        await deliverance.SaveAsync("slot", new SaveRequest(new SaveApplicationMetadata(), [v1]));

        SaveModuleDefinition current = new(
            "counter",
            3,
            ModuleCriticality.Required,
            [
                new ModuleMigration(1, bytes => serializer.Serialize(serializer.Deserialize<int>(bytes) + 10)),
                new ModuleMigration(2, bytes => serializer.Serialize(serializer.Deserialize<int>(bytes) * 2)),
            ]);
        LoadedSaveCandidate candidate = await deliverance.LoadAsync("slot", [current]);

        Assert.Equal(30, candidate.Deserialize<int>("counter", deliverance.Options.Serializers));
        Assert.Equal(3, candidate.GetModule("counter").SchemaVersion);
    }

    [Theory]
    [InlineData("definitions-other", null, SaveDiagnosticCode.DefinitionMismatch)]
    [InlineData(null, "cadence-other", SaveDiagnosticCode.CadenceMismatch)]
    public async Task CompatibilityMismatch_HasTypedDiagnostic(
        string? definitionHash,
        string? cadenceHash,
        SaveDiagnosticCode expectedCode)
    {
        var store = new InMemorySaveStore();
        MessagePackSaveSerializer serializer = new();
        DeliveranceService deliverance = Create(store, serializer);
        SaveModulePayload payload = SaveModulePayload.Create(
            "world", 1, ModuleCriticality.Required, serializer, new NoCompressionCodec(), 1);
        await deliverance.SaveAsync(
            "slot",
            new SaveRequest(new SaveApplicationMetadata("app", DefinitionHash: "definitions", CadenceConfigHash: "cadence"), [payload]));

        DeliveranceException exception = await Assert.ThrowsAsync<DeliveranceException>(() => deliverance.LoadAsync(
            "slot",
            [new SaveModuleDefinition("world", 1, ModuleCriticality.Required)],
            new LoadCompatibility("app", definitionHash ?? "definitions", cadenceHash ?? "cadence")));
        Assert.Equal(expectedCode, exception.Code);
    }

    private static DeliveranceService Create(InMemorySaveStore store, MessagePackSaveSerializer serializer)
    {
        return new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            BackupCopiesToKeep = 0,
        });
    }

    public sealed record WorldDto(int Day, string Scene);
}
