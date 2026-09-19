using dvtui.Models;
using dvtui.Services;

namespace dvtui.TerminalTests;

internal sealed class FakeDataverseService : DataverseService
{
    private readonly Dictionary<Guid, List<DataverseColumn>> _columns;
    private readonly Dictionary<string, Guid> _tableIds;

    public FakeDataverseService(
        string url,
        Dictionary<Guid, List<DataverseColumn>> columns,
        Dictionary<string, Guid> tableIds
    )
        : base(url)
    {
        _columns = columns;
        _tableIds = tableIds;
    }

    public override async Task<IReadOnlyList<DataverseColumn>> GetColumnsAsync(
        string logicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        if( _columns.TryGetValue(metadataId, out var columns) == false )
        {
            return [];
        }

        return columns
            .OrderBy(column => column.SchemaName, StringComparer.Ordinal)
            .ToList();
    }

    public override async Task<Guid> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        return Guid.NewGuid();
    }

    public override async Task<Guid> CreateColumnAsync(
        CreateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var metadataId = Guid.NewGuid();
        var column = new DataverseColumn
        {
            MetadataId = metadataId,
            LogicalName = request.SchemaSuffix,
            SchemaName = request.SchemaSuffix,
            DisplayName = request.DisplayName,
            Description = request.Description ?? string.Empty,
            Kind = request.Kind,
            MaxLength = request.MaxLength,
            MinValue = request.MinValue.HasValue
                ? (int)request.MinValue.Value
                : null,
            MaxValue = request.MaxValue.HasValue
                ? (int)request.MaxValue.Value
                : null,
            Precision = request.Precision,
            IsCustom = true,
            IsManaged = false,
            IsPrimaryId = false,
            IsPrimaryName = false,
            IsCustomizable = true,
            IsRenameable = true,
            CanModifyAdditionalSettings = true,
            RequirementLevel = request.RequirementLevel,
            AttributeTypeCode = request.Kind.ToString()
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

    public override async Task<ColumnUpdateResult> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
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
                    RequirementLevel = request.SetRequirementLevel
                        ? request.RequirementLevel
                        : current.RequirementLevel,
                    AttributeTypeCode = current.AttributeTypeCode,
                    IsLogical = current.IsLogical,
                    SourceType = current.SourceType,
                    AutoNumberFormat = current.AutoNumberFormat
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
}
