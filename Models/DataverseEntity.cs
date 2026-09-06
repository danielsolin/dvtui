namespace Dvtui.Models;

public sealed class DataverseEntity
{
    public string LogicalName { get; init; } = string.Empty;
    public string SchemaName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string CollectionName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string EntitySetName { get; init; } = string.Empty;
    public string PrimaryIdAttribute { get; init; } = string.Empty;
    public string PrimaryNameAttribute { get; init; } = string.Empty;
    public string? OwnershipType { get; init; }
    public int? ObjectTypeCode { get; init; }
    public bool? IsCustom { get; init; }
    public bool? IsCustomizable { get; init; }
    public bool? IsManaged { get; init; }
    public bool? IsActivity { get; init; }
}
