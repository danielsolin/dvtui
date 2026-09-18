namespace dvtui.Models;

public sealed class DataverseSolutionComponent
{
    public Guid Id { get; init; }
    public Guid? ObjectId { get; init; }
    public int ComponentType { get; init; }
    public Guid? RootComponentId { get; init; }
    public int? RootComponentBehavior { get; init; }
    public DataverseEntity? Entity { get; set; }
    public string? ResolutionError { get; set; }
}
