using System.Net.Http;
using System.Text.Json.Nodes;
using dvtui.Models;

namespace dvtui.Services;

internal sealed class WebApiClient
{
    private readonly IDataverseExecutor _client;

    public WebApiClient(IDataverseExecutor client)
    {
        _client = client;
    }

    public async Task UpdateRequirementLevelAsync(
        UpdateColumnRequest request,
        string requirement,
        SolutionWriteContext context,
        CancellationToken cancellationToken
    )
    {
        if( request.TableMetadataId == Guid.Empty )
        {
            SessionLog.Debug(
                "Schema.UpdateColumn",
                "Web API requirement fallback skipped without table metadata ID"
            );
            return;
        }

        if( _client is not IDataverseWebExecutor webExecutor )
        {
            SessionLog.Debug(
                "Schema.UpdateColumn",
                "Web API requirement fallback unavailable for test executor"
            );
            return;
        }

        var retrievePath = BuildRetrieveEntityPath(
            request.TableLogicalName,
            request.TableMetadataId
        );
        using var retrieveResponse =
            await webExecutor.ExecuteWebRequestAsync(
                HttpMethod.Get,
                retrievePath,
                string.Empty,
                CreateWebApiHeaders(),
                "application/json",
                cancellationToken
            );
        var retrieveBody = await retrieveResponse.Content.ReadAsStringAsync(
            cancellationToken
        );
        EnsureWebApiSuccess(retrieveResponse, retrieveBody, "metadata read");

        var document = JsonNode.Parse(retrieveBody) as JsonObject
            ?? throw new InvalidOperationException(
                "The metadata read returned invalid JSON."
            );
        var entity = GetObject(document, "EntityMetadata")
            ?? throw new InvalidOperationException(
                "The metadata read did not return EntityMetadata."
            );
        var entityId = GetGuid(entity, "MetadataId");
        if( entityId != request.TableMetadataId )
        {
            throw new ColumnConflictException(
                "The selected table identity changed; reload before writing."
            );
        }

        var attribute = FindAttribute(
            entity,
            request.ColumnLogicalName,
            request.ExpectedMetadataId
        );
        if( attribute == null )
        {
            throw new ColumnConflictException(
                "The selected column was not returned by the metadata read."
            );
        }

        var currentRequirement = GetObject(attribute, "RequiredLevel")
            ?? new JsonObject();
        var previousValue = GetString(currentRequirement, "Value") ?? "<missing>";
        currentRequirement["Value"] = ToWebApiRequirementLevel(requirement);
        if( GetNode(currentRequirement, "CanBeChanged") == null )
        {
            currentRequirement["CanBeChanged"] = true;
        }

        if( GetNode(currentRequirement, "ManagedPropertyLogicalName") == null )
        {
            currentRequirement["ManagedPropertyLogicalName"] =
                "canmodifyrequirementlevelsettings";
        }

        attribute["RequiredLevel"] = currentRequirement;
        if( GetNode(attribute, "@odata.type") == null )
        {
            throw new InvalidOperationException(
                "The metadata read did not return a type-specific column definition."
            );
        }
        NormalizeWebApiAttributeType(attribute);

        SessionLog.Debug(
            "Schema.UpdateColumn",
            "Web API requirement update table=" + request.TableLogicalName
                + " column=" + request.ColumnLogicalName
                + " previous=" + previousValue
                + " requested=" + requirement
        );

        var updatePath = BuildAttributePath(
            request.TableLogicalName,
            request.ColumnLogicalName
        );
        var updateBody = attribute.ToJsonString();
        using var updateResponse =
            await webExecutor.ExecuteWebRequestAsync(
                HttpMethod.Put,
                updatePath,
                updateBody,
                CreateWebApiHeaders(context.SolutionUniqueName),
                "application/json",
                cancellationToken
            );
        var responseBody = await updateResponse.Content.ReadAsStringAsync(
            cancellationToken
        );
        EnsureWebApiSuccess(updateResponse, responseBody, "metadata update");
        SessionLog.Info(
            "Schema.UpdateColumn",
            "Web API requirement update completed table="
                + request.TableLogicalName
                + " column=" + request.ColumnLogicalName
        );
    }

