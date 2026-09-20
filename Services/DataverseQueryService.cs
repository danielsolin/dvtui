using dvtui.Models;

using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters;
using EntityMetadata = Microsoft.Xrm.Sdk.Metadata.EntityMetadata;
using Entity = Microsoft.Xrm.Sdk.Entity;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace dvtui.Services;

public sealed class DataverseQueryService : IDataverseQueryService
{
    private const int RecordPageSize = 5000;

    private static readonly string[] SolutionColumns =
    [
        "solutionid",
        "friendlyname",
        "uniquename",
        "version",
        "ismanaged",
        "description",
        "publisherid"
    ];

    private static readonly string[] ComponentColumns =
    [
        "solutioncomponentid",
        "solutionid",
        "componenttype",
        "objectid",
        "rootsolutioncomponentid",
        "rootcomponentbehavior"
    ];

    private readonly IDataverseExecutor _executor;

    public DataverseQueryService(IDataverseExecutor executor)
    {
        _executor = executor;
    }

    public async Task<List<DataverseSolution>> GetSolutionsAsync(
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info("Dataverse.Operation", "GetSolutions started");
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet(SolutionColumns),
            PageInfo = new PagingInfo
            {
                PageNumber = 1,
                Count = RecordPageSize
            }
        };
        query.Criteria.AddCondition(
            "isvisible",
            ConditionOperator.Equal,
            true
        );
        query.Orders.Add(new OrderExpression("friendlyname", OrderType.Ascending));
        query.Orders.Add(new OrderExpression("uniquename", OrderType.Ascending));
        query.Orders.Add(new OrderExpression("solutionid", OrderType.Ascending));

        var solutions = new List<DataverseSolution>();
        foreach( var entity in await RetrieveAllAsync(query, cancellationToken) )
        {
            solutions.Add(CreateSolution(entity));
        }

