using dvtui.Models;
using dvtui.Services;

namespace dvtui.TerminalTests;

internal sealed class FakeDataverseServices
{
    public FakeDataverseServices(
        SolutionWriteContext context,
        Dictionary<Guid, List<DataverseColumn>> columns,
        Dictionary<string, Guid> tableIds,
        TimeSpan? operationDelay = null
    )
    {
        Query = new FakeQueryService(columns);
        Schema = new FakeSchemaService(
            context,
            columns,
            tableIds,
            operationDelay
        );
    }

    public FakeQueryService Query { get; }

    public FakeSchemaService Schema { get; }
}

internal sealed class FakeQueryService : IDataverseQueryService
{
    private readonly Dictionary<Guid, List<DataverseColumn>> _columns;

    public FakeQueryService(
        Dictionary<Guid, List<DataverseColumn>> columns
    )
    {
        _columns = columns;
    }

    public async Task<IReadOnlyList<DataverseColumn>> GetColumnsAsync(
        string tableLogicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        if( !_columns.TryGetValue(metadataId, out var columns) )
        {
            return [];
        }

        return columns
            .OrderBy(column => column.SchemaName, StringComparer.Ordinal)
            .ToList();
    }

    public Task<List<DataverseSolution>> GetSolutionsAsync(
        CancellationToken cancellationToken
    )
    {
        return Unsupported<List<DataverseSolution>>();
    }

    public Task<List<DataverseSolutionComponent>> GetSolutionComponentsAsync(
        Guid solutionId,
        CancellationToken cancellationToken
    )
    {
        return Unsupported<List<DataverseSolutionComponent>>();
    }

    public Task<DataverseEntityDetails> GetEntityAsync(
        string tableLogicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        return Unsupported<DataverseEntityDetails>();
    }

    public Task<DataverseEntityDetails> GetEntityByLogicalNameAsync(
        string tableLogicalName,
        CancellationToken cancellationToken
    )
    {
        return Unsupported<DataverseEntityDetails>();
    }

    private static Task<T> Unsupported<T>()
    {
        throw new NotSupportedException(
            "This fake only supports column-list operations."
        );
    }
}

internal sealed class FakeSchemaService : IDataverseSchemaService
{
    private readonly SolutionWriteContext _context;
    private readonly Dictionary<Guid, List<DataverseColumn>> _columns;
    private readonly Dictionary<string, Guid> _tableIds;
    private readonly TimeSpan _operationDelay;

    public FakeSchemaService(
        SolutionWriteContext context,
        Dictionary<Guid, List<DataverseColumn>> columns,
        Dictionary<string, Guid> tableIds,
        TimeSpan? operationDelay = null
    )
    {
        _context = context;
        _columns = columns;
        _tableIds = tableIds;
        _operationDelay = operationDelay ?? TimeSpan.Zero;
    }

