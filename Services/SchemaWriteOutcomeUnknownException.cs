namespace dvtui.Services;

public sealed class SchemaWriteOutcomeUnknownException : Exception
{
    public SchemaWriteOutcomeUnknownException(
        string message,
        Guid? metadataId,
        Exception innerException
    )
        : base(message, innerException)
    {
        MetadataId = metadataId;
    }

    public Guid? MetadataId { get; }
}
