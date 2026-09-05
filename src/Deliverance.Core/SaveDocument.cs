using Deliverance.Core.Modules;
using Deliverance.Core.Serialization;

namespace Deliverance.Core;

public sealed record SaveApplicationMetadata(
    string? ApplicationId = null,
    string? ApplicationVersion = null,
    string? BuildId = null,
    string? DefinitionHash = null,
    string? CadenceConfigHash = null,
    int? ApplicationSaveVersion = null);

public sealed record SaveRequest(
    SaveApplicationMetadata Metadata,
    IReadOnlyList<SaveModulePayload> Modules,
    long CreatedUtcUnixSeconds = 0);

public sealed record LoadCompatibility(
    string? ApplicationId = null,
    string? DefinitionHash = null,
    string? CadenceConfigHash = null,
    bool RequireCadenceMatch = true,
    int? ApplicationSaveVersion = null);

public sealed record LoadedModuleCandidate(
    string Id,
    int SchemaVersion,
    ModuleCriticality Criticality,
    byte SerializerId,
    byte CompressionId,
    ReadOnlyMemory<byte> Payload,
    string SemanticHash);

public sealed class LoadedSaveCandidate
{
    private readonly IReadOnlyDictionary<string, LoadedModuleCandidate> modules;

    public SaveApplicationMetadata Metadata { get; }
    public long CreatedUtcUnixSeconds { get; }
    public IReadOnlyList<LoadedModuleCandidate> Modules { get; }

    internal LoadedSaveCandidate(
        SaveApplicationMetadata metadata,
        long createdUtcUnixSeconds,
        IReadOnlyList<LoadedModuleCandidate> modules)
    {
        Metadata = metadata;
        CreatedUtcUnixSeconds = createdUtcUnixSeconds;
        Modules = modules;
        this.modules = modules.ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    public LoadedModuleCandidate GetModule(string id)
    {
        return modules.TryGetValue(id, out LoadedModuleCandidate? module)
            ? module
            : throw new KeyNotFoundException($"Loaded candidate does not contain module '{id}'.");
    }

    public T Deserialize<T>(string id, ISaveSerializerRegistry serializers)
    {
        LoadedModuleCandidate module = GetModule(id);
        if (!serializers.TryGet(module.SerializerId, out ISaveSerializer serializer))
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.SerializerUnavailable,
                $"Serializer id '{module.SerializerId}' required by module '{id}' is unavailable.");
        }
        try
        {
            return serializer.Deserialize<T>(module.Payload);
        }
        catch (Exception exception) when (exception is not DeliveranceException)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.SerializationFailed,
                $"Serializer '{serializer.Name}' failed while decoding module '{id}'.",
                exception);
        }
    }
}
