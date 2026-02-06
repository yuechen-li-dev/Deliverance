using Deliverance.Core;
using Deliverance.Core.IO;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class VersionMismatchPolicyTests
{
    [Fact]
    public async Task VersionMismatch_Error_Throws()
    {
        var store = new InMemorySaveStore();
        var serializer = new MessagePackSaveSerializer();

        // Save with V1 module
        var saveV1 = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            VersionMismatchPolicy = VersionMismatchPolicy.Error,
        });

        var v1 = new IntModuleV1("vermod") { Value = 123 };
        saveV1.Register(v1);

        await saveV1.SaveSlotAsync("slot1");

        // Load with V2 module (same key, different version, NOT migratable)
        var loadV2 = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            VersionMismatchPolicy = VersionMismatchPolicy.Error,
        });

        var v2 = new IntModuleV2_NoMigration("vermod") { Value = 0 };
        loadV2.Register(v2);

        await Assert.ThrowsAsync<InvalidDataException>(() => loadV2.LoadSlotAsync("slot1"));
    }

    [Fact]
    public async Task VersionMismatch_Warn_EmitsWarning_And_SkipsRestore()
    {
        var store = new InMemorySaveStore();
        var serializer = new MessagePackSaveSerializer();

        // Save with V1 module
        var saveV1 = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            VersionMismatchPolicy = VersionMismatchPolicy.Error,
        });

        var v1 = new IntModuleV1("vermod") { Value = 777 };
        saveV1.Register(v1);

        await saveV1.SaveSlotAsync("slot1");

        // Load with V2 module (no migration) under WARN policy
        var loadV2 = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            VersionMismatchPolicy = VersionMismatchPolicy.Warn,
        });

        var warned = false;
        loadV2.Diagnostics.Warning += _ => warned = true;

        var v2 = new IntModuleV2_NoMigration("vermod") { Value = 42 }; // sentinel
        loadV2.Register(v2);

        await loadV2.LoadSlotAsync("slot1");

        Assert.True(warned);
        // Must NOT have restored from the mismatched chunk
        Assert.Equal(42, v2.Value);
    }

    [Fact]
    public async Task VersionMismatch_Ignore_SkipsRestore_Silently()
    {
        var store = new InMemorySaveStore();
        var serializer = new MessagePackSaveSerializer();

        // Save with V1 module
        var saveV1 = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            VersionMismatchPolicy = VersionMismatchPolicy.Error,
        });

        var v1 = new IntModuleV1("vermod") { Value = 5 };
        saveV1.Register(v1);

        await saveV1.SaveSlotAsync("slot1");

        // Load with V2 module (no migration) under IGNORE policy
        var loadV2 = new DeliveranceService(new DeliveranceOptions
        {
            Store = store,
            Serializer = serializer,
            VersionMismatchPolicy = VersionMismatchPolicy.Ignore,
        });

        var warned = false;
        loadV2.Diagnostics.Warning += _ => warned = true;

        var v2 = new IntModuleV2_NoMigration("vermod") { Value = 9001 }; // sentinel
        loadV2.Register(v2);

        await loadV2.LoadSlotAsync("slot1");

        Assert.False(warned);
        Assert.Equal(9001, v2.Value);
    }

    // --- local helper modules ---

    private sealed class IntModuleV1(string key) : ISaveModule
    {
        public string Key { get; } = key;
        public int Version => 1;

        public int Value { get; set; }

        public void Capture(ISaveWriter w) => w.Write(Value);

        public void Restore(ISaveReader r) => Value = r.Read<int>();
    }

    /// <summary>
    /// Same chunk key as V1, but Version=2 and NO migration interface.
    /// This is what VersionMismatchPolicy must handle.
    /// </summary>
    private sealed class IntModuleV2_NoMigration(string key) : ISaveModule
    {
        public string Key { get; } = key;
        public int Version => 2;

        public int Value { get; set; }

        // Save/restore shape is irrelevant; we must not restore when versions mismatch.
        public void Capture(ISaveWriter w) => w.Write(Value);

        public void Restore(ISaveReader r) => Value = r.Read<int>();
    }
}
