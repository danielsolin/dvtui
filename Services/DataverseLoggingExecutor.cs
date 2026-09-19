using System.Collections;
using System.Diagnostics;
using System.Net.Http;
using dvtui.Models;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using RetrieveDependenciesForDeleteResponse =
    Microsoft.Crm.Sdk.Messages.RetrieveDependenciesForDeleteResponse;

namespace dvtui.Services;

internal sealed class LoggingDataverseExecutor :
    IDataverseExecutor,
    IDataverseWebExecutor
{
    private readonly IDataverseExecutor _inner;

    public LoggingDataverseExecutor(IDataverseExecutor inner)
    {
        _inner = inner;
    }

    public async Task<OrganizationResponse> ExecuteAsync(
        OrganizationRequest request,
        CancellationToken cancellationToken
    )
    {
        var operationId = SessionLog.NextOperationId();
        var started = Stopwatch.StartNew();
        SessionLog.Info(
            "Dataverse.Request",
            "id=" + operationId
                + " transport=Execute"
                + " request=" + DataverseLogFormatter.Request(request)
        );
        try
        {
            var response = await _inner.ExecuteAsync(
                request,
                cancellationToken
            );
            SessionLog.Info(
                "Dataverse.Response",
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " response=" + DataverseLogFormatter.Response(response)
            );
            return response;
        }
        catch( OperationCanceledException ex )
        {
            SessionLog.Warning(
                "Dataverse.Response",
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " cancelled=" + ex.Message
            );
            throw;
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "Dataverse.Response",
                ex,
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " requestType=" + request.GetType().Name
            );
            throw;
        }
    }

    public async Task<EntityCollection> RetrieveMultipleAsync(
        QueryBase query,
        CancellationToken cancellationToken
    )
    {
        var operationId = SessionLog.NextOperationId();
        var started = Stopwatch.StartNew();
        SessionLog.Info(
            "Dataverse.Request",
            "id=" + operationId
                + " transport=RetrieveMultiple"
                + " query=" + DataverseLogFormatter.Query(query)
        );
        try
        {
            var response = await _inner.RetrieveMultipleAsync(
                query,
                cancellationToken
            );
            SessionLog.Info(
                "Dataverse.Response",
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " response=" + DataverseLogFormatter.EntityCollection(
                        response
                    )
            );
            return response;
        }
        catch( OperationCanceledException ex )
        {
            SessionLog.Warning(
                "Dataverse.Response",
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " cancelled=" + ex.Message
            );
            throw;
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "Dataverse.Response",
                ex,
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " transport=RetrieveMultiple"
            );
            throw;
        }
    }

    public async Task<HttpResponseMessage> ExecuteWebRequestAsync(
        HttpMethod method,
        string queryString,
        string body,
        Dictionary<string, List<string>> customHeaders,
        string? contentType,
        CancellationToken cancellationToken
    )
    {
        if( _inner is not IDataverseWebExecutor webExecutor )
        {
            throw new InvalidOperationException(
                "The configured Dataverse executor does not support Web API requests."
            );
        }

        var operationId = SessionLog.NextOperationId();
        var started = Stopwatch.StartNew();
        SessionLog.Info(
            "Dataverse.WebRequest",
            "id=" + operationId
                + " method=" + method.Method
                + " path=" + queryString
                + " contentType=" + (contentType ?? "default")
                + " headers=" + DataverseLogFormatter.Headers(customHeaders)
                + " body=" + DataverseLogFormatter.WebBody(body)
        );
        try
        {
            var response = await webExecutor.ExecuteWebRequestAsync(
                method,
                queryString,
                body,
                customHeaders,
                contentType,
                cancellationToken
            );
            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken
            );
            SessionLog.Info(
                "Dataverse.WebResponse",
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " status=" + (int)response.StatusCode
                    + " reason=" + response.ReasonPhrase
                    + " body=" + DataverseLogFormatter.WebBody(responseBody)
            );
            return response;
        }
        catch( OperationCanceledException ex )
        {
            SessionLog.Warning(
                "Dataverse.WebResponse",
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " cancelled=" + ex.Message
            );
            throw;
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "Dataverse.WebResponse",
                ex,
                "id=" + operationId
                    + " elapsedMs=" + started.ElapsedMilliseconds
                    + " method=" + method.Method
                    + " path=" + queryString
            );
            throw;
        }
    }
}

