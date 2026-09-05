namespace Deliverance.Core.Format;

public readonly record struct SaveHeader(
    int ContainerVersion,
    long UtcUnixSeconds,
    string? BuildId,
    string? ApplicationId = null,
    string? ApplicationVersion = null,
    int ApplicationSaveVersion = 0,
    string? DefinitionHash = null,
    string? CadenceConfigHash = null,
    byte Flags = 0
);
