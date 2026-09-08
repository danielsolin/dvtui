namespace dvtui.Models;

public sealed class DataverseEntityDetails
{
    public DataverseEntity Entity { get; init; } = new DataverseEntity();
    public IReadOnlyList<DataverseField> Fields { get; init; } = [];
}
