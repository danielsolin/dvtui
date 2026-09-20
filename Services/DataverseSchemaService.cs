using System.Globalization;
using dvtui.Models;

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
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

    private readonly IDataverseExecutor _client;
    private readonly string _environmentUrl;
    private readonly WebApiClient _webApiClient;

    public DataverseSchemaService(
        IDataverseExecutor client,
        string environmentUrl = ""
    )
    {
        _client = client;
        _environmentUrl = environmentUrl;
        _webApiClient = new WebApiClient(client);
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

    public async Task<DataverseColumn> GetColumnDefinitionAsync(
        string tableLogicalName,
        string columnLogicalName,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken,
        string? baseLanguage = null
    )
    {
        SessionLog.Info(
            "Schema.GetColumnDefinition",
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
        var result = SchemaMetadataFactory.CreateColumn(
            metadata,
            SchemaMetadataFactory.ParseBaseLanguage(baseLanguage)
        );
        SessionLog.Info(
            "Schema.GetColumnDefinition",
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

        SchemaValidation.ValidateTableCreation(request);
        var language = int.Parse(context.BaseLanguage, CultureInfo.InvariantCulture);
        var logicalName = SchemaMetadataFactory.BuildSchemaName(
            context.PublisherPrefix,
            request.SchemaSuffix
        );
        var primaryName = SchemaMetadataFactory.BuildSchemaName(
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
            DisplayName = SchemaMetadataFactory.CreateLabel(
                request.DisplayName,
                language
            ),
            DisplayCollectionName = SchemaMetadataFactory.CreateLabel(
                request.PluralDisplayName,
                language
            ),
            Description = request.Description == null
                ? null
                : SchemaMetadataFactory.CreateLabel(
                    request.Description,
                    language
                ),
            OwnershipType = request.IsUserOwned
                ? OwnershipTypes.UserOwned
                : OwnershipTypes.OrganizationOwned,
            IsActivity = false
        };

        var primaryColumn = new StringAttributeMetadata
        {
            SchemaName = primaryName,
            DisplayName = SchemaMetadataFactory.CreateLabel(
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

        SchemaValidation.ValidateColumnCreation(request);
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

        var schemaName = SchemaMetadataFactory.BuildSchemaName(
            context.PublisherPrefix,
            request.SchemaSuffix
        );
        await EnsureColumnNameAvailableAsync(
            request.TableLogicalName,
            schemaName,
            cancellationToken
        );
        var attribute = SchemaMetadataFactory.CreateAttributeMetadata(
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
        var fresh = SchemaMetadataFactory.CreateColumn(
            freshMetadata,
            SchemaMetadataFactory.ParseBaseLanguage(context.BaseLanguage)
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

        SchemaValidation.ValidateColumnUpdate(request, fresh);

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

        var metadata = SchemaMetadataFactory.CreateUpdateMetadata(
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
                await _webApiClient.UpdateRequirementLevelAsync(
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
                ComponentType = entity.GetOptionValue(
                    "dependentcomponenttype"
                ) ?? entity.GetOptionValue("componenttype"),
                ObjectId = entity.GetGuidValue(
                    "dependentcomponentobjectid"
                ) ?? entity.GetGuidValue("objectid"),
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

        var fresh = await GetColumnDefinitionAsync(
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

    internal static bool IsMetadataNotFound(Exception exception)
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

        var xml = SchemaMetadataFactory.BuildPublishXml(tableLogicalName);
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
        CancellationToken cancellationToken
    )
    {
        SessionLog.Info(
            "Schema.DeleteTable",
            "Started table=" + tableLogicalName
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
        var publisherId = solution.GetGuidValue("publisherid");
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
        var actual = SchemaMetadataFactory.CreateColumn(metadata);
        var language = int.Parse(baseLanguage, CultureInfo.InvariantCulture);
        var actualDisplayName = SchemaMetadataFactory.GetLabelForLanguage(
            metadata.DisplayName,
            language
        );
        var actualDescription = SchemaMetadataFactory.GetLabelForLanguage(
            metadata.Description,
            language
        );
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

}
