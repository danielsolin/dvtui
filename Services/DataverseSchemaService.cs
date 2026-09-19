using System.Globalization;
using System.Text;
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
    private const int MaxTextLength = 8000;
    private const int MinMemoLength = 1;
    private const int MaxMemoLength = 32000;
    private const int MinPrecision = 0;
    private const int MaxPrecision = 23;
    private const int IntMin = int.MinValue;
    private const int IntMax = int.MaxValue;
    private const decimal DecMin = -1000000000000m;
    private const decimal DecMax = 1000000000000m;

    private readonly IDataverseExecutor _client;

    public DataverseSchemaService(IDataverseExecutor client)
    {
        _client = client;
    }

    public async Task<SolutionWriteContext> LoadWriteContextAsync(
        DataverseSolution solution,
        CancellationToken cancellationToken
    )
    {
        if( solution.IsManaged == true )
        {
            throw new InvalidOperationException(
                ColumnCapabilityPolicy.ReasonManagedSolution
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
        var baseLanguage = org.GetAttributeValue<int>("languagecode")
            .ToString(CultureInfo.InvariantCulture);

        return new SolutionWriteContext
        {
            SolutionId = solution.Id,
            SolutionUniqueName = solution.UniqueName,
            IsManaged = solution.IsManaged ?? false,
            PublisherId = publisherId,
            PublisherPrefix = prefix,
            BaseLanguage = baseLanguage,
            EnvironmentUrl = solution.UniqueName
        };
    }

    public async Task<DataverseColumn> LoadColumnDefinitionAsync(
        string tableLogicalName,
        string columnLogicalName,
        bool retrieveAsIfPublished,
        CancellationToken cancellationToken
    )
    {
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
        return CreateColumn(response.AttributeMetadata);
    }

    public async Task<Guid> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken
    )
    {
        var context = request.Context;
        if( context.IsManaged )
        {
            throw new InvalidOperationException(
                ColumnCapabilityPolicy.ReasonManagedSolution
            );
        }

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
            IsActivity = false,
            IsMappable = new BooleanManagedProperty(false),
            IsReadingPaneEnabled = false
        };
        SetMetadataFlag(entityMetadata, "IsCustomEntity", true);
        SetMetadataFlag(entityMetadata, "IsIntersect", false);
        SetMetadataFlag(entityMetadata, "PrimaryIdAttribute", "id");
        SetMetadataFlag(entityMetadata, "PrimaryNameAttribute", primaryName);

        var primaryColumn = new StringAttributeMetadata
        {
            SchemaName = primaryName,
            DisplayName = CreateLabel(
                request.PrimaryNameDisplayName,
                language
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
        return response.EntityId;
    }

    public async Task<Guid> CreateColumnAsync(
        CreateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        var context = request.Context;
        if( context.IsManaged )
        {
            throw new InvalidOperationException(
                ColumnCapabilityPolicy.ReasonManagedSolution
            );
        }

        ValidateColumnCreation(request);
        var schemaName = BuildSchemaName(
            context.PublisherPrefix,
            request.SchemaSuffix
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
            request.RequirementLevel
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
        return response.AttributeId;
    }

    public async Task<ColumnUpdateResult> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken
    )
    {
        var context = request.Context;
        if( context.IsManaged )
        {
            throw new InvalidOperationException(
                ColumnCapabilityPolicy.ReasonManagedSolution
            );
        }

        var fresh = await LoadColumnDefinitionAsync(
            request.TableLogicalName,
            request.ColumnLogicalName,
            retrieveAsIfPublished: false,
            cancellationToken
        );
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

        var changed = false;
        var displayName = fresh.DisplayName;
        var description = fresh.Description;
        var newMaxLength = fresh.MaxLength;
        var requirement = fresh.RequirementLevel;

        if( request.SetDisplayName )
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
            if( !capability.CanEditDescription )
            {
                throw new InvalidOperationException(
                    capability.EditDescriptionReason
                    ?? ColumnCapabilityPolicy.ReasonNotCustomizableSettings
                );
            }

            description = request.Description;
            changed = true;
        }

        if( request.NewMaxLength.HasValue )
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

        if( request.SetRequirementLevel )
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
            return new ColumnUpdateResult
            {
                MetadataId = fresh.MetadataId,
                Changed = false
            };
        }

        var metadata = CreateUpdateMetadata(
            fresh,
            displayName,
            description,
            newMaxLength,
            requirement,
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
        return new ColumnUpdateResult
        {
            MetadataId = fresh.MetadataId,
            Changed = true
        };
    }

    public async Task<IReadOnlyList<DependencyInfo>>
        GetColumnDeleteDependenciesAsync(
        Guid columnMetadataId,
        CancellationToken cancellationToken
    )
    {
        var request = new RetrieveDependenciesForDeleteRequest
        {
            ComponentType = ComponentTypeAttribute,
            ObjectId = columnMetadataId
        };

        var response = (RetrieveDependenciesForDeleteResponse)
            await _client.ExecuteAsync(request, cancellationToken);
        var dependencies = new List<DependencyInfo>();
        if( response.EntityCollection == null )
        {
            return dependencies;
        }

        foreach( var entity in response.EntityCollection.Entities )
        {
            dependencies.Add(new DependencyInfo
            {
                ComponentType = GetOptionValue(entity, "componenttype"),
                ObjectId = entity.Contains("objectid")
                    ? entity.GetAttributeValue<Guid>("objectid")
                    : null,
                Name = entity.GetAttributeValue<string>("name")
                    ?? entity.GetAttributeValue<string>("friendlyname")
                    ?? string.Empty
            });
        }

        return dependencies;
    }

    public async Task DeleteColumnAsync(
        string tableLogicalName,
        string columnLogicalName,
        Guid expectedMetadataId,
        CancellationToken cancellationToken
    )
    {
        var fresh = await LoadColumnDefinitionAsync(
            tableLogicalName,
            columnLogicalName,
            retrieveAsIfPublished: false,
            cancellationToken
        );
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

        var request = new DeleteAttributeRequest
        {
            EntityLogicalName = tableLogicalName,
            LogicalName = columnLogicalName
        };

        await _client.ExecuteAsync(request, cancellationToken);
    }

    public async Task PublishTableAsync(
        string tableLogicalName,
        CancellationToken cancellationToken
    )
    {
        var xml = BuildPublishXml(tableLogicalName);
        var request = new PublishXmlRequest
        {
            ParameterXml = xml
        };

        await _client.ExecuteAsync(request, cancellationToken);
    }

    public async Task DeleteTableAsync(
        string tableLogicalName,
        Guid tableMetadataId,
        CancellationToken cancellationToken
    )
    {
        var request = new DeleteEntityRequest
        {
            LogicalName = tableLogicalName
        };

        await _client.ExecuteAsync(request, cancellationToken);
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
        RequireText(request.DisplayName, "Display name");
        RequireText(request.SchemaSuffix, "Schema suffix");
        ValidateSchemaSuffix(request.SchemaSuffix);
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
                break;
            case ColumnKind.Decimal:
                ValidateNumericBounds(
                    request.MinValue,
                    request.MaxValue,
                    (long)DecMin,
                    (long)DecMax
                );
                ValidatePrecision(request.Precision);
                break;
            case ColumnKind.YesNo:
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported column type."
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
        long? min,
        long? max,
        long lowerBound,
        long upperBound
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

    private static AttributeMetadata CreateAttributeMetadata(
        ColumnKind kind,
        string schemaName,
        string displayName,
        string? description,
        string baseLanguage,
        int? maxLength,
        long? minValue,
        long? maxValue,
        int? precision,
        string requirementLevel
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
                MaxLength = maxLength ?? ColumnDefaults.TextLength,
                RequiredLevel = requirement
            },
            ColumnKind.MultilineText => new MemoAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = schemaName,
                DisplayName = label,
                Description = descLabel,
                MaxLength = maxLength ?? ColumnDefaults.MultilineLength,
                RequiredLevel = requirement
            },
            ColumnKind.WholeNumber => new IntegerAttributeMetadata
            {
                SchemaName = schemaName,
                LogicalName = schemaName,
                DisplayName = label,
                Description = descLabel,
                MinValue = minValue.HasValue
                    ? (int)minValue.Value
                    : ColumnDefaults.WholeNumberMin,
                MaxValue = maxValue.HasValue
                    ? (int)maxValue.Value
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
                DefaultValue = false,
                RequiredLevel = requirement
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static AttributeMetadata CreateUpdateMetadata(
        DataverseColumn current,
        string displayName,
        string description,
        int? newMaxLength,
        string? requirementLevel,
        string baseLanguage
    )
    {
        var language = int.Parse(baseLanguage, CultureInfo.InvariantCulture);
        var metadata = CreateAttributeMetadata(
            current.Kind,
            current.SchemaName,
            current.DisplayName,
            current.Description,
            baseLanguage,
            current.MaxLength,
            current.MinValue,
            current.MaxValue,
            current.Precision,
            current.RequirementLevel ?? RequirementLevels.Optional
        );

        metadata.DisplayName = CreateLabel(displayName, language);
        metadata.Description = string.IsNullOrEmpty(description)
            ? null
            : CreateLabel(description, language);
        metadata.RequiredLevel = ParseRequirementLevel(requirementLevel);

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

    private static void SetMetadataFlag(
        EntityMetadata metadata,
        string propertyName,
        object value
    )
    {
        var property = metadata.GetType().GetProperty(propertyName);
        property?.SetValue(metadata, value);
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
        var name = System.Security.SecurityElement.Escape(tableLogicalName);
        var builder = new StringBuilder();
        builder.Append("<ImportExportXml>");
        builder.Append("<Entities>");
        builder.Append($"<Entity Name=\"{name}\">");
        builder.Append("<Fields><Field Name=\"*\" /></Fields>");
        builder.Append("<Forms /><Views />");
        builder.Append("</Entity>");
        builder.Append("</Entities>");
        builder.Append("</ImportExportXml>");
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

    public static DataverseColumn CreateColumn(AttributeMetadata metadata)
    {
        var kind = metadata switch
        {
            StringAttributeMetadata => ColumnKind.Text,
            MemoAttributeMetadata => ColumnKind.MultilineText,
            IntegerAttributeMetadata => ColumnKind.WholeNumber,
            DecimalAttributeMetadata => ColumnKind.Decimal,
            BooleanAttributeMetadata => ColumnKind.YesNo,
            _ => ColumnKind.Unknown
        };

        int? maxLength = null;
        if( metadata is StringAttributeMetadata stringMeta )
        {
            maxLength = stringMeta.MaxLength;
        }
        else if( metadata is MemoAttributeMetadata memoMeta )
        {
            maxLength = memoMeta.MaxLength;
        }

        int? minValue = null;
        int? maxValue = null;
        int? precision = null;
        if( metadata is IntegerAttributeMetadata intMeta )
        {
            minValue = intMeta.MinValue;
            maxValue = intMeta.MaxValue;
        }
        else if( metadata is DecimalAttributeMetadata decMeta )
        {
            minValue = decMeta.MinValue.HasValue
                ? (int?)decimal.ToInt32(decMeta.MinValue.Value)
                : null;
            maxValue = decMeta.MaxValue.HasValue
                ? (int?)decimal.ToInt32(decMeta.MaxValue.Value)
                : null;
            precision = decMeta.Precision;
        }

        return new DataverseColumn
        {
            MetadataId = metadata.MetadataId ?? Guid.Empty,
            LogicalName = metadata.LogicalName ?? string.Empty,
            SchemaName = metadata.SchemaName ?? string.Empty,
            DisplayName = GetLabel(metadata.DisplayName),
            Description = GetLabel(metadata.Description),
            Kind = kind,
            MaxLength = maxLength,
            MinValue = minValue,
            MaxValue = maxValue,
            Precision = precision,
            IsCustom = metadata.IsCustomAttribute,
            IsManaged = metadata.IsManaged,
            IsPrimaryId = metadata.IsPrimaryId,
            IsPrimaryName = metadata.IsPrimaryName,
            IsCustomizable = metadata.IsCustomizable?.Value,
            IsRenameable = metadata.IsRenameable?.Value,
            CanModifyAdditionalSettings =
                metadata.CanModifyAdditionalSettings?.Value,
            RequirementLevel = metadata.RequiredLevel?.Value.ToString(),
            AttributeTypeCode = metadata.AttributeType?.ToString(),
            AttributeOf = metadata.AttributeOf,
            IsLogical = metadata.IsLogical,
            SourceType = metadata.SourceType,
            AutoNumberFormat = metadata.AutoNumberFormat
        };
    }

    private static string GetLabel(Label? label)
    {
        return label?.UserLocalizedLabel?.Label
            ?? label?.LocalizedLabels.FirstOrDefault()?.Label
            ?? string.Empty;
    }
}
