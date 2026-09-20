using dvtui.Models;

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Auth;
using Microsoft.PowerPlatform.Dataverse.Client.Model;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Label = Microsoft.Xrm.Sdk.Label;
using EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters;
using EntityMetadata = Microsoft.Xrm.Sdk.Metadata.EntityMetadata;
using AttributeMetadata = Microsoft.Xrm.Sdk.Metadata.AttributeMetadata;
using Entity = Microsoft.Xrm.Sdk.Entity;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace dvtui.Services;

public class DataverseService : IDisposable
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

    private readonly ServiceClient _client;
    private readonly IDataverseExecutor _executor;
    private readonly string _environmentUrl;

    public DataverseService(string url)
    {
        url = url.Replace("http://", "").TrimEnd('/');
        if( url.StartsWith("https://") == false )
        {
            url = "https://" + url;
        }
        _environmentUrl = url;

        var options = new ConnectionOptions
        {
            AuthenticationType = AuthenticationType.OAuth,
            ServiceUri = new Uri(url),
            RedirectUri = new Uri("http://localhost"),
            LoginPrompt = PromptBehavior.Auto,
            SkipDiscovery = true
        };

        _client = new ServiceClient(options, deferConnection: true);
        _executor = new LoggingDataverseExecutor(
            new ServiceClientExecutor(_client)
        );
        SessionLog.Info(
            "Dataverse.Client",
            "Created deferred client for environment=" + _environmentUrl
        );
    }

    public void Connect()
    {
        SessionLog.Info(
            "Dataverse.Connect",
            "Starting connection to environment=" + _environmentUrl
        );
        try
        {
            _client.Connect();

            if( !_client.IsReady )
            {
                SessionLog.Warning(
                    "Dataverse.Connect",
                    "Client is not ready. lastError=" + _client.LastError
                        + " lastException=" + _client.LastException
                );
                throw new InvalidOperationException(
                    $"Connection failed: {_client.LastError}"
                );
            }

            SessionLog.Info(
                "Dataverse.Connect",
                "Connection ready. isReady=" + _client.IsReady
            );
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "Dataverse.Connect",
                ex,
                "Connection failed for environment=" + _environmentUrl
                    + " lastError=" + _client.LastError
                    + " lastException=" + _client.LastException
            );
            throw;
        }
    }

    public string EnvironmentUrl => _environmentUrl;

    public DataverseSchemaService CreateSchemaService()
    {
        SessionLog.Debug(
            "Dataverse.Client",
            "Creating schema service for environment=" + _environmentUrl
        );
        return new DataverseSchemaService(
            _executor,
            _environmentUrl
        );
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
        string logicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntity started logicalName=" + logicalName
                + " metadataId=" + metadataId
        );
        if( string.IsNullOrWhiteSpace(logicalName) )
        {
            throw new ArgumentException("A table logical name is required.", nameof(logicalName));
        }

        if( metadataId == Guid.Empty )
        {
            throw new ArgumentException("A table metadata ID is required.", nameof(metadataId));
        }

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            LogicalName = logicalName,
            MetadataId = metadataId,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)await _executor.ExecuteAsync(
            request,
            cancellationToken
        );
        var metadata = response.EntityMetadata;
        VerifyEntityIdentity(metadata, logicalName, metadataId);
        var result = CreateEntityDetails(metadata);
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntity completed logicalName=" + logicalName
                + " fields=" + result.Fields.Count
        );
        return result;
    }

    public virtual async Task<DataverseEntityDetails>
        GetEntityByLogicalNameAsync(
            string logicalName,
            CancellationToken cancellationToken
        )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntityByLogicalName started logicalName=" + logicalName
        );
        if( string.IsNullOrWhiteSpace(logicalName) )
        {
            throw new ArgumentException(
                "A table logical name is required.",
                nameof(logicalName)
            );
        }

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            LogicalName = logicalName,
            RetrieveAsIfPublished = true
        };
        var response = (RetrieveEntityResponse)await _executor.ExecuteAsync(
            request,
            cancellationToken
        );
        var result = CreateEntityDetails(response.EntityMetadata);
        SessionLog.Info(
            "Dataverse.Operation",
            "GetEntityByLogicalName completed logicalName=" + logicalName
                + " fields=" + result.Fields.Count
        );
        return result;
    }

    public virtual async Task<IReadOnlyList<DataverseColumn>> GetColumnsAsync(
        string logicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetColumns started logicalName=" + logicalName
                + " metadataId=" + metadataId
        );
        if( string.IsNullOrWhiteSpace(logicalName) )
        {
            throw new ArgumentException(
                "A table logical name is required.",
                nameof(logicalName)
            );
        }

        if( metadataId == Guid.Empty )
        {
            throw new ArgumentException(
                "A table metadata ID is required.",
                nameof(metadataId)
            );
        }

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            LogicalName = logicalName,
            MetadataId = metadataId,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)await _executor.ExecuteAsync(
            request,
            cancellationToken
        );
        var metadata = response.EntityMetadata;
        VerifyEntityIdentity(metadata, logicalName, metadataId);
        var columns = new List<DataverseColumn>();
        if( metadata.Attributes == null )
        {
            SessionLog.Info(
                "Dataverse.Operation",
                "GetColumns completed logicalName=" + logicalName + " count=0"
            );
            return columns;
        }

        foreach( var attribute in metadata.Attributes )
        {
            columns.Add(CreateColumn(attribute));
        }

        var result = columns
            .OrderBy(column => column.SchemaName, StringComparer.Ordinal)
            .ToList();
        SessionLog.Info(
            "Dataverse.Operation",
            "GetColumns completed logicalName=" + logicalName
                + " count=" + result.Count
        );
        return result;
    }

    public virtual Task<Guid> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "CreateTable requested solution=" + request.Context.SolutionUniqueName
                + " schemaSuffix=" + request.SchemaSuffix
        );
        return CreateSchemaService().CreateTableAsync(
            request,
            cancellationToken
        );
    }

    public virtual Task<Guid> CreateColumnAsync(
        CreateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "CreateColumn requested table=" + request.TableLogicalName
                + " schemaSuffix=" + request.SchemaSuffix
                + " kind=" + request.Kind
        );
        return CreateSchemaService().CreateColumnAsync(
            request,
            cancellationToken
        );
    }

    public virtual Task<DataverseColumn> GetColumnDefinitionAsync(
        string tableLogicalName,
        string columnLogicalName,
        bool retrieveAsIfPublished,
        string baseLanguage,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetColumnDefinition requested table=" + tableLogicalName
                + " column=" + columnLogicalName
                + " published=" + retrieveAsIfPublished
        );
        return CreateSchemaService().LoadColumnDefinitionAsync(
            tableLogicalName,
            columnLogicalName,
            retrieveAsIfPublished,
            cancellationToken,
            baseLanguage
        );
    }

    public virtual Task<ColumnUpdateResult> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "UpdateColumn requested table=" + request.TableLogicalName
                + " column=" + request.ColumnLogicalName
                + " expectedMetadataId=" + request.ExpectedMetadataId
        );
        return CreateSchemaService().UpdateColumnAsync(
            request,
            cancellationToken
        );
    }

    public Task<IReadOnlyList<DependencyInfo>> GetColumnDeleteDependenciesAsync(
        Guid columnMetadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "GetColumnDeleteDependencies requested metadataId=" + columnMetadataId
        );
        return CreateSchemaService().GetColumnDeleteDependenciesAsync(
            columnMetadataId,
            cancellationToken
        );
    }

    public Task DeleteColumnAsync(
        string tableLogicalName,
        string columnLogicalName,
        Guid expectedMetadataId,
        CancellationToken cancellationToken,
        Guid tableMetadataId = default,
        SolutionWriteContext? context = null
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "DeleteColumn requested table=" + tableLogicalName
                + " column=" + columnLogicalName
                + " expectedMetadataId=" + expectedMetadataId
        );
        return CreateSchemaService().DeleteColumnAsync(
            tableLogicalName,
            columnLogicalName,
            expectedMetadataId,
            cancellationToken,
            tableMetadataId,
            context
        );
    }

    public Task PublishTableAsync(
        string tableLogicalName,
        CancellationToken cancellationToken,
        SolutionWriteContext? context = null,
        Guid tableMetadataId = default
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "PublishTable requested table=" + tableLogicalName
                + " metadataId=" + tableMetadataId
        );
        return CreateSchemaService().PublishTableAsync(
            tableLogicalName,
            cancellationToken,
            context,
            tableMetadataId
        );
    }

    public Task DeleteTableAsync(
        string tableLogicalName,
        Guid tableMetadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Dataverse.Operation",
            "DeleteTable requested table=" + tableLogicalName
                + " metadataId=" + tableMetadataId
        );
        return CreateSchemaService().DeleteTableAsync(
            tableLogicalName,
            tableMetadataId,
            cancellationToken
        );
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
                // Fallback: try to retrieve the entity directly by MetadataId
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
            DisplayName = GetLabel(metadata.DisplayName),
            CollectionName = GetLabel(metadata.DisplayCollectionName),
            Description = GetLabel(metadata.Description),
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
        string logicalName,
        Guid metadataId
    )
    {
        if( metadata.MetadataId != metadataId
            || !string.Equals(
                metadata.LogicalName,
                logicalName,
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
                DisplayName = GetLabel(attribute.DisplayName),
                Type = attribute.AttributeType?.ToString() ?? string.Empty,
                Description = GetLabel(attribute.Description)
            });
        }

        return fields
            .OrderBy(field => field.SchemaName, StringComparer.Ordinal)
            .ToList();
    }

    private static string GetLabel(Label? label)
    {
        return label?.UserLocalizedLabel?.Label
            ?? label?.LocalizedLabels.FirstOrDefault()?.Label
            ?? string.Empty;
    }

    private static DataverseColumn CreateColumn(AttributeMetadata metadata)
    {
        return DataverseSchemaService.CreateColumn(metadata);
    }

    public void Dispose()
    {
        SessionLog.Info(
            "Dataverse.Client",
            "Disposing client for environment=" + _environmentUrl
        );
        _client.Dispose();
    }
}
