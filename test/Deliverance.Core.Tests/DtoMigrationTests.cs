using Deliverance.Core;
using Deliverance.Core.IO;
using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;
using Deliverance.Core.Tests.TestDtos;
using Xunit;

namespace Deliverance.Core.Tests;

public sealed class DtoMigrationTests
{
    [Fact]
    public async Task MigratableModule_Upgrades_V1_To_V2_OnLoad()
    {
        var store = new InMemorySaveStore();
        var serializer = new MessagePackSaveSerializer();

        // Save with V1
        var saveV1 = new DeliveranceService(new DeliveranceOptions { Store = store, Serializer = serializer });
        var v1 = new MigratingCounterModuleV1 { Value = 7 };
        saveV1.Register(v1);
        await saveV1.SaveSlotAsync("slot1");

        // Load with V2 (migrates)
        var loadV2 = new DeliveranceService(new DeliveranceOptions { Store = store, Serializer = serializer });
        var v2 = new MigratingCounterModuleV2();
        loadV2.Register(v2);
        await loadV2.LoadSlotAsync("slot1");

        Assert.Equal(7, v2.Value);
        Assert.Equal(100, v2.Bonus);
    }

    // ----- Test module + DTOs -----

    private sealed class MigratingCounterModuleV1 : ISaveModule
    {
        public string Key => "migratingCounter";
        public int Version => 1;

        public int Value { get; set; }

        public void Capture(ISaveWriter w) => w.Write(new CounterDtoV1 { Value = Value });

        public void Restore(ISaveReader r)
        {
            var dto = r.Read<CounterDtoV1>();
            Value = dto.Value;
        }
    }


    /// <summary>
    /// A module whose current schema is V2, but can upgrade V1 -> V2.
    /// This test module supports migrations via DTO interfaces.
    /// </summary>
    private sealed class MigratingCounterModuleV2 : ISaveModule, IDtoMigratableSaveModule, IDtoRestorableSaveModule
    {
        public string Key => "migratingCounter";
        public int Version => 2;

        public int Value { get; set; }
        public int Bonus { get; set; }

        public void Capture(ISaveWriter w) => w.Write(new CounterDtoV2 { Value = Value, Bonus = Bonus });

        public void Restore(ISaveReader r)
        {
            var dto = r.Read<CounterDtoV2>();
            Value = dto.Value;
            Bonus = dto.Bonus;
        }

        public Type GetDtoType(int version) => version switch
        {
            1 => typeof(CounterDtoV1),
            2 => typeof(CounterDtoV2),
            _ => throw new NotSupportedException($"Unsupported dto version {version}."),
        };

        public object UpgradeDto(object dto, int fromVersion) => fromVersion switch
        {
            1 => new CounterDtoV2 { Value = ((CounterDtoV1)dto).Value, Bonus = 100 },
            _ => throw new NotSupportedException($"No upgrade path from version {fromVersion}."),
        };

        public void RestoreFromDto(object dto)
        {
            var v2 = dto as CounterDtoV2
                ?? throw new InvalidDataException($"Expected {nameof(CounterDtoV2)} after migration, got {dto.GetType()}.");

            Value = v2.Value;
            Bonus = v2.Bonus;
        }
    }

}