    private static string BuildRetrieveEntityPath(
        string tableLogicalName,
        Guid tableMetadataId
    )
    {
        var filter = Uri.EscapeDataString(
            "Microsoft.Dynamics.CRM.EntityFilters'Attributes'"
        );
        var logicalName = Uri.EscapeDataString(
            "'" + EscapeODataString(tableLogicalName) + "'"
        );
        var metadataId = Uri.EscapeDataString(
            tableMetadataId.ToString()
        );
        return "RetrieveEntity(EntityFilters=@filters,LogicalName=@logicalName,"
            + "MetadataId=@metadataId,RetrieveAsIfPublished=@published)"
            + "?@filters=" + filter
            + "&@logicalName=" + logicalName
            + "&@metadataId=" + metadataId
            + "&@published=true";
    }

    private static string BuildAttributePath(
        string tableLogicalName,
        string columnLogicalName
    )
    {
        return "EntityDefinitions(LogicalName='"
            + EscapeODataString(tableLogicalName)
            + "')/Attributes(LogicalName='"
            + EscapeODataString(columnLogicalName)
            + "')";
    }

    private static Dictionary<string, List<string>> CreateWebApiHeaders(
        string? solutionUniqueName = null
    )
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["Accept"] = ["application/json"],
            ["OData-MaxVersion"] = ["4.0"],
            ["OData-Version"] = ["4.0"],
            ["If-None-Match"] = ["null"]
        };
        if( !string.IsNullOrWhiteSpace(solutionUniqueName) )
        {
            headers["MSCRM.SolutionUniqueName"] = [solutionUniqueName];
            headers["MSCRM.MergeLabels"] = ["true"];
        }

        return headers;
    }

    private static void EnsureWebApiSuccess(
        HttpResponseMessage response,
        string body,
        string operation
    )
    {
        if( response.IsSuccessStatusCode )
        {
            return;
        }

        throw new InvalidOperationException(
            "Dataverse Web API " + operation + " failed with HTTP "
                + (int)response.StatusCode + " " + response.ReasonPhrase
                + ": " + SessionLog.SafeSingleLine(body)
        );
    }

    private static JsonObject? FindAttribute(
        JsonObject entity,
        string logicalName,
        Guid metadataId
    )
    {
        var attributes = GetNode(entity, "Attributes") as JsonArray;
        if( attributes == null )
        {
            return null;
        }

        foreach( var item in attributes )
        {
            if( item is not JsonObject attribute )
            {
                continue;
            }

            var candidateId = GetGuid(attribute, "MetadataId");
            var candidateName = GetString(attribute, "LogicalName");
            if( candidateId == metadataId
                || string.Equals(
                    candidateName,
                    logicalName,
                    StringComparison.OrdinalIgnoreCase
                ) )
            {
                return attribute;
            }
        }

        return null;
    }

    private static JsonObject? GetObject(JsonObject parent, string name)
    {
        return GetNode(parent, name) as JsonObject;
    }

    private static JsonNode? GetNode(JsonObject parent, string name)
    {
        foreach( var pair in parent )
        {
            if( string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) )
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string? GetString(JsonObject parent, string name)
    {
        return GetNode(parent, name)?.GetValue<string>();
    }

    private static Guid GetGuid(JsonObject parent, string name)
    {
        var value = GetString(parent, name);
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    private static string ToWebApiRequirementLevel(string requirement)
    {
        return requirement switch
        {
            RequirementLevels.Recommended => "Recommended",
            RequirementLevels.Required => "ApplicationRequired",
            _ => "None"
        };
    }

    private static void NormalizeWebApiAttributeType(JsonObject attribute)
    {
        var type = GetString(attribute, "@odata.type");
        if( string.IsNullOrWhiteSpace(type) )
        {
            return;
        }

        const string prefix = "Microsoft.Dynamics.CRM.";
        var shortName = type.StartsWith("#" + prefix, StringComparison.Ordinal)
            ? type[(prefix.Length + 1)..]
            : type.StartsWith(prefix, StringComparison.Ordinal)
                ? type[prefix.Length..]
                : type;
        if( shortName.StartsWith("Complex", StringComparison.Ordinal) )
        {
            shortName = shortName["Complex".Length..];
        }

        attribute["@odata.type"] = prefix + shortName;
    }

    private static string EscapeODataString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
