namespace Deliverance.Core.Modules;

/// <summary>
/// Optional interface to apply a fully-migrated DTO without reserializing.
/// Recommended for migratable modules.
/// </summary>
public interface IDtoRestorableSaveModule
{
    void RestoreFromDto(object dto);
}
