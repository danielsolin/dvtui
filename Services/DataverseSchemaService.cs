using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using dvtui.Models;

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Label = Microsoft.Xrm.Sdk.Label;
using UserLocalizedLabel = Microsoft.Xrm.Sdk.LocalizedLabel;
using RetrieveDependenciesForDeleteRequest =
    Microsoft.Crm.Sdk.Messages.RetrieveDependenciesForDeleteRequest;
using RetrieveDependenciesForDeleteResponse =
    Microsoft.Crm.Sdk.Messages.RetrieveDependenciesForDeleteResponse;
using PublishXmlRequest = Microsoft.Crm.Sdk.Messages.PublishXmlRequest;
using EntityMetadata = Microsoft.Xrm.Sdk.Metadata.EntityMetadata;
using AttributeMetadata = Microsoft.Xrm.Sdk.Metadata.AttributeMetadata;

namespace dvtui.Services;

public sealed class DataverseSchemaService
{
    private const int ComponentTypeAttribute = 2;
    private const int MaxSchemaNameLength = 80;
    private const int MaxDisplayLength = 125;
    private const int MaxDescriptionLength = 4000;
    private const int MinTextLength = 1;
    private const int MaxTextLength = ColumnDefaults.TextMaxLength;
    private const int MinMemoLength = 1;
    private const int MaxMemoLength = ColumnDefaults.MultilineMaxLength;
    private const int MinPrecision = 0;
    private const int MaxPrecision = DecimalAttributeMetadata.MaxSupportedPrecision;
    private const int IntMin = ColumnDefaults.WholeNumberLowerBound;
    private const int IntMax = ColumnDefaults.WholeNumberUpperBound;
    private const decimal DecMin = ColumnDefaults.DecimalLowerBound;
    private const decimal DecMax = ColumnDefaults.DecimalUpperBound;

    private readonly IDataverseExecutor _client;
    private readonly string _environmentUrl;

    public DataverseSchemaService(
        IDataverseExecutor client,
        string environmentUrl = ""
    )
    {
        _client = client;
        _environmentUrl = environmentUrl;
    }

    public async Task<SolutionWriteContext> LoadWriteContextAsync(
        DataverseSolution solution,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Schema.LoadWriteContext",
            "Started solution=" + solution.UniqueName
                + " id=" + solution.Id
                + " managed=" + solution.IsManaged
        );
        if( solution.IsManaged == true )
        {
            SessionLog.Warning(
                "Schema.LoadWriteContext",
                "Writes disabled because solution is managed"
            );
            return CreateReadOnlyContext(
                solution,
                ColumnCapabilityPolicy.ReasonManagedSolution
            );
        }

        if( solution.IsManaged != false )
        {
            SessionLog.Warning(
                "Schema.LoadWriteContext",
                "Writes disabled because solution management state is unknown"
            );
            return CreateReadOnlyContext(
                solution,
                "Solution management state is unknown; writes are disabled."
            );
        }

        var publisherQuery = new QueryExpression("publisher")
        {
            ColumnSet = new ColumnSet("publisherid", "customizationprefix")
        };
        publisherQuery.Criteria.AddCondition(
            "publisherid",
            ConditionOperator.Equal,
            solution.PublisherId
        );
        var publisherResponse = await _client.RetrieveMultipleAsync(
            publisherQuery,
            cancellationToken
        );
        if( publisherResponse.Entities.Count == 0 )
        {
            throw new InvalidOperationException(
                "Could not find the publisher for this solution."
            );
        }

        var publisher = publisherResponse.Entities[0];
        var publisherId = publisher.GetAttributeValue<Guid>("publisherid");
        var prefix = publisher.GetAttributeValue<string>(
            "customizationprefix") ?? string.Empty;
        if( publisherId == Guid.Empty || string.IsNullOrWhiteSpace(prefix) )
        {
            throw new InvalidOperationException(
                "The selected solution publisher context is incomplete."
            );
        }

