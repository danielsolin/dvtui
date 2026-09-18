using dvtui.Models;

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Auth;
using Microsoft.PowerPlatform.Dataverse.Client.Model;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Label = Microsoft.Xrm.Sdk.Label;
using OptionSetValue = Microsoft.Xrm.Sdk.OptionSetValue;
using EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters;
using EntityMetadata = Microsoft.Xrm.Sdk.Metadata.EntityMetadata;
using Entity = Microsoft.Xrm.Sdk.Entity;

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
        "description"
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

    public DataverseService(string url)
    {
        url = url.Replace("http://", "").TrimEnd('/');
        if( url.StartsWith("https://") == false )
        {
            url = "https://" + url;
        }

        var options = new ConnectionOptions
        {
            AuthenticationType = AuthenticationType.OAuth,
            ServiceUri = new Uri(url),
            RedirectUri = new Uri("http://localhost"),
            LoginPrompt = PromptBehavior.Auto,
            SkipDiscovery = true
        };

        _client = new ServiceClient(options, deferConnection: true);
    }

    public void Connect()
    {
        _client.Connect();

        if( !_client.IsReady )
        {
            throw new InvalidOperationException($"Connection failed: {_client.LastError}");
        }
    }

    public async Task<List<DataverseSolution>> GetSolutionsAsync(
        CancellationToken cancellationToken
    )
    {
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

        return solutions;
    }

    public async Task<List<DataverseSolutionComponent>> GetSolutionComponentsAsync(
        Guid solutionId,
        CancellationToken cancellationToken
    )
    {
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
        return components;
    }

    public async Task<DataverseEntityDetails> GetEntityAsync(
        string logicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
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

        var response = (RetrieveEntityResponse)await _client.ExecuteAsync(
            request,
            cancellationToken
        );
        var metadata = response.EntityMetadata;
        return new DataverseEntityDetails
        {
            Entity = CreateEntity(metadata),
            Fields = CreateFields(metadata)
        };
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
            var page = await _client.RetrieveMultipleAsync(
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
            Description = entity.GetAttributeValue<string>("description") ?? string.Empty
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
            ComponentType = GetOptionValue(entity, "componenttype") ?? 0,
            RootComponentId = entity.Contains("rootsolutioncomponentid")
                ? entity.GetAttributeValue<Guid>("rootsolutioncomponentid")
                : null,
            RootComponentBehavior = GetOptionValue(entity, "rootcomponentbehavior")
        };
    }

    private static int? GetOptionValue(Entity entity, string attribute)
    {
        if( !entity.Contains(attribute) )
        {
            return null;
        }

        var value = entity.GetAttributeValue<OptionSetValue>(attribute);
        return value?.Value;
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

        var response = (RetrieveAllEntitiesResponse)await _client.ExecuteAsync(
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

            var response = (RetrieveEntityResponse)await _client.ExecuteAsync(
                request,
                cancellationToken
            );
            return CreateEntity(response.EntityMetadata);
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
            IsManaged = metadata.IsManaged,
            IsActivity = metadata.IsActivity
        };
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

    public void Dispose()
    {
        _client.Dispose();
    }
}