internal static class DataverseLogFormatter
{
    public static string Request(OrganizationRequest request)
    {
        var parameters = request.Parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + Value(pair.Value));
        return "type=" + request.GetType().Name
            + " parameters={" + string.Join(",", parameters) + "}";
    }

    public static string Response(OrganizationResponse response)
    {
        var resultNames = response.Results.Keys
            .OrderBy(key => key, StringComparer.Ordinal);
        var details = response switch
        {
            CreateEntityResponse createEntity =>
                " entityId=" + createEntity.EntityId,
            CreateAttributeResponse createAttribute =>
                " attributeId=" + createAttribute.AttributeId,
            RetrieveEntityResponse retrieveEntity =>
                " metadata=" + EntityMetadataValue(retrieveEntity.EntityMetadata),
            RetrieveAttributeResponse retrieveAttribute =>
                " metadata=" + AttributeMetadataValue(
                    retrieveAttribute.AttributeMetadata
                ),
            RetrieveAllEntitiesResponse retrieveAll =>
                " entityCount=" + retrieveAll.EntityMetadata.Count(),
            RetrieveDependenciesForDeleteResponse dependencies =>
                " dependencyCount=" + (dependencies.EntityCollection?.Entities.Count
                    ?? 0),
            _ => string.Empty
        };
        return "type=" + response.GetType().Name
            + " resultKeys=[" + string.Join(",", resultNames) + "]"
            + details;
    }

    public static string Query(QueryBase query)
    {
        if( query is not QueryExpression expression )
        {
            return "type=" + query.GetType().Name;
        }

        var columns = expression.ColumnSet.AllColumns
            ? "*"
            : string.Join(";", expression.ColumnSet.Columns);
        var conditions = expression.Criteria.Conditions
            .Select(condition =>
                condition.AttributeName
                    + " "
                    + condition.Operator
                    + " ["
                    + string.Join(";", condition.Values.Select(Value))
                    + "]"
            );
        var orders = expression.Orders.Select(
            order => order.AttributeName + " " + order.OrderType
        );
        return "type=QueryExpression"
            + " entity=" + expression.EntityName
            + " columns=[" + columns + "]"
            + " conditions=[" + string.Join(" | ", conditions) + "]"
            + " orders=[" + string.Join(" | ", orders) + "]"
            + " page=" + expression.PageInfo.PageNumber
            + " count=" + expression.PageInfo.Count
            + " top=" + (expression.TopCount?.ToString() ?? "none");
    }

    public static string EntityCollection(EntityCollection collection)
    {
        var sample = collection.Entities
            .Take(5)
            .Select(entity => entity.LogicalName + ":" + entity.Id);
        return "type=EntityCollection"
            + " count=" + collection.Entities.Count
            + " moreRecords=" + collection.MoreRecords
            + " sample=[" + string.Join(";", sample) + "]";
    }

    public static string Headers(
        Dictionary<string, List<string>> headers
    )
    {
        return "["
            + string.Join(
                ";",
                headers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Key + "=" + string.Join(",", pair.Value))
            )
            + "]";
    }

    public static string WebBody(string body)
    {
        const int maxLength = 4000;
        var safe = SessionLog.SafeSingleLine(body);
        return safe.Length <= maxLength
            ? "\"" + safe + "\""
            : "\"" + safe[..maxLength] + "...<truncated>\"";
    }

    private static string Value(object? value, int depth = 0)
    {
        if( value == null )
        {
            return "null";
        }

        if( depth > 2 )
        {
            return value.GetType().Name;
        }

        switch( value )
        {
            case string text:
                return "\"" + SessionLog.SafeSingleLine(text) + "\"";
            case Guid id:
                return id.ToString();
            case EntityReference reference:
                return "EntityReference(" + reference.LogicalName
                    + "," + reference.Id + ")";
            case OptionSetValue option:
                return "OptionSet(" + option.Value + ")";
            case Money money:
                return "Money(" + money.Value + ")";
            case Entity entity:
                return "Entity(" + entity.LogicalName
                    + "," + entity.Id
                    + ",attributes=" + entity.Attributes.Count + ")";
            case EntityMetadata metadata:
                return EntityMetadataValue(metadata);
            case AttributeMetadata attribute:
                return AttributeMetadataValue(attribute);
            case Label label:
                return "Label("
                    + SessionLog.SafeSingleLine(
                        label.UserLocalizedLabel?.Label ?? string.Empty
                    )
                    + ")";
            case ColumnSet columnSet:
                return columnSet.AllColumns
                    ? "ColumnSet(*)"
                    : "ColumnSet(" + string.Join(";", columnSet.Columns) + ")";
            case IEnumerable enumerable:
                var values = enumerable
                    .Cast<object?>()
                    .Take(10)
                    .Select(item => Value(item, depth + 1));
                return value.GetType().Name
                    + "[" + string.Join(";", values) + "]";
            default:
                return SessionLog.SafeSingleLine(value.ToString() ?? "null");
        }
    }

    private static string EntityMetadataValue(EntityMetadata metadata)
    {
        return "EntityMetadata(logical=" + metadata.LogicalName
            + ",schema=" + metadata.SchemaName
            + ",id=" + metadata.MetadataId
            + ",attributes=" + (metadata.Attributes?.Count() ?? 0)
            + ",custom=" + metadata.IsCustomEntity
            + ",managed=" + metadata.IsManaged + ")";
    }

    private static string AttributeMetadataValue(AttributeMetadata metadata)
    {
        return "AttributeMetadata(logical=" + metadata.LogicalName
            + ",schema=" + metadata.SchemaName
            + ",id=" + metadata.MetadataId
            + ",type=" + metadata.AttributeType
            + ",custom=" + metadata.IsCustomAttribute
            + ",managed=" + metadata.IsManaged
            + ",required=" + RequiredLevelValue(metadata.RequiredLevel)
            + ")";
    }

    private static string RequiredLevelValue(
        AttributeRequiredLevelManagedProperty? requiredLevel
    )
    {
        if( requiredLevel == null )
        {
            return "null";
        }

        return "value=" + requiredLevel.Value
            + ",canBeChanged=" + requiredLevel.CanBeChanged
            + ",managedName="
            + SessionLog.SafeSingleLine(
                requiredLevel.ManagedPropertyLogicalName ?? "<null>"
            )
            + ",valueModified=" + requiredLevel.IsValueModified;
    }
}