        var orgQuery = new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("languagecode")
        };
        var orgResponse = await _client.RetrieveMultipleAsync(
            orgQuery,
            cancellationToken
        );
        if( orgResponse.Entities.Count == 0 )
        {
            throw new InvalidOperationException(
                "Could not find the organization."
            );
        }

        var org = orgResponse.Entities[0];
        var languageCode = org.GetAttributeValue<int>("languagecode");
        if( languageCode <= 0 )
        {
            throw new InvalidOperationException(
                "The organization base language is unavailable."
            );
        }

        var baseLanguage = languageCode
            .ToString(CultureInfo.InvariantCulture);

        var context = new SolutionWriteContext
        {
            SolutionId = solution.Id,
            SolutionUniqueName = solution.UniqueName,
            IsManaged = solution.IsManaged ?? false,
            CanWrite = true,
            PublisherId = publisherId,
            PublisherPrefix = prefix,
            BaseLanguage = baseLanguage,
            EnvironmentUrl = _environmentUrl
        };
        SessionLog.Info(
            "Schema.LoadWriteContext",
            "Completed solution=" + solution.UniqueName
                + " publisherId=" + context.PublisherId
                + " prefix=" + context.PublisherPrefix
                + " baseLanguage=" + context.BaseLanguage
        );
        return context;
    }

    private SolutionWriteContext CreateReadOnlyContext(
        DataverseSolution solution,
        string reason
    )
    {
        return new SolutionWriteContext
        {
            SolutionId = solution.Id,
            SolutionUniqueName = solution.UniqueName,
            IsManaged = solution.IsManaged == true,
            CanWrite = false,
            WriteDisabledReason = reason,
            EnvironmentUrl = _environmentUrl
        };
    }

    public async Task<DataverseColumn> LoadColumnDefinitionAsync(
        string tableLogicalName,
        string columnLogicalName,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken,
        string? baseLanguage = null
    )
    {
        SessionLog.Info(
            "Schema.LoadColumnDefinition",
            "Started table=" + tableLogicalName
                + " column=" + columnLogicalName
                + " published=" + retrieveAsIfPublished
        );
        var metadata = await LoadAttributeMetadataAsync(
            tableLogicalName,
            columnLogicalName,
            retrieveAsIfPublished,
            cancellationToken
        );
        var result = CreateColumn(metadata, ParseBaseLanguage(baseLanguage));
        SessionLog.Info(
            "Schema.LoadColumnDefinition",
            "Completed table=" + tableLogicalName
                + " column=" + columnLogicalName
                + " metadataId=" + result.MetadataId
        );
        return result;
    }

    private async Task<AttributeMetadata> LoadAttributeMetadataAsync(
        string tableLogicalName,
        string columnLogicalName,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(tableLogicalName) )
        {
            throw new ArgumentException(
                "A table logical name is required.",
                nameof(tableLogicalName)
            );
        }

        if( string.IsNullOrWhiteSpace(columnLogicalName) )
        {
            throw new ArgumentException(
                "A column logical name is required.",
                nameof(columnLogicalName)
            );
        }

        var request = new RetrieveAttributeRequest
        {
            EntityLogicalName = tableLogicalName,
            LogicalName = columnLogicalName,
            RetrieveAsIfPublished = retrieveAsIfPublished
        };

        var response = (RetrieveAttributeResponse)await _client.ExecuteAsync(
            request,
            cancellationToken
        );
        return response.AttributeMetadata;
    }

    private async Task<EntityMetadata> LoadEntityMetadataAsync(
        string tableLogicalName,
        Guid tableMetadataId,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(tableLogicalName) )
        {
            throw new ArgumentException(
                "A table logical name is required.",
                nameof(tableLogicalName)
            );
        }

        if( tableMetadataId == Guid.Empty )
        {
            throw new ArgumentException(
                "A table metadata ID is required.",
                nameof(tableMetadataId)
            );
        }

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity,
            LogicalName = tableLogicalName,
            MetadataId = tableMetadataId,
            RetrieveAsIfPublished = retrieveAsIfPublished
        };
        var response = (RetrieveEntityResponse)await _client.ExecuteAsync(
            request,
            cancellationToken
        );
        var metadata = response.EntityMetadata;
        if( metadata.MetadataId != tableMetadataId
            || !string.Equals(
                metadata.LogicalName,
                tableLogicalName,
                StringComparison.OrdinalIgnoreCase
            ) )
        {
            throw new ColumnConflictException(
                "The selected table identity changed; reload before writing."
            );
        }

        return metadata;
    }

    public async Task<Guid> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Schema.CreateTable",
            "Started solution=" + request.Context.SolutionUniqueName
                + " schemaSuffix=" + request.SchemaSuffix
        );
        RequireWriteContext(request.Context);
        var context = request.Context;
        EnsureWriteAllowed(context);
        await VerifySolutionWriteScopeAsync(context, cancellationToken);
        RequirePublisherContext(context);

        ValidateTableCreation(request);
        var language = int.Parse(context.BaseLanguage, CultureInfo.InvariantCulture);
        var logicalName = BuildSchemaName(
            context.PublisherPrefix,
            request.SchemaSuffix
        );
        var primaryName = BuildSchemaName(
            context.PublisherPrefix,
            request.PrimaryNameSchemaSuffix
        );
        await EnsureTableNameAvailableAsync(logicalName, cancellationToken);
        SessionLog.Debug(
            "Schema.CreateTable",
            "Validated logicalName=" + logicalName
                + " primaryName=" + primaryName
        );

        var entityMetadata = new EntityMetadata
        {
            LogicalName = logicalName,
            SchemaName = logicalName,
            DisplayName = CreateLabel(request.DisplayName, language),
            DisplayCollectionName = CreateLabel(
                request.PluralDisplayName,
                language
            ),
            Description = request.Description == null
                ? null
                : CreateLabel(request.Description, language),
            OwnershipType = request.IsUserOwned
                ? OwnershipTypes.UserOwned
                : OwnershipTypes.OrganizationOwned,
            IsActivity = false
        };

        var primaryColumn = new StringAttributeMetadata
        {
            SchemaName = primaryName,
            DisplayName = CreateLabel(
                request.PrimaryNameDisplayName,
                language
            ),
            Format = StringFormat.Text,
            FormatName = StringFormatName.Text,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.None
            ),
            MaxLength = request.PrimaryNameMaxLength
        };

        var sdkRequest = new CreateEntityRequest
        {
            Entity = entityMetadata,
            PrimaryAttribute = primaryColumn,
            SolutionUniqueName = context.SolutionUniqueName
        };

        var response = (CreateEntityResponse)await _client.ExecuteAsync(
            sdkRequest,
            cancellationToken
        );
        if( response.EntityId == Guid.Empty )
        {
            throw new InvalidOperationException(
                "Table creation completed without a table identity."
            );
        }

        try
        {
            await VerifyCreatedTableAsync(
                logicalName,
                response.EntityId,
                cancellationToken
            );
        }
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch( Exception ex )
        {
            throw new SchemaWriteOutcomeUnknownException(
                "Table creation returned ID " + response.EntityId
                    + ", but readback failed: " + ex.Message,
                response.EntityId,
                ex
            );
        }

        SessionLog.Info(
            "Schema.CreateTable",
            "Completed logicalName=" + logicalName
                + " entityId=" + response.EntityId
        );
        return response.EntityId;
    }

    public async Task<Guid> CreateColumnAsync(
        CreateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Schema.CreateColumn",
            "Started table=" + request.TableLogicalName
                + " schemaSuffix=" + request.SchemaSuffix
                + " kind=" + request.Kind
        );
        RequireWriteContext(request.Context);
        var context = request.Context;
        EnsureWriteAllowed(context);
        await VerifySolutionWriteScopeAsync(context, cancellationToken);
        RequirePublisherContext(context);

        ValidateColumnCreation(request);
        RequireTableIdentity(request.TableMetadataId);
        if( request.TableMetadataId != Guid.Empty )
        {
            await VerifyTableSolutionScopeAsync(
                context,
                request.TableMetadataId,
                cancellationToken
            );
            var table = await LoadEntityMetadataAsync(
                request.TableLogicalName,
                request.TableMetadataId,
                retrieveAsIfPublished: true,
                cancellationToken
            );
            var capability = ColumnCapabilityPolicy.EvaluateTable(
                table.IsCustomizable?.Value,
                table.CanCreateAttributes?.Value
            );
            if( !capability.CanCreateColumn )
            {
                throw new InvalidOperationException(
                    capability.CreateColumnReason
                    ?? ColumnCapabilityPolicy.ReasonUnknownTable
                );
            }
        }

        var schemaName = BuildSchemaName(
            context.PublisherPrefix,
            request.SchemaSuffix
        );
        await EnsureColumnNameAvailableAsync(
            request.TableLogicalName,
            schemaName,
            cancellationToken
        );
        var attribute = CreateAttributeMetadata(
            request.Kind,
            schemaName,
            request.DisplayName,
            request.Description,
            context.BaseLanguage,
            request.MaxLength,
            request.MinValue,
            request.MaxValue,
            request.Precision,
            request.RequirementLevel,
            request.BooleanDefaultValue,
            request.BooleanTrueLabel,
            request.BooleanFalseLabel
        );

        var sdkRequest = new CreateAttributeRequest
        {
            EntityName = request.TableLogicalName,
            Attribute = attribute,
            SolutionUniqueName = context.SolutionUniqueName
        };

        var response = (CreateAttributeResponse)await _client.ExecuteAsync(
            sdkRequest,
            cancellationToken
        );
        if( response.AttributeId == Guid.Empty )
        {
            throw new InvalidOperationException(
                "Column creation completed without a column identity."
            );
        }

        try
        {
            await VerifyCreatedColumnAsync(
                request.TableLogicalName,
                schemaName,
                request.TableMetadataId,
                response.AttributeId,
                cancellationToken
            );
        }
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch( Exception ex )
        {
            throw new SchemaWriteOutcomeUnknownException(
                "Column creation returned ID " + response.AttributeId
                    + ", but readback failed: " + ex.Message,
                response.AttributeId,
                ex
            );
        }

        SessionLog.Info(
            "Schema.CreateColumn",
            "Completed table=" + request.TableLogicalName
                + " logicalName=" + schemaName
                + " attributeId=" + response.AttributeId
        );
        return response.AttributeId;
    }

    public async Task<ColumnUpdateResult> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Schema.UpdateColumn",
            "Started table=" + request.TableLogicalName
                + " column=" + request.ColumnLogicalName
                + " expectedMetadataId=" + request.ExpectedMetadataId
        );
        RequireWriteContext(request.Context);
        var context = request.Context;
        EnsureWriteAllowed(context);
        await VerifySolutionWriteScopeAsync(context, cancellationToken);
        RequirePublisherContext(context);
        RequireTableIdentity(request.TableMetadataId);

        if( request.TableMetadataId != Guid.Empty )
        {
            await VerifyTableSolutionScopeAsync(
                context,
                request.TableMetadataId,
                cancellationToken
            );
            await LoadEntityMetadataAsync(
                request.TableLogicalName,
                request.TableMetadataId,
                retrieveAsIfPublished: true,
                cancellationToken
            );
        }

        var freshMetadata = await LoadAttributeMetadataAsync(
            request.TableLogicalName,
            request.ColumnLogicalName,
            retrieveAsIfPublished: true,
            cancellationToken
        );
        var fresh = CreateColumn(
            freshMetadata,
            ParseBaseLanguage(context.BaseLanguage)
        );
        if( request.ExpectedMetadataId == Guid.Empty )
        {
            throw new ColumnConflictException(
                "The column identity is missing; reload before saving."
            );
        }

        if( fresh.MetadataId != request.ExpectedMetadataId )
        {
            throw new ColumnConflictException(
                "The column was changed while you were editing it. "
                + "Reload and review before saving."
            );
        }

        var capability = ColumnCapabilityPolicy.Evaluate(fresh);
        if( !capability.CanEdit )
        {
            throw new InvalidOperationException(
                capability.EditReason ?? ColumnCapabilityPolicy.ReasonUnknown
            );
        }

        ValidateColumnUpdate(request, fresh);

        var changed = false;
        var displayName = fresh.DisplayName;
        var description = fresh.Description;
        var newMaxLength = fresh.MaxLength;
        var requirement = fresh.RequirementLevel;

        if( request.SetDisplayName
            && !string.Equals(
                request.DisplayName,
                fresh.DisplayName,
                StringComparison.Ordinal
            ) )
        {
            if( !capability.CanEditDisplayName )
            {
                throw new InvalidOperationException(
                    capability.EditDisplayNameReason
                    ?? ColumnCapabilityPolicy.ReasonNotRenameable
                );
            }

            displayName = request.DisplayName;
            changed = true;
        }

        if( request.SetDescription )
        {
            var requestedDescription = string.IsNullOrWhiteSpace(
                request.Description
            )
                ? string.Empty
                : request.Description;
            if( !string.Equals(
                requestedDescription,
                fresh.Description,
                StringComparison.Ordinal
            ) )
            {
                if( !capability.CanEditDescription )
                {
                    throw new InvalidOperationException(
                        capability.EditDescriptionReason
                        ?? ColumnCapabilityPolicy.ReasonNotCustomizableSettings
                    );
                }

                description = requestedDescription;
                changed = true;
            }
        }

        if( request.NewMaxLength.HasValue
            && request.NewMaxLength.Value != fresh.MaxLength )
        {
            if( !capability.CanIncreaseLength )
            {
                throw new InvalidOperationException(
                    capability.IncreaseLengthReason
                    ?? ColumnCapabilityPolicy.ReasonNotCustomizableSettings
                );
            }

            if( request.NewMaxLength.Value < (fresh.MaxLength ?? 0) )
            {
                throw new InvalidOperationException(
                    "Maximum length can only be increased, not decreased."
                );
            }

            newMaxLength = request.NewMaxLength.Value;
            changed = true;
        }

        if( request.SetRequirementLevel
            && !string.Equals(
                request.RequirementLevel,
                fresh.RequirementLevel,
                StringComparison.Ordinal
            ) )
        {
            if( !capability.CanEditRequirement )
            {
                throw new InvalidOperationException(
                    capability.EditRequirementReason
                    ?? ColumnCapabilityPolicy.ReasonNotCustomizableSettings
                );
            }

            requirement = request.RequirementLevel;
            changed = true;
        }

        if( !changed )
        {
            SessionLog.Info(
                "Schema.UpdateColumn",
                "No changes table=" + request.TableLogicalName
                    + " column=" + request.ColumnLogicalName
            );
            return new ColumnUpdateResult
            {
                MetadataId = fresh.MetadataId,
                Changed = false
            };
        }

        var metadata = CreateUpdateMetadata(
            freshMetadata,
            displayName,
            description,
            newMaxLength,
            requirement,
            request.SetDisplayName,
            request.SetDescription,
            request.SetRequirementLevel,
            context.BaseLanguage
        );

        var sdkRequest = new UpdateAttributeRequest
        {
            EntityName = request.TableLogicalName,
            Attribute = metadata,
            SolutionUniqueName = context.SolutionUniqueName,
            MergeLabels = true
        };

        await _client.ExecuteAsync(sdkRequest, cancellationToken);
        if( request.SetRequirementLevel )
        {
            try
            {
                await UpdateRequirementLevelViaWebApiAsync(
                    request,
                    requirement ?? RequirementLevels.Optional,
                    context,
                    cancellationToken
                );
            }
            catch( OperationCanceledException )
                when( cancellationToken.IsCancellationRequested )
            {
                throw;
            }
            catch( Exception ex )
            {
                throw new SchemaWriteOutcomeUnknownException(
                    "Column requirement update for ID " + fresh.MetadataId
                        + " was sent, but the Web API fallback failed: "
                        + ex.Message,
                    fresh.MetadataId,
                    ex
                );
            }
        }

        try
        {
            await VerifyUpdatedColumnAsync(
                request,
                displayName,
                description,
                newMaxLength,
                requirement,
                context.BaseLanguage,
                cancellationToken
            );
        }
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch( Exception ex )
        {
            throw new SchemaWriteOutcomeUnknownException(
                "Column update for ID " + fresh.MetadataId
                    + " was sent, but readback failed: " + ex.Message,
                fresh.MetadataId,
                ex
            );
        }

        var result = new ColumnUpdateResult
        {
            MetadataId = fresh.MetadataId,
            Changed = true
        };
        SessionLog.Info(
            "Schema.UpdateColumn",
            "Completed table=" + request.TableLogicalName
                + " column=" + request.ColumnLogicalName
                + " metadataId=" + result.MetadataId
        );
        return result;
    }

    public async Task<IReadOnlyList<DependencyInfo>>
        GetColumnDeleteDependenciesAsync(
        Guid columnMetadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Schema.GetDependencies",
            "Started metadataId=" + columnMetadataId
        );
        if( columnMetadataId == Guid.Empty )
        {
            throw new ArgumentException(
                "A column metadata ID is required.",
                nameof(columnMetadataId)
            );
        }

        var request = new RetrieveDependenciesForDeleteRequest
        {
            ComponentType = ComponentTypeAttribute,
            ObjectId = columnMetadataId
        };

        var response = (RetrieveDependenciesForDeleteResponse)
            await _client.ExecuteAsync(request, cancellationToken);
        if( response.EntityCollection == null )
        {
            throw new InvalidOperationException(
                "The dependency check returned no dependency collection."
            );
        }

        var dependencies = new List<DependencyInfo>();
        foreach( var entity in response.EntityCollection.Entities )
        {
            dependencies.Add(new DependencyInfo
            {
                ComponentType = GetOptionValue(
                    entity,
                    "dependentcomponenttype"
                ) ?? GetOptionValue(entity, "componenttype"),
                ObjectId = GetGuidValue(
                    entity,
                    "dependentcomponentobjectid"
                ) ?? GetGuidValue(entity, "objectid"),
                Name = entity.GetAttributeValue<string>(
                        "dependentcomponentname"
                    )
                    ?? entity.GetAttributeValue<string>("name")
                    ?? entity.GetAttributeValue<string>("friendlyname")
                    ?? string.Empty
            });
        }

        SessionLog.Info(
            "Schema.GetDependencies",
            "Completed metadataId=" + columnMetadataId
                + " count=" + dependencies.Count
        );
        return dependencies;
    }

    public async Task DeleteColumnAsync(
        string tableLogicalName,
        string columnLogicalName,
        Guid expectedMetadataId,
        CancellationToken cancellationToken,
        Guid tableMetadataId = default,
        SolutionWriteContext? context = null
    )
    {
        SessionLog.Info(
            "Schema.DeleteColumn",
            "Started table=" + tableLogicalName
                + " column=" + columnLogicalName
                + " expectedMetadataId=" + expectedMetadataId
        );
        RequireWriteContext(context);
        if( context != null )
        {
            EnsureWriteAllowed(context);
            await VerifySolutionWriteScopeAsync(context, cancellationToken);
        }
        RequireTableIdentity(tableMetadataId);

        if( tableMetadataId != Guid.Empty )
        {
            if( context != null )
            {
                await VerifyTableSolutionScopeAsync(
                    context,
                    tableMetadataId,
                    cancellationToken
                );
            }
            await LoadEntityMetadataAsync(
                tableLogicalName,
                tableMetadataId,
                retrieveAsIfPublished: true,
                cancellationToken
            );
        }

        var fresh = await LoadColumnDefinitionAsync(
            tableLogicalName,
            columnLogicalName,
            retrieveAsIfPublished: true,
            cancellationToken
        );
        if( expectedMetadataId == Guid.Empty )
        {
            throw new ColumnConflictException(
                "The column identity is missing; reload before deleting."
            );
        }

        if( fresh.MetadataId != expectedMetadataId )
        {
            throw new ColumnConflictException(
                "The column was changed while you were editing it. "
                + "Reload and review before deleting."
            );
        }

        var capability = ColumnCapabilityPolicy.Evaluate(fresh);
        if( !capability.CanDelete )
        {
            throw new InvalidOperationException(
                capability.DeleteReason
                ?? ColumnCapabilityPolicy.ReasonUnknown
            );
        }

        var dependencies = await GetColumnDeleteDependenciesAsync(
            fresh.MetadataId,
            cancellationToken
        );
        if( dependencies.Count > 0 )
        {
            throw new InvalidOperationException(
                "Column has dependencies and cannot be deleted."
            );
        }

        var request = new DeleteAttributeRequest
        {
            EntityLogicalName = tableLogicalName,
            LogicalName = columnLogicalName
        };

        await _client.ExecuteAsync(request, cancellationToken);
        try
        {
            await VerifyColumnDeletedAsync(
                tableLogicalName,
                columnLogicalName,
                cancellationToken
            );
        }
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch( Exception ex )
        {
            throw new SchemaWriteOutcomeUnknownException(
                "Column deletion for ID " + fresh.MetadataId
                    + " was sent, but absence could not be verified: "
                    + ex.Message,
                fresh.MetadataId,
                ex
            );
        }
        SessionLog.Info(
            "Schema.DeleteColumn",
            "Completed table=" + tableLogicalName
                + " column=" + columnLogicalName
        );
    }

    private async Task VerifyColumnDeletedAsync(
        string tableLogicalName,
        string columnLogicalName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await LoadAttributeMetadataAsync(
                tableLogicalName,
                columnLogicalName,
                retrieveAsIfPublished: true,
                cancellationToken
            );
        }
        catch( Exception ex ) when( IsMetadataNotFound(ex) )
        {
            return;
        }
        catch( Exception ex )
        {
            throw new InvalidOperationException(
                "Column deletion was sent, but its result could not be verified: "
                + ex.Message,
                ex
            );
        }

        throw new InvalidOperationException(
            "Column deletion was sent, but the column is still present."
        );
    }

    private static bool IsMetadataNotFound(Exception exception)
    {
        for( var current = exception; current != null; current = current.InnerException )
        {
            var message = current.Message;
            if( message.Contains("could not find", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase)
                || message.Contains("cannot be found", StringComparison.OrdinalIgnoreCase) )
            {
                return true;
            }
        }

        return false;
    }

    public async Task PublishTableAsync(
        string tableLogicalName,
        CancellationToken cancellationToken,
        SolutionWriteContext? context = null,
        Guid tableMetadataId = default
    )
    {
        SessionLog.Info(
            "Schema.PublishTable",
            "Started table=" + tableLogicalName
                + " metadataId=" + tableMetadataId
        );
        RequireWriteContext(context);
        if( context != null )
        {
            EnsureWriteAllowed(context);
            await VerifySolutionWriteScopeAsync(context, cancellationToken);
            RequireTableIdentity(tableMetadataId);
            await VerifyTableSolutionScopeAsync(
                context,
                tableMetadataId,
                cancellationToken
            );
            await LoadEntityMetadataAsync(
                tableLogicalName,
                tableMetadataId,
                retrieveAsIfPublished: true,
                cancellationToken
            );
        }

        var xml = BuildPublishXml(tableLogicalName);
        var request = new PublishXmlRequest
        {
            ParameterXml = xml
        };

        await _client.ExecuteAsync(request, cancellationToken);
        SessionLog.Info(
            "Schema.PublishTable",
            "Completed table=" + tableLogicalName
        );
    }

    public async Task DeleteTableAsync(
        string tableLogicalName,
        Guid tableMetadataId,
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Schema.DeleteTable",
            "Started table=" + tableLogicalName
                + " metadataId=" + tableMetadataId
        );
        var request = new DeleteEntityRequest
        {
            LogicalName = tableLogicalName
        };

        await _client.ExecuteAsync(request, cancellationToken);
        SessionLog.Info(
            "Schema.DeleteTable",
            "Completed table=" + tableLogicalName
        );
    }

    private static void EnsureWriteAllowed(SolutionWriteContext context)
    {
        if( context == null )
        {
            throw new InvalidOperationException(
                "A solution write context is required."
            );
        }

        if( context.CanWrite && !context.IsManaged )
        {
            return;
        }

        var reason = context.WriteDisabledReason;
        if( string.IsNullOrWhiteSpace(reason) )
        {
            reason = context.IsManaged
                ? ColumnCapabilityPolicy.ReasonManagedSolution
                : "Writes are disabled because the solution context is unavailable.";
        }

        throw new InvalidOperationException(reason);
    }

    private void RequireWriteContext(SolutionWriteContext? context)
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) || context != null )
        {
            return;
        }

        throw new InvalidOperationException(
            "A solution write context is required."
        );
    }

    private static void RequirePublisherContext(SolutionWriteContext context)
    {
        if( string.IsNullOrWhiteSpace(context.SolutionUniqueName) )
        {
            throw new InvalidOperationException(
                "The selected solution unique name is unavailable."
            );
        }

        if( string.IsNullOrWhiteSpace(context.PublisherPrefix) )
        {
            throw new InvalidOperationException(
                "The selected solution publisher prefix is unavailable."
            );
        }

        if( string.IsNullOrWhiteSpace(context.BaseLanguage)
            || !int.TryParse(
                context.BaseLanguage,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _
            ) )
        {
            throw new InvalidOperationException(
                "The environment base language is unavailable."
            );
        }
    }

    private void RequireTableIdentity(Guid tableMetadataId)
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl)
            || tableMetadataId != Guid.Empty )
        {
            return;
        }

        throw new ArgumentException(
            "A table metadata ID is required for a schema write.",
            nameof(tableMetadataId)
        );
    }

    private async Task VerifySolutionWriteScopeAsync(
        SolutionWriteContext context,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) )
        {
            return;
        }

        if( context.SolutionId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.SolutionUniqueName) )
        {
            throw new InvalidOperationException(
                "The selected solution identity is incomplete."
            );
        }

        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet(
                "solutionid",
                "uniquename",
                "ismanaged",
                "publisherid"
            )
        };
        query.Criteria.AddCondition(
            "solutionid",
            ConditionOperator.Equal,
            context.SolutionId
        );
        var response = await _client.RetrieveMultipleAsync(
            query,
            cancellationToken
        );
        if( response.Entities.Count == 0 )
        {
            throw new InvalidOperationException(
                "The selected solution no longer exists."
            );
        }

        var solution = response.Entities[0];
        if( !solution.Contains("ismanaged") )
        {
            throw new InvalidOperationException(
                "The selected solution management state is unknown."
            );
        }

        var isManaged = solution.GetAttributeValue<bool>("ismanaged");
        var solutionId = solution.GetAttributeValue<Guid>("solutionid");
        var uniqueName = solution.GetAttributeValue<string>("uniquename");
        var publisherId = GetGuidValue(solution, "publisherid");
        if( solutionId != context.SolutionId )
        {
            throw new InvalidOperationException(
                "The selected solution identity changed; reload it before writing."
            );
        }
        if( isManaged )
        {
            throw new InvalidOperationException(
                ColumnCapabilityPolicy.ReasonManagedSolution
            );
        }

        if( !string.Equals(
            uniqueName,
            context.SolutionUniqueName,
            StringComparison.Ordinal
        ) )
        {
            throw new InvalidOperationException(
                "The selected solution identity changed; reload it before writing."
            );
        }

        if( publisherId != context.PublisherId )
        {
            throw new InvalidOperationException(
                "The selected solution publisher changed; reload it before writing."
            );
        }
    }

    private async Task VerifyTableSolutionScopeAsync(
        SolutionWriteContext context,
        Guid tableMetadataId,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) )
        {
            return;
        }

        if( tableMetadataId == Guid.Empty )
        {
            throw new ArgumentException(
                "A table metadata ID is required.",
                nameof(tableMetadataId)
            );
        }

        var query = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet(
                "solutioncomponentid",
                "solutionid",
                "componenttype",
                "objectid"
            )
        };
        query.Criteria.AddCondition(
            "solutionid",
            ConditionOperator.Equal,
            context.SolutionId
        );
        query.Criteria.AddCondition(
            "componenttype",
            ConditionOperator.Equal,
            SolutionComponentTypes.Entity
        );
        query.Criteria.AddCondition(
            "objectid",
            ConditionOperator.Equal,
            tableMetadataId
        );
        var response = await _client.RetrieveMultipleAsync(
            query,
            cancellationToken
        );
        if( response.Entities.Count == 0 )
        {
            throw new InvalidOperationException(
                "The selected table is no longer part of the selected solution."
            );
        }
    }

    private async Task VerifyCreatedTableAsync(
        string logicalName,
        Guid metadataId,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) )
        {
            return;
        }

        await LoadEntityMetadataAsync(
            logicalName,
            metadataId,
            retrieveAsIfPublished: true,
            cancellationToken
        );
    }

    private async Task EnsureTableNameAvailableAsync(
        string logicalName,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) )
        {
            return;
        }

        var request = new RetrieveEntityRequest
        {
            EntityFilters = EntityFilters.Entity,
            LogicalName = logicalName,
            RetrieveAsIfPublished = true
        };
        try
        {
            await _client.ExecuteAsync(request, cancellationToken);
        }
        catch( Exception ex ) when( IsMetadataNotFound(ex) )
        {
            return;
        }

        throw new InvalidOperationException(
            "A table with the predicted logical name already exists."
        );
    }

    private async Task EnsureColumnNameAvailableAsync(
        string tableLogicalName,
        string columnLogicalName,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) )
        {
            return;
        }

        var request = new RetrieveAttributeRequest
        {
            EntityLogicalName = tableLogicalName,
            LogicalName = columnLogicalName,
            RetrieveAsIfPublished = true
        };
        try
        {
            await _client.ExecuteAsync(request, cancellationToken);
        }
        catch( Exception ex ) when( IsMetadataNotFound(ex) )
        {
            return;
        }

        throw new InvalidOperationException(
            "A column with the predicted logical name already exists."
        );
    }

    private async Task VerifyCreatedColumnAsync(
        string tableLogicalName,
        string columnLogicalName,
        Guid tableMetadataId,
        Guid columnMetadataId,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) )
        {
            return;
        }

        if( tableMetadataId != Guid.Empty )
        {
            await LoadEntityMetadataAsync(
                tableLogicalName,
                tableMetadataId,
                retrieveAsIfPublished: true,
                cancellationToken
            );
        }

        var metadata = await LoadAttributeMetadataByIdAsync(
            tableLogicalName,
            columnMetadataId,
            retrieveAsIfPublished: true,
            cancellationToken
        );
        if( metadata.MetadataId != columnMetadataId
            || !string.Equals(
                metadata.LogicalName,
                columnLogicalName,
                StringComparison.OrdinalIgnoreCase
            ) )
        {
            throw new ColumnConflictException(
                "Column creation returned a different column identity."
            );
        }
    }

    private async Task<AttributeMetadata> LoadAttributeMetadataByIdAsync(
        string tableLogicalName,
        Guid columnMetadataId,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken
    )
    {
        if( columnMetadataId == Guid.Empty )
        {
            throw new ArgumentException(
                "A column metadata ID is required.",
                nameof(columnMetadataId)
            );
        }

        var request = new RetrieveAttributeRequest
        {
            EntityLogicalName = tableLogicalName,
            MetadataId = columnMetadataId,
            RetrieveAsIfPublished = retrieveAsIfPublished
        };
        var response = (RetrieveAttributeResponse)await _client.ExecuteAsync(
            request,
            cancellationToken
        );
        return response.AttributeMetadata;
    }

    private async Task UpdateRequirementLevelViaWebApiAsync(
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

    private async Task VerifyUpdatedColumnAsync(
        UpdateColumnRequest request,
        string displayName,
        string description,
        int? maxLength,
        string? requirement,
        string baseLanguage,
        CancellationToken cancellationToken
    )
    {
        if( string.IsNullOrWhiteSpace(_environmentUrl) )
        {
            return;
        }

        var metadata = await LoadAttributeMetadataAsync(
            request.TableLogicalName,
            request.ColumnLogicalName,
            retrieveAsIfPublished: true,
            cancellationToken
        );
        var actual = CreateColumn(metadata);
        var language = int.Parse(baseLanguage, CultureInfo.InvariantCulture);
        var actualDisplayName = GetLabelForLanguage(metadata.DisplayName, language);
        var actualDescription = GetLabelForLanguage(metadata.Description, language);
        SessionLog.Debug(
            "Schema.UpdateColumn",
            "Readback values table=" + request.TableLogicalName
                + " column=" + request.ColumnLogicalName
                + " metadataId=" + actual.MetadataId
                + " displayName=" + FormatVerificationValue(actualDisplayName)
                + " description=" + FormatVerificationValue(actualDescription)
                + " maxLength=" + FormatVerificationValue(actual.MaxLength)
                + " requirement=" + FormatVerificationValue(actual.RequirementLevel)
                + " requested=displayName:" + request.SetDisplayName
                + ",description:" + request.SetDescription
                + ",maxLength:" + request.NewMaxLength.HasValue
                + ",requirement:" + request.SetRequirementLevel
        );

        var mismatches = new List<string>();
        if( actual.MetadataId != request.ExpectedMetadataId )
        {
            mismatches.Add(
                "metadataId expected=" + request.ExpectedMetadataId
                    + " actual=" + actual.MetadataId
            );
        }

        if( request.SetDisplayName
            && !string.Equals(
                actualDisplayName,
                displayName,
                StringComparison.Ordinal
            ) )
        {
            mismatches.Add(
                "displayName expected=" + FormatVerificationValue(displayName)
                    + " actual=" + FormatVerificationValue(actualDisplayName)
            );
        }

        if( request.SetDescription
            && !string.Equals(
                actualDescription,
                description,
                StringComparison.Ordinal
            ) )
        {
            mismatches.Add(
                "description expected=" + FormatVerificationValue(description)
                    + " actual=" + FormatVerificationValue(actualDescription)
            );
        }

        if( request.NewMaxLength.HasValue
            && actual.MaxLength != maxLength )
        {
            mismatches.Add(
                "maxLength expected=" + FormatVerificationValue(maxLength)
                    + " actual=" + FormatVerificationValue(actual.MaxLength)
            );
        }

        if( request.SetRequirementLevel
            && !string.Equals(
                actual.RequirementLevel,
                requirement,
                StringComparison.Ordinal
            ) )
        {
            mismatches.Add(
                "requirement expected=" + FormatVerificationValue(requirement)
                    + " actual=" + FormatVerificationValue(actual.RequirementLevel)
            );
        }

        if( mismatches.Count > 0 )
        {
            SessionLog.Warning(
                "Schema.UpdateColumn",
                "Readback mismatch table=" + request.TableLogicalName
                    + " column=" + request.ColumnLogicalName
                    + " mismatches=[" + string.Join(" | ", mismatches) + "]"
            );
            throw new ColumnConflictException(
                "Column was saved, but the requested values could not be verified."
            );
        }
    }

    private static string FormatVerificationValue(string? value)
    {
        return "\"" + SessionLog.SafeSingleLine(value ?? "<null>") + "\"";
    }

    private static string FormatVerificationValue(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "<null>";
    }

    private static string BuildSchemaName(string prefix, string suffix)
    {
        return prefix + "_" + suffix;
    }

    private static void ValidateTableCreation(CreateTableRequest request)
    {
        RequireText(request.DisplayName, "Display name");
        RequireText(request.PluralDisplayName, "Plural display name");
        RequireText(request.SchemaSuffix, "Schema suffix");
        RequireText(request.PrimaryNameDisplayName, "Primary name");
        RequireText(request.PrimaryNameSchemaSuffix, "Primary name suffix");
        ValidateSchemaSuffix(request.SchemaSuffix);
        ValidateSchemaSuffix(request.PrimaryNameSchemaSuffix);
        ValidateTextLength(
            request.PrimaryNameMaxLength,
            MinTextLength,
            MaxTextLength,
            "Primary name length"
        );
        if( request.Description != null
            && request.Description.Length > MaxDescriptionLength )
        {
            throw new InvalidOperationException(
                "Description is too long."
            );
        }
    }

    private static void ValidateColumnCreation(CreateColumnRequest request)
    {
        if( string.IsNullOrWhiteSpace(request.Context.PublisherPrefix) )
        {
            throw new InvalidOperationException(
                "The solution publisher prefix is required."
            );
        }

        RequireText(request.DisplayName, "Display name");
        RequireText(request.SchemaSuffix, "Schema suffix");
        ValidateSchemaSuffix(request.SchemaSuffix);
        ValidateRequirementLevel(request.RequirementLevel);
        if( request.Description != null
            && request.Description.Length > MaxDescriptionLength )
        {
            throw new InvalidOperationException(
                "Description is too long."
            );
        }

        switch( request.Kind )
        {
            case ColumnKind.Text:
                ValidateTextLength(
                    request.MaxLength,
                    MinTextLength,
                    MaxTextLength,
                    "Text length"
                );
                break;
            case ColumnKind.MultilineText:
                ValidateTextLength(
                    request.MaxLength,
                    MinMemoLength,
                    MaxMemoLength,
                    "Multiline length"
                );
                break;
            case ColumnKind.WholeNumber:
                ValidateNumericBounds(
                    request.MinValue,
                    request.MaxValue,
                    IntMin,
                    IntMax
                );
                ValidateWholeNumber(request.MinValue, "Minimum value");
                ValidateWholeNumber(request.MaxValue, "Maximum value");
                break;
            case ColumnKind.Decimal:
                ValidateNumericBounds(
                    request.MinValue,
                    request.MaxValue,
                    DecMin,
                    DecMax
                );
                ValidatePrecision(request.Precision);
                break;
            case ColumnKind.YesNo:
                RequireText(request.BooleanTrueLabel, "Yes label");
                RequireText(request.BooleanFalseLabel, "No label");
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported column type."
                );
        }
    }

    private static void ValidateColumnUpdate(
        UpdateColumnRequest request,
        DataverseColumn current
    )
    {
        if( request.SetDisplayName )
        {
            RequireText(request.DisplayName, "Display name");
        }

        if( request.SetDescription
            && request.Description.Length > MaxDescriptionLength )
        {
            throw new InvalidOperationException(
                "Description is too long."
            );
        }

        if( request.SetRequirementLevel )
        {
            ValidateRequirementLevel(request.RequirementLevel);
        }

        if( request.NewMaxLength.HasValue )
        {
            if( current.Kind == ColumnKind.Text )
            {
                ValidateTextLength(
                    request.NewMaxLength,
                    MinTextLength,
                    MaxTextLength,
                    "Text length"
                );
            }
            else if( current.Kind == ColumnKind.MultilineText )
            {
                ValidateTextLength(
                    request.NewMaxLength,
                    MinMemoLength,
                    MaxMemoLength,
                    "Multiline length"
                );
            }
            else
            {
                throw new InvalidOperationException(
                    "Maximum length can only be changed on text columns."
                );
            }
        }
    }

    private static void ValidateWholeNumber(
        decimal? value,
        string field
    )
    {
        if( value.HasValue && value.Value != decimal.Truncate(value.Value) )
        {
            throw new InvalidOperationException(
                "Whole number value is invalid: " + field
            );
        }
    }

    private static void RequireText(string value, string field)
    {
        if( string.IsNullOrWhiteSpace(value) )
        {
            throw new InvalidOperationException(
                $"{field} is required."
            );
        }

        if( value.Length > MaxDisplayLength )
        {
            throw new InvalidOperationException(
                $"{field} is too long."
            );
        }
    }

    private static void ValidateSchemaSuffix(string suffix)
    {
        if( string.IsNullOrWhiteSpace(suffix) )
        {
            throw new InvalidOperationException(
                "Schema suffix is required."
            );
        }

        if( !char.IsLetter(suffix[0]) )
        {
            throw new InvalidOperationException(
                "Schema suffix must start with a letter."
            );
        }

        foreach( var character in suffix )
        {
            if( char.IsLetterOrDigit(character) || character == '_' )
            {
                continue;
            }

            throw new InvalidOperationException(
                "Schema suffix may only contain letters, digits, and "
                + "underscores."
            );
        }

        if( suffix.Length > MaxSchemaNameLength )
        {
            throw new InvalidOperationException(
                "Schema suffix is too long."
            );
        }
    }

    private static void ValidateTextLength(
        int? value,
        int min,
        int max,
        string field
    )
    {
        if( value.HasValue && (value.Value < min || value.Value > max) )
        {
            throw new InvalidOperationException(
                $"{field} must be between {min} and {max}."
            );
        }
    }

    private static void ValidateNumericBounds(
        decimal? min,
        decimal? max,
        decimal lowerBound,
        decimal upperBound
    )
    {
        if( min.HasValue && min.Value < lowerBound )
        {
            throw new InvalidOperationException(
                "Minimum value is below the supported range."
            );
        }

        if( max.HasValue && max.Value > upperBound )
        {
            throw new InvalidOperationException(
                "Maximum value is above the supported range."
            );
        }

        if( min.HasValue && max.HasValue && min.Value > max.Value )
        {
            throw new InvalidOperationException(
                "Minimum value must be less than or equal to maximum."
            );
        }
    }

    private static void ValidatePrecision(int? precision)
    {
        if( precision.HasValue
            && (precision.Value < MinPrecision
                || precision.Value > MaxPrecision) )
        {
            throw new InvalidOperationException(
                $"Precision must be between {MinPrecision} and "
                + MaxPrecision + "."
            );
        }
    }

    private static void ValidateRequirementLevel(string level)
    {
        if( level == RequirementLevels.Optional
            || level == RequirementLevels.Recommended
            || level == RequirementLevels.Required )
        {
            return;
        }

        throw new InvalidOperationException(
            "The selected requirement level is not supported."
        );
    }

    private static AttributeMetadata CreateAttributeMetadata(
        ColumnKind kind,
        string schemaName,
        string displayName,
        string? description,
        string baseLanguage,
        int? maxLength,
        decimal? minValue,
        decimal? maxValue,
        int? precision,
        string requirementLevel,
        bool booleanDefaultValue,
        string booleanTrueLabel,
        string booleanFalseLabel
    )
    {
        var language = int.Parse(baseLanguage, CultureInfo.InvariantCulture);
        var label = CreateLabel(displayName, language);
        var descLabel = description == null
            ? null
            : CreateLabel(description, language);
        var requirement = ParseRequirementLevel(requirementLevel);

        return kind switch
        {
            ColumnKind.Text => new StringAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = schemaName,
                DisplayName = label,
                Description = descLabel,
                Format = StringFormat.Text,
                FormatName = StringFormatName.Text,
                MaxLength = maxLength ?? ColumnDefaults.TextLength,
                RequiredLevel = requirement
            },
            ColumnKind.MultilineText => new MemoAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = schemaName,
                DisplayName = label,
                Description = descLabel,
                Format = StringFormat.TextArea,
                FormatName = MemoFormatName.TextArea,
                ImeMode = ImeMode.Disabled,
                MaxLength = maxLength ?? ColumnDefaults.MultilineLength,
                RequiredLevel = requirement
            },
            ColumnKind.WholeNumber => new IntegerAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = schemaName,
                DisplayName = label,
                Description = descLabel,
                Format = IntegerFormat.None,
                MinValue = minValue.HasValue
                    ? decimal.ToInt32(minValue.Value)
                    : ColumnDefaults.WholeNumberMin,
                MaxValue = maxValue.HasValue
                    ? decimal.ToInt32(maxValue.Value)
                    : ColumnDefaults.WholeNumberMax,
                RequiredLevel = requirement
            },
            ColumnKind.Decimal => new DecimalAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = schemaName,
                DisplayName = label,
                Description = descLabel,
                MinValue = minValue.HasValue
                    ? (decimal?)minValue.Value
                    : (decimal?)ColumnDefaults.DecimalMin,
                MaxValue = maxValue.HasValue
                    ? (decimal?)maxValue.Value
                    : (decimal?)ColumnDefaults.DecimalMax,
                Precision = precision ?? ColumnDefaults.DecimalPrecision,
                RequiredLevel = requirement
            },
            ColumnKind.YesNo => new BooleanAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = schemaName,
                DisplayName = label,
                Description = descLabel,
                DefaultValue = booleanDefaultValue,
                OptionSet = CreateBooleanOptionSet(
                    booleanTrueLabel,
                    booleanFalseLabel,
                    language
                ),
                RequiredLevel = requirement
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static AttributeMetadata CreateUpdateMetadata(
        AttributeMetadata currentMetadata,
        string displayName,
        string description,
        int? newMaxLength,
        string? requirementLevel,
        bool setDisplayName,
        bool setDescription,
        bool setRequirementLevel,
        string baseLanguage
    )
    {
        var language = int.Parse(baseLanguage, CultureInfo.InvariantCulture);
        var metadata = currentMetadata;
        if( setDisplayName )
        {
            metadata.DisplayName = MergeLabel(
                currentMetadata.DisplayName,
                displayName,
                language
            );
        }

        if( setDescription )
        {
            metadata.Description = MergeLabel(
                currentMetadata.Description,
                description,
                language
            );
        }
        if( setRequirementLevel )
        {
            var parsedRequirement = ParseRequirementLevel(requirementLevel);
            var currentRequirement = metadata.RequiredLevel;
            if( currentRequirement == null )
            {
                metadata.RequiredLevel = parsedRequirement;
            }
            else
            {
                currentRequirement.Value = parsedRequirement.Value;
                currentRequirement.IsValueModified = true;
            }
        }

        if( newMaxLength.HasValue )
        {
            if( metadata is StringAttributeMetadata stringMeta )
            {
                stringMeta.MaxLength = newMaxLength.Value;
            }
            else if( metadata is MemoAttributeMetadata memoMeta )
            {
                memoMeta.MaxLength = newMaxLength.Value;
            }
        }

        return metadata;
    }

    private static BooleanOptionSetMetadata CreateBooleanOptionSet(
        string trueLabel,
        string falseLabel,
        int language
    )
    {
        return new BooleanOptionSetMetadata(
            new OptionMetadata(
                CreateLabel(trueLabel, language),
                1
            ),
            new OptionMetadata(
                CreateLabel(falseLabel, language),
                0
            )
        );
    }

    private static Label MergeLabel(
        Label? existing,
        string value,
        int language
    )
    {
        var label = new Label();
        var preservedLanguages = new HashSet<int>();
        if( existing != null )
        {
            foreach( var localized in existing.LocalizedLabels )
            {
                if( localized.LanguageCode == language )
                {
                    continue;
                }

                label.LocalizedLabels.Add(new UserLocalizedLabel
                {
                    Label = localized.Label,
                    LanguageCode = localized.LanguageCode
                });
                preservedLanguages.Add(localized.LanguageCode);
            }

            var userLocalized = existing.UserLocalizedLabel;
            if( userLocalized != null
                && userLocalized.LanguageCode != language
                && preservedLanguages.Add(userLocalized.LanguageCode) )
            {
                label.LocalizedLabels.Add(new UserLocalizedLabel
                {
                    Label = userLocalized.Label,
                    LanguageCode = userLocalized.LanguageCode
                });
            }
        }

        var baseLabel = new UserLocalizedLabel
        {
            Label = value,
            LanguageCode = language
        };
        label.UserLocalizedLabel = baseLabel;
        label.LocalizedLabels.Add(baseLabel);
        return label;
    }

    private static Label CreateLabel(string text, int language)
    {
        var localizedLabel = new UserLocalizedLabel
        {
            Label = text,
            LanguageCode = language
        };
        var label = new Label
        {
            UserLocalizedLabel = localizedLabel
        };
        label.LocalizedLabels.Add(localizedLabel);
        return label;
    }

    private static AttributeRequiredLevelManagedProperty ParseRequirementLevel(
        string? level
    )
    {
        return level switch
        {
            RequirementLevels.Recommended => new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.Recommended
            ),
            RequirementLevels.Required => new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.ApplicationRequired
            ),
            _ => new AttributeRequiredLevelManagedProperty(
                AttributeRequiredLevel.None
            )
        };
    }

    private static string BuildPublishXml(string tableLogicalName)
    {
        if( string.IsNullOrWhiteSpace(tableLogicalName) )
        {
            throw new ArgumentException(
                "A table logical name is required.",
                nameof(tableLogicalName)
            );
        }

        var name = System.Security.SecurityElement.Escape(tableLogicalName);
        var builder = new StringBuilder();
        builder.Append("<importexportxml>");
        builder.Append("<entities><entity>");
        builder.Append(name);
        builder.Append("</entity></entities>");
        builder.Append("</importexportxml>");
        return builder.ToString();
    }

    private static int? GetOptionValue(Entity entity, string attribute)
    {
        if( !entity.Contains(attribute) )
        {
            return null;
        }

        var value = entity.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>(
            attribute);
        return value?.Value;
    }

    private static Guid? GetGuidValue(Entity entity, string attribute)
    {
        if( !entity.Contains(attribute) )
        {
            return null;
        }

        return entity[attribute] switch
        {
            Guid value => value,
            EntityReference reference => reference.Id,
            _ => null
        };
    }

    public static DataverseColumn CreateColumn(
        AttributeMetadata metadata,
        int? baseLanguage = null
    )
    {
        var kind = GetColumnKind(metadata);

        int? maxLength = null;
        if( metadata is StringAttributeMetadata stringMeta )
        {
            maxLength = stringMeta.MaxLength;
        }
        else if( metadata is MemoAttributeMetadata memoMeta )
        {
            maxLength = memoMeta.MaxLength;
        }

        decimal? minValue = null;
        decimal? maxValue = null;
        int? precision = null;
        if( metadata is IntegerAttributeMetadata intMeta )
        {
            minValue = intMeta.MinValue;
            maxValue = intMeta.MaxValue;
        }
        else if( metadata is DecimalAttributeMetadata decMeta )
        {
            minValue = decMeta.MinValue;
            maxValue = decMeta.MaxValue;
            precision = decMeta.Precision;
        }

        var booleanMetadata = metadata as BooleanAttributeMetadata;
        var trueOption = booleanMetadata?.OptionSet?.TrueOption;
        var falseOption = booleanMetadata?.OptionSet?.FalseOption;
        var isUnmanagedCustom = metadata.IsCustomAttribute == true
            && metadata.IsManaged == false;

        return new DataverseColumn
        {
            MetadataId = metadata.MetadataId ?? Guid.Empty,
            LogicalName = metadata.LogicalName ?? string.Empty,
            SchemaName = metadata.SchemaName ?? string.Empty,
            DisplayName = GetLabel(metadata.DisplayName, baseLanguage),
            Description = GetLabel(metadata.Description, baseLanguage),
            Kind = kind,
            MaxLength = maxLength,
            MinValue = minValue,
            MaxValue = maxValue,
            Precision = precision,
            IsCustom = metadata.IsCustomAttribute,
            IsManaged = metadata.IsManaged,
            IsPrimaryId = metadata.IsPrimaryId,
            IsPrimaryName = metadata.IsPrimaryName,
            IsCustomizable = metadata.IsCustomizable?.Value
                ?? (isUnmanagedCustom ? true : null),
            IsRenameable = metadata.IsRenameable?.Value
                ?? (isUnmanagedCustom ? true : null),
            CanModifyAdditionalSettings =
                metadata.CanModifyAdditionalSettings?.Value
                ?? (isUnmanagedCustom ? true : null),
            CanChangeRequirement = metadata.RequiredLevel?.CanBeChanged
                ?? (isUnmanagedCustom ? true : null),
            RequirementLevel = GetRequirementLevel(metadata.RequiredLevel?.Value),
            AttributeTypeCode = metadata.AttributeType?.ToString()
                ?? metadata.AttributeTypeName?.ToString(),
            AttributeFormat = GetAttributeFormat(metadata),
            AttributeOf = metadata.AttributeOf,
            IsLogical = metadata.IsLogical,
            SourceType = metadata.SourceType,
            AutoNumberFormat = metadata.AutoNumberFormat,
            BooleanDefaultValue = booleanMetadata?.DefaultValue,
            BooleanTrueLabel = GetLabel(trueOption?.Label, baseLanguage),
            BooleanFalseLabel = GetLabel(falseOption?.Label, baseLanguage)
        };
    }

    private static ColumnKind GetColumnKind(AttributeMetadata metadata)
    {
        return metadata switch
        {
            StringAttributeMetadata => ColumnKind.Text,
            MemoAttributeMetadata => ColumnKind.MultilineText,
            IntegerAttributeMetadata => ColumnKind.WholeNumber,
            DecimalAttributeMetadata => ColumnKind.Decimal,
            BooleanAttributeMetadata => ColumnKind.YesNo,
            _ => GetColumnKindFromTypeCode(metadata)
        };
    }

    private static ColumnKind GetColumnKindFromTypeCode(
        AttributeMetadata metadata
    )
    {
        var kind = metadata.AttributeType switch
        {
            AttributeTypeCode.String => ColumnKind.Text,
            AttributeTypeCode.Memo => ColumnKind.MultilineText,
            AttributeTypeCode.Integer => ColumnKind.WholeNumber,
            AttributeTypeCode.Decimal => ColumnKind.Decimal,
            AttributeTypeCode.Boolean => ColumnKind.YesNo,
            _ => ColumnKind.Unknown
        };
        if( kind != ColumnKind.Unknown )
        {
            return kind;
        }

        return metadata.AttributeTypeName?.ToString() switch
        {
            "StringType" or "String" => ColumnKind.Text,
            "MemoType" or "Memo" => ColumnKind.MultilineText,
            "IntegerType" or "Integer" => ColumnKind.WholeNumber,
            "DecimalType" or "Decimal" => ColumnKind.Decimal,
            "BooleanType" or "Boolean" => ColumnKind.YesNo,
            _ => ColumnKind.Unknown
        };
    }

    private static string GetLabel(Label? label, int? baseLanguage = null)
    {
        if( baseLanguage.HasValue )
        {
            return GetLabelForLanguage(label, baseLanguage.Value);
        }

        return label?.UserLocalizedLabel?.Label
            ?? label?.LocalizedLabels.FirstOrDefault()?.Label
            ?? string.Empty;
    }

    private static int? ParseBaseLanguage(string? baseLanguage)
    {
        if( string.IsNullOrWhiteSpace(baseLanguage) )
        {
            return null;
        }

        if( !int.TryParse(
            baseLanguage,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var language
        ) || language <= 0 )
        {
            throw new ArgumentException(
                "The organization base language is invalid.",
                nameof(baseLanguage)
            );
        }

        return language;
    }

    private static string? GetAttributeFormat(AttributeMetadata metadata)
    {
        return metadata switch
        {
            StringAttributeMetadata value => GetStringFormat(
                value.FormatName,
                value.Format
            ),
            MemoAttributeMetadata value => GetMemoFormat(
                value.FormatName,
                value.Format
            ),
            IntegerAttributeMetadata value => value.Format?.ToString(),
            _ => null
        };
    }

    private static string? GetStringFormat(
        StringFormatName? formatName,
        StringFormat? format
    )
    {
        if( formatName == StringFormatName.Text
            || format == StringFormat.Text )
        {
            return "Text";
        }

        if( formatName == StringFormatName.TextArea
            || format == StringFormat.TextArea )
        {
            return "TextArea";
        }

        return formatName?.ToString() ?? format?.ToString();
    }

    private static string? GetMemoFormat(
        MemoFormatName? formatName,
        StringFormat? format
    )
    {
        if( formatName == MemoFormatName.Text
            || format == StringFormat.Text )
        {
            return "Text";
        }

        if( formatName == MemoFormatName.TextArea
            || format == StringFormat.TextArea )
        {
            return "TextArea";
        }

        return formatName?.ToString() ?? format?.ToString();
    }

    private static string GetLabelForLanguage(Label? label, int language)
    {
        return label?.LocalizedLabels
            .FirstOrDefault(localized => localized.LanguageCode == language)
            ?.Label
            ?? (label?.UserLocalizedLabel?.LanguageCode == language
                ? label.UserLocalizedLabel.Label
                : null)
            ?? string.Empty;
    }

    private static string? GetRequirementLevel(AttributeRequiredLevel? level)
    {
        return level switch
        {
            AttributeRequiredLevel.None => RequirementLevels.Optional,
            AttributeRequiredLevel.Recommended => RequirementLevels.Recommended,
            AttributeRequiredLevel.ApplicationRequired => RequirementLevels.Required,
            _ => null
        };
    }
}
