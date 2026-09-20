using dvtui.Models;

namespace dvtui.Services;

public interface IDataverseQueryService
{
    Task<List<DataverseSolution>> GetSolutionsAsync(
        CancellationToken cancellationToken
    );

    Task<List<DataverseSolutionComponent>> GetSolutionComponentsAsync(
        Guid solutionId,
        CancellationToken cancellationToken
    );

    Task<DataverseEntityDetails> GetEntityAsync(
        string tableLogicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    );

    Task<DataverseEntityDetails> GetEntityByLogicalNameAsync(
        string tableLogicalName,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<DataverseColumn>> GetColumnsAsync(
        string tableLogicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    );
}