        SessionLog.Info(
            "Dataverse.Operation",
            "GetSolutions completed count=" + solutions.Count
        );
        return solutions;
    }

    public async Task<List<DataverseSolutionComponent>> GetSolutionComponentsAsync(
        Guid solutionId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetSolutionComponents started solutionId=" + solutionId
        );
        if( solutionId == Guid.Empty )
        {
            throw new ArgumentException("A solution ID is required.", nameof(solutionId));
        }

        var query = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet(ComponentColumns),
            PageInfo = new PagingInfo
            {
                PageNumber = 1,
                Count = RecordPageSize
            }
        };
        query.Criteria.AddCondition(
            "solutionid",
            ConditionOperator.Equal,
            solutionId
        );
        query.Orders.Add(new OrderExpression("componenttype", OrderType.Ascending));
        query.Orders.Add(new OrderExpression("objectid", OrderType.Ascending));
        query.Orders.Add(new OrderExpression("solutioncomponentid", OrderType.Ascending));

        var rows = await RetrieveAllAsync(query, cancellationToken);
        var components = rows.Select(CreateComponent).ToList();
        await EnrichTableComponentsAsync(components, cancellationToken);
        SessionLog.Info(
            "Dataverse.Operation",
            "GetSolutionComponents completed solutionId=" + solutionId
                + " count=" + components.Count
        );
        return components;
    }

    public async Task<DataverseEntityDetails> GetEntityAsync(
        string tableLogicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntity started logicalName=" + tableLogicalName
                + " metadataId=" + metadataId
        );
        ValidateEntityIdentityInput(tableLogicalName, metadataId);

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            LogicalName = tableLogicalName,
            MetadataId = metadataId,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)await _executor.ExecuteAsync(
            request,
            cancellationToken
        );
        var metadata = response.EntityMetadata;
        VerifyEntityIdentity(metadata, tableLogicalName, metadataId);
        var result = CreateEntityDetails(metadata);
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntity completed logicalName=" + tableLogicalName
                + " fields=" + result.Fields.Count
        );
        return result;
    }

    public async Task<DataverseEntityDetails> GetEntityByLogicalNameAsync(
        string tableLogicalName,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntityByLogicalName started logicalName=" + tableLogicalName
        );
        if( string.IsNullOrWhiteSpace(tableLogicalName) )
        {
            throw new ArgumentException(
                "A table logical name is required.",
                nameof(tableLogicalName)
            );
        }

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            LogicalName = tableLogicalName,
            RetrieveAsIfPublished = true
        };
        var response = (RetrieveEntityResponse)await _executor.ExecuteAsync(
            request,
            cancellationToken
        );
        var result = CreateEntityDetails(response.EntityMetadata);
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntityByLogicalName completed logicalName=" + tableLogicalName
                + " fields=" + result.Fields.Count
        );
        return result;
    }

    public async Task<IReadOnlyList<DataverseColumn>> GetColumnsAsync(
        string tableLogicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetColumns started logicalName=" + tableLogicalName
                + " metadataId=" + metadataId
        );
        ValidateEntityIdentityInput(tableLogicalName, metadataId);

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            LogicalName = tableLogicalName,
            MetadataId = metadataId,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)await _executor.ExecuteAsync(
            request,
            cancellationToken
        );
        var metadata = response.EntityMetadata;
        VerifyEntityIdentity(metadata, tableLogicalName, metadataId);
        var columns = new List<DataverseColumn>();
        if( metadata.Attributes == null )
        {
            SessionLog.Info(
                "Dataverse.Operation",
                "GetColumns completed logicalName=" + tableLogicalName
                    + " count=0"
            );
            return columns;
        }

        foreach( var attribute in metadata.Attributes )
        {
            columns.Add(SchemaMetadataFactory.CreateColumn(attribute));
        }

        var result = columns
            .OrderBy(column => column.SchemaName, StringComparer.Ordinal)
            .ToList();
        SessionLog.Info(
            "Dataverse.Operation",
            "GetColumns completed logicalName=" + tableLogicalName
                + " count=" + result.Count
        );
        return result;
    }

    private async Task<List<Entity>> RetrieveAllAsync(
        QueryExpression query,
        CancellationToken cancellationToken
    )
    {
        var results = new List<Entity>();
        while( true )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _executor.RetrieveMultipleAsync(
                query,
                cancellationToken
            );
            results.AddRange(page.Entities);
            if( !page.MoreRecords )
            {
                break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }

        return results;
    }

    private static void ValidateEntityIdentityInput(
        string tableLogicalName,
        Guid metadataId
    )
    {
        if( string.IsNullOrWhiteSpace(tableLogicalName) )
        {
            throw new ArgumentException(
                "A table logical name is required.",
                nameof(tableLogicalName)
            );
        }

        if( metadataId == Guid.Empty )
        {
            throw new ArgumentException(
                "A table metadata ID is required.",
                nameof(metadataId)
            );
        }
    }

    private static DataverseSolution CreateSolution(Entity entity)
    {
        return new DataverseSolution
        {
            Id = entity.GetAttributeValue<Guid>("solutionid"),
            FriendlyName = entity.GetAttributeValue<string>("friendlyname") ?? string.Empty,
            UniqueName = entity.GetAttributeValue<string>("uniquename") ?? string.Empty,
            Version = entity.GetAttributeValue<string>("version") ?? string.Empty,
            IsManaged = entity.Contains("ismanaged")
                ? entity.GetAttributeValue<bool>("ismanaged")
                : null,
            Description = entity.GetAttributeValue<string>("description") ?? string.Empty,
            PublisherId = entity.GetAttributeValue<EntityReference>(
                "publisherid")?.Id ?? Guid.Empty
        };
    }

    private static DataverseSolutionComponent CreateComponent(Entity entity)
    {
        return new DataverseSolutionComponent
        {
            Id = entity.GetAttributeValue<Guid>("solutioncomponentid"),
            ObjectId = entity.Contains("objectid")
                ? entity.GetAttributeValue<Guid>("objectid")
                : null,
            ComponentType = entity.GetOptionValue("componenttype") ?? 0,
            RootComponentId = entity.Contains("rootsolutioncomponentid")
                ? entity.GetAttributeValue<Guid>("rootsolutioncomponentid")
                : null,
            RootComponentBehavior = entity.GetOptionValue(
                "rootcomponentbehavior"
            )
        };
    }

    private async Task EnrichTableComponentsAsync(
        List<DataverseSolutionComponent> components,
        CancellationToken cancellationToken
    )
    {
        var tableComponents = components
            .Where(
                component =>
                    component.ComponentType == SolutionComponentTypes.Entity
                    && component.ObjectId != null
            )
            .ToList();
        if( tableComponents.Count == 0 )
        {
            return;
        }

        var index = await GetTableIndexAsync(cancellationToken);
        foreach( var component in tableComponents )
        {
            var objectId = component.ObjectId!.Value;
            if( index.TryGetValue(objectId, out var entity) )
            {
                component.Entity = entity;
            }
            else
            {
                component.Entity = await TryRetrieveEntityByMetadataIdAsync(
                    objectId,
                    cancellationToken
                );
                if( component.Entity == null )
                {
                    component.ResolutionError =
                        "Table metadata could not be resolved.";
                }
            }
        }
    }

    private async Task<Dictionary<Guid, DataverseEntity>> GetTableIndexAsync(
        CancellationToken cancellationToken
    )
    {
        var request = new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveAllEntitiesResponse)await _executor.ExecuteAsync(
            request,
            cancellationToken
        );
        var index = new Dictionary<Guid, DataverseEntity>();
        foreach( var metadata in response.EntityMetadata )
        {
            if( metadata.MetadataId.HasValue )
            {
                index[metadata.MetadataId.Value] = CreateEntity(metadata);
            }
        }

        return index;
    }

    private async Task<DataverseEntity?> TryRetrieveEntityByMetadataIdAsync(
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.Entity,
                MetadataId = metadataId,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveEntityResponse)await _executor.ExecuteAsync(
                request,
                cancellationToken
            );
            if( response.EntityMetadata.MetadataId != metadataId )
            {
                return null;
            }

            return CreateEntity(response.EntityMetadata);
        }
        catch( OperationCanceledException ) when( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static DataverseEntity CreateEntity(EntityMetadata metadata)
    {
        return new DataverseEntity
        {
            MetadataId = metadata.MetadataId ?? Guid.Empty,
            LogicalName = metadata.LogicalName ?? string.Empty,
            SchemaName = metadata.SchemaName ?? string.Empty,
            DisplayName = SchemaMetadataFactory.GetLabel(metadata.DisplayName),
            CollectionName = SchemaMetadataFactory.GetLabel(
                metadata.DisplayCollectionName
            ),
            Description = SchemaMetadataFactory.GetLabel(metadata.Description),
            EntitySetName = metadata.EntitySetName ?? string.Empty,
            PrimaryIdAttribute = metadata.PrimaryIdAttribute ?? string.Empty,
            PrimaryNameAttribute = metadata.PrimaryNameAttribute ?? string.Empty,
            OwnershipType = metadata.OwnershipType?.ToString(),
            ObjectTypeCode = metadata.ObjectTypeCode,
            IsCustom = metadata.IsCustomEntity,
            IsCustomizable = metadata.IsCustomizable?.Value,
            CanCreateAttributes = metadata.CanCreateAttributes?.Value,
            IsManaged = metadata.IsManaged,
            IsActivity = metadata.IsActivity
        };
    }

    private static DataverseEntityDetails CreateEntityDetails(
        EntityMetadata metadata
    )
    {
        return new DataverseEntityDetails
        {
            Entity = CreateEntity(metadata),
            Fields = CreateFields(metadata)
        };
    }

    private static void VerifyEntityIdentity(
        EntityMetadata metadata,
        string tableLogicalName,
        Guid metadataId
    )
    {
        if( metadata.MetadataId != metadataId
            || !string.Equals(
                metadata.LogicalName,
                tableLogicalName,
                StringComparison.OrdinalIgnoreCase
            ) )
        {
            throw new ColumnConflictException(
                "The retrieved table does not match the selected table."
            );
        }
    }

    private static IReadOnlyList<DataverseField> CreateFields(
        EntityMetadata metadata
    )
    {
        var fields = new List<DataverseField>();
        if( metadata.Attributes == null )
        {
            return fields;
        }

        foreach( var attribute in metadata.Attributes )
        {
            fields.Add(new DataverseField
            {
                LogicalName = attribute.LogicalName ?? string.Empty,
                SchemaName = attribute.SchemaName ?? string.Empty,
                DisplayName = SchemaMetadataFactory.GetLabel(attribute.DisplayName),
                Type = attribute.AttributeType?.ToString() ?? string.Empty,
                Description = SchemaMetadataFactory.GetLabel(attribute.Description)
            });
        }

        return fields
            .OrderBy(field => field.SchemaName, StringComparer.Ordinal)
            .ToList();
    }
}
