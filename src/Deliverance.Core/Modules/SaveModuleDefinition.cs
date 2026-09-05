namespace Deliverance.Core.Modules;

public sealed record ModuleMigration(
    int FromVersion,
    int ToVersion,
    Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> Migrate)
{
    public ModuleMigration(int fromVersion, Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> migrate)
        : this(fromVersion, checked(fromVersion + 1), migrate)
    {
    }
}

/// <summary>Application-owned schema and forward migration definition for one semantic module.</summary>
public sealed class SaveModuleDefinition
{
    private readonly IReadOnlyDictionary<int, ModuleMigration> migrations;

    public string Id { get; }
    public int CurrentSchemaVersion { get; }
    public ModuleCriticality Criticality { get; }
    public Action<ReadOnlyMemory<byte>>? ValidateCurrentPayload { get; }

    public SaveModuleDefinition(
        string id,
        int currentSchemaVersion,
        ModuleCriticality criticality,
        IEnumerable<ModuleMigration>? migrations = null,
        Action<ReadOnlyMemory<byte>>? validateCurrentPayload = null)
    {
        SaveModulePayload.ValidateIdentity(id, currentSchemaVersion);
        Id = id;
        CurrentSchemaVersion = currentSchemaVersion;
        Criticality = criticality;
        ValidateCurrentPayload = validateCurrentPayload;

        ModuleMigration[] ordered = (migrations ?? []).OrderBy(item => item.FromVersion).ToArray();
        foreach (ModuleMigration migration in ordered)
        {
            if (migration.Migrate is null || migration.FromVersion < 1 || migration.ToVersion != migration.FromVersion + 1)
            {
                throw new ArgumentException("Migrations must be explicit consecutive forward steps.", nameof(migrations));
            }
        }
        this.migrations = ordered.ToDictionary(item => item.FromVersion);
    }

    internal ReadOnlyMemory<byte> Upgrade(int fromVersion, ReadOnlyMemory<byte> payload)
    {
        int version = fromVersion;
        ReadOnlyMemory<byte> current = payload;
        while (version < CurrentSchemaVersion)
        {
            if (!migrations.TryGetValue(version, out ModuleMigration? migration))
            {
                throw new DeliveranceException(
                    SaveDiagnosticCode.MigrationUnavailable,
                    $"Module '{Id}' has no migration from schema {version} to {version + 1}.");
            }

            try
            {
                current = migration.Migrate(current);
            }
            catch (Exception exception) when (exception is not DeliveranceException)
            {
                throw new DeliveranceException(
                    SaveDiagnosticCode.MigrationFailed,
                    $"Module '{Id}' migration from schema {version} to {version + 1} failed.",
                    exception);
            }
            version++;
        }
        return current;
    }
}
