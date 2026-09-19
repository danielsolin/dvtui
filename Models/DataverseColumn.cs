namespace dvtui.Models;

public enum ColumnKind
{
    Text,
    MultilineText,
    WholeNumber,
    Decimal,
    YesNo,
    Unknown
}

public sealed class DataverseColumn
{
    public Guid MetadataId { get; init; }
    public string LogicalName { get; init; } = string.Empty;
    public string SchemaName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ColumnKind Kind { get; init; }
    public int? MaxLength { get; init; }
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public int? Precision { get; init; }
    public bool? IsCustom { get; init; }
    public bool? IsManaged { get; init; }
    public bool? IsPrimaryId { get; init; }
    public bool? IsPrimaryName { get; init; }
    public bool? IsCustomizable { get; init; }
    public bool? IsRenameable { get; init; }
    public bool? CanModifyAdditionalSettings { get; init; }
    public bool? CanChangeRequirement { get; init; }
    public string? RequirementLevel { get; init; }
    public string? AttributeTypeCode { get; init; }
    public string? AttributeFormat { get; init; }
    public string? AttributeOf { get; init; }
    public bool? IsLogical { get; init; }
    public int? SourceType { get; init; }
    public string? AutoNumberFormat { get; init; }
    public bool? BooleanDefaultValue { get; init; }
    public string? BooleanTrueLabel { get; init; }
    public string? BooleanFalseLabel { get; init; }
}
