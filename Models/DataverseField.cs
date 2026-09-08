namespace dvtui.Models;

public sealed class DataverseField
{
    public string SchemaName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
