namespace Deliverance.Core.Modules;

/// <summary>
/// Optional interface for modules that can upgrade older DTO payloads to the module's current Version.
/// DTO upgrades happen explicitly in code (no reflection/auto-magic).
/// </summary>
public interface IDtoMigratableSaveModule : ISaveModule
{
    /// <summary>Returns the DTO type used to serialize this module at a historical version.</summary>
    Type GetDtoType(int version);

    /// <summary>
    /// Upgrade a DTO instance from (fromVersion) -> (fromVersion + 1).
    /// The returned object is the next-version DTO instance.
    /// </summary>
    object UpgradeDto(object dto, int fromVersion);
}
