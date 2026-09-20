using dvtui.Models;

namespace dvtui.Services;

public interface IDataverseSchemaService
{
    Task<SolutionWriteContext> LoadWriteContextAsync(
        DataverseSolution solution,
        CancellationToken cancellationToken
    );

    Task<DataverseColumn> GetColumnDefinitionAsync(
        string tableLogicalName,
        string columnLogicalName,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken,
        string? baseLanguage = null
    );

    Task<Guid> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken
    );

    Task<Guid> CreateColumnAsync(
        CreateColumnRequest request,
        CancellationToken cancellationToken
    );

    Task<ColumnUpdateResult> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<DependencyInfo>> GetColumnDeleteDependenciesAsync(
        Guid columnMetadataId,
        CancellationToken cancellationToken
    );

    Task DeleteColumnAsync(
        DeleteColumnRequest request,
        CancellationToken cancellationToken
    );

    Task PublishTableAsync(
        PublishTableRequest request,
        CancellationToken cancellationToken
    );

    Task DeleteTableAsync(
        string tableLogicalName,
        CancellationToken cancellationToken
    );
}
