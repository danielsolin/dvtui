namespace dvtui.Models;

public sealed class CreateTableRequest
{
    public SolutionWriteContext Context { get; init; } = null!;
    public string DisplayName { get; init; } = string.Empty;
    public string PluralDisplayName { get; init; } = string.Empty;
    public string SchemaSuffix { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsUserOwned { get; init; }
    public string PrimaryNameDisplayName { get; init; } = string.Empty;
    public string PrimaryNameSchemaSuffix { get; init; } = string.Empty;
    public int PrimaryNameMaxLength { get; init; } = ColumnDefaults.PrimaryNameDefaultLength;
}

public sealed class CreateColumnRequest
{
    public SolutionWriteContext Context { get; init; } = null!;
    public string TableLogicalName { get; init; } = string.Empty;
    public Guid TableMetadataId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string SchemaSuffix { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ColumnKind Kind { get; init; }
    public int? MaxLength { get; init; }
    public decimal? MinValue { get; init; }
    public int? Precision { get; init; }
    public decimal? MaxValue { get; init; }
    public bool BooleanDefaultValue { get; init; }
    public string BooleanTrueLabel { get; init; } = "Yes";
    public string BooleanFalseLabel { get; init; } = "No";
    public string RequirementLevel { get; init; } = RequirementLevels.Optional;
}

public sealed class UpdateColumnRequest
{
    public SolutionWriteContext Context { get; init; } = null!;
    public string TableLogicalName { get; init; } = string.Empty;
    public Guid TableMetadataId { get; init; }
    public string ColumnLogicalName { get; init; } = string.Empty;
    public Guid ExpectedMetadataId { get; init; }
    public bool SetDisplayName { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool SetDescription { get; init; }
    public string Description { get; init; } = string.Empty;
    public int? NewMaxLength { get; init; }
    public bool SetRequirementLevel { get; init; }
    public string RequirementLevel { get; init; } = RequirementLevels.Optional;
}

public sealed class DeleteColumnRequest
{
    public string TableLogicalName { get; init; } = string.Empty;
    public string ColumnLogicalName { get; init; } = string.Empty;
    public Guid ExpectedMetadataId { get; init; }
    public Guid TableMetadataId { get; init; }
    public SolutionWriteContext? Context { get; init; }
}

public sealed class PublishTableRequest
{
    public string TableLogicalName { get; init; } = string.Empty;
    public Guid TableMetadataId { get; init; }
    public SolutionWriteContext? Context { get; init; }
}

public sealed class ColumnUpdateResult
{
    public Guid MetadataId { get; init; }
    public bool Changed { get; init; }
}

public sealed class DependencyInfo
{
    public int? ComponentType { get; init; }
    public Guid? ObjectId { get; init; }
    public string Name { get; init; } = string.Empty;
}
