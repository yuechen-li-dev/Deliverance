using Deliverance.Core.Codecs;
using Deliverance.Core.Serialization;

namespace Deliverance.Core.Modules;

/// <summary>An immutable, explicitly captured domain payload. Deliverance never discovers application state.</summary>
public sealed record SaveModulePayload(
    string Id,
    int SchemaVersion,
    ModuleCriticality Criticality,
    byte SerializerId,
    byte CompressionId,
    ReadOnlyMemory<byte> Bytes)
{
    public static SaveModulePayload Create<T>(
        string id,
        int schemaVersion,
        ModuleCriticality criticality,
        ISaveSerializer serializer,
        ICompressionCodec compression,
        T snapshot)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(compression);
        ValidateIdentity(id, schemaVersion);
        try
        {
            return new SaveModulePayload(
                id,
                schemaVersion,
                criticality,
                serializer.Id,
                compression.Id,
                serializer.Serialize(snapshot));
        }
        catch (Exception exception) when (exception is not DeliveranceException)
        {
            throw new DeliveranceException(
                SaveDiagnosticCode.SerializationFailed,
                $"Serializer '{serializer.Name}' failed while capturing module '{id}'.",
                exception);
        }
    }

    internal static void ValidateIdentity(string id, int schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Module id must be non-empty.", nameof(id));
        }
        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Module schema versions begin at 1.");
        }
    }
}
