namespace dvtui.Models;

public sealed class ColumnCapability
{
    public bool CanCreateColumn { get; init; }
    public string? CreateColumnReason { get; init; }
    public bool CanEdit { get; init; }
    public string? EditReason { get; init; }
    public bool CanDelete { get; init; }
    public string? DeleteReason { get; init; }
    public bool CanEditDisplayName { get; init; }
    public string? EditDisplayNameReason { get; init; }
    public bool CanEditDescription { get; init; }
    public string? EditDescriptionReason { get; init; }
    public bool CanEditRequirement { get; init; }
    public string? EditRequirementReason { get; init; }
    public bool CanIncreaseLength { get; init; }
    public string? IncreaseLengthReason { get; init; }
}
