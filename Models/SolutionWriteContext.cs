namespace dvtui.Models;

public sealed class SolutionWriteContext
{
    public Guid SolutionId { get; init; }
    public string SolutionUniqueName { get; init; } = string.Empty;
    public bool IsManaged { get; init; }
    public Guid PublisherId { get; init; }
    public string PublisherPrefix { get; init; } = string.Empty;
    public string BaseLanguage { get; init; } = string.Empty;
    public string EnvironmentUrl { get; init; } = string.Empty;
}