    public Task<SolutionWriteContext> LoadWriteContextAsync(
        DataverseSolution solution,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_context);
    }

    public Task<DataverseColumn> GetColumnDefinitionAsync(
        string tableLogicalName,
        string columnLogicalName,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken,
        string? baseLanguage = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if( !_tableIds.TryGetValue(tableLogicalName, out var tableId)
            || !_columns.TryGetValue(tableId, out var columns) )
        {
            throw new InvalidOperationException(
                "The fake table was not found."
            );
        }

        var column = columns.FirstOrDefault(item =>
            string.Equals(
                item.LogicalName,
                columnLogicalName,
                StringComparison.OrdinalIgnoreCase
            )
        );
        return column == null
            ? throw new InvalidOperationException("The fake column was not found.")
            : Task.FromResult(column);
    }

    public async Task<Guid> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForOperationAsync(cancellationToken);
        return Guid.NewGuid();
    }

    public async Task<Guid> CreateColumnAsync(
        CreateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForOperationAsync(cancellationToken);
        var metadataId = Guid.NewGuid();
        var logicalName = request.Context.PublisherPrefix
            + "_"
            + request.SchemaSuffix;
        var column = new DataverseColumn
        {
            MetadataId = metadataId,
            LogicalName = logicalName,
            SchemaName = logicalName,
            DisplayName = request.DisplayName,
            Description = request.Description ?? string.Empty,
            Kind = request.Kind,
            MaxLength = request.MaxLength,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            Precision = request.Precision,
            BooleanDefaultValue = request.BooleanDefaultValue,
            BooleanTrueLabel = request.BooleanTrueLabel,
            BooleanFalseLabel = request.BooleanFalseLabel,
            IsCustom = true,
            IsManaged = false,
            IsPrimaryId = false,
            IsPrimaryName = false,
            IsCustomizable = true,
            IsRenameable = true,
            CanModifyAdditionalSettings = true,
            CanChangeRequirement = true,
            RequirementLevel = request.RequirementLevel,
            AttributeTypeCode = request.Kind.ToString(),
            AttributeFormat = request.Kind switch
            {
                ColumnKind.Text => "Text",
                ColumnKind.MultilineText => "TextArea",
                ColumnKind.WholeNumber => "None",
                _ => null
            }
        };
        if( _tableIds.TryGetValue(
            request.TableLogicalName,
            out var tableId
        )
            && _columns.TryGetValue(tableId, out var columns) )
        {
            columns.Add(column);
        }
        return metadataId;
    }

    public async Task<ColumnUpdateResult> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForOperationAsync(cancellationToken);
        if( _tableIds.TryGetValue(
            request.TableLogicalName,
            out var tableId
        )
            && _columns.TryGetValue(tableId, out var columns) )
        {
            var index = columns.FindIndex(
                column => column.MetadataId == request.ExpectedMetadataId
            );
            if( index >= 0 )
            {
                var current = columns[index];
                columns[index] = new DataverseColumn
                {
                    MetadataId = current.MetadataId,
                    LogicalName = current.LogicalName,
                    SchemaName = current.SchemaName,
                    DisplayName = request.SetDisplayName
                        ? request.DisplayName
                        : current.DisplayName,
                    Description = request.SetDescription
                        ? request.Description
                        : current.Description,
                    Kind = current.Kind,
                    MaxLength = request.NewMaxLength
                        ?? current.MaxLength,
                    MinValue = current.MinValue,
                    MaxValue = current.MaxValue,
                    Precision = current.Precision,
                    IsCustom = current.IsCustom,
                    IsManaged = current.IsManaged,
                    IsPrimaryId = current.IsPrimaryId,
                    IsPrimaryName = current.IsPrimaryName,
                    IsCustomizable = current.IsCustomizable,
                    IsRenameable = current.IsRenameable,
                    CanModifyAdditionalSettings =
                        current.CanModifyAdditionalSettings,
                    CanChangeRequirement = current.CanChangeRequirement,
                    RequirementLevel = request.SetRequirementLevel
                        ? request.RequirementLevel
                        : current.RequirementLevel,
                    AttributeTypeCode = current.AttributeTypeCode,
                    AttributeFormat = current.AttributeFormat,
                    IsLogical = current.IsLogical,
                    SourceType = current.SourceType,
                    AutoNumberFormat = current.AutoNumberFormat,
                    BooleanDefaultValue = current.BooleanDefaultValue,
                    BooleanTrueLabel = current.BooleanTrueLabel,
                    BooleanFalseLabel = current.BooleanFalseLabel
                };
                return new ColumnUpdateResult
                {
                    MetadataId = current.MetadataId,
                    Changed = true
                };
            }
        }
        throw new InvalidOperationException(
            $"Column {request.ExpectedMetadataId} not found."
        );
    }

    public Task<IReadOnlyList<DependencyInfo>> GetColumnDeleteDependenciesAsync(
        Guid columnMetadataId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DependencyInfo>>([]);
    }

    public async Task DeleteColumnAsync(
        DeleteColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForOperationAsync(cancellationToken);
        if( _tableIds.TryGetValue(
            request.TableLogicalName,
            out var tableId
        )
            && _columns.TryGetValue(tableId, out var columns) )
        {
            columns.RemoveAll(column =>
                column.MetadataId == request.ExpectedMetadataId
            );
        }
    }

    public async Task PublishTableAsync(
        PublishTableRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForOperationAsync(cancellationToken);
    }

    public Task DeleteTableAsync(
        string tableLogicalName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private async Task WaitForOperationAsync(
        CancellationToken cancellationToken
    )
    {
        if( _operationDelay > TimeSpan.Zero )
        {
            await Task.Delay(_operationDelay, cancellationToken);
            return;
        }

        await Task.Yield();
    }
}
