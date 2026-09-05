namespace Deliverance.Core;

public enum SaveDiagnosticCode
{
    BadMagic,
    UnsupportedContainerVersion,
    DuplicateModule,
    UnknownRequiredModule,
    MissingRequiredModule,
    NewerModuleSchema,
    MigrationUnavailable,
    MigrationFailed,
    SerializerUnavailable,
    SerializationFailed,
    CompressionUnavailable,
    CompressionFailed,
    DecompressionFailed,
    EncryptionUnavailable,
    EncryptionFailed,
    DecryptionFailed,
    EncryptionKeyUnavailable,
    AuthenticationFailed,
    CorruptPayload,
    InvalidContainer,
    ApplicationMismatch,
    DefinitionMismatch,
    CadenceMismatch,
    AtomicWriteFailed,
}

public sealed class DeliveranceException : IOException
{
    public SaveDiagnosticCode Code { get; }

    public DeliveranceException(SaveDiagnosticCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public DeliveranceException(SaveDiagnosticCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
