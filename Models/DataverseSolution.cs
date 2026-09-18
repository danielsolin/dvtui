namespace dvtui.Models;

public sealed class DataverseSolution
{
    public Guid Id { get; init; }
    public string FriendlyName { get; init; } = string.Empty;
    public string UniqueName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public bool? IsManaged { get; init; }
    public string Description { get; init; } = string.Empty;
}
