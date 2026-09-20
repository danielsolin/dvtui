using System.Globalization;
using System.Text;
using dvtui.Models;

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Label = Microsoft.Xrm.Sdk.Label;
using UserLocalizedLabel = Microsoft.Xrm.Sdk.LocalizedLabel;

namespace dvtui.Services;

internal static class SchemaMetadataFactory
{
    internal static string BuildSchemaName(string prefix, string suffix)
    {
        return prefix + "_" + suffix;
    }

    internal static AttributeMetadata CreateAttributeMetadata(
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
                MaxLength = maxLength ?? ColumnDefaults.TextDefaultLength,
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
                MaxLength = maxLength ?? ColumnDefaults.MultilineDefaultLength,
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
                    : ColumnDefaults.WholeNumberDefaultMin,
                MaxValue = maxValue.HasValue
                    ? decimal.ToInt32(maxValue.Value)
                    : ColumnDefaults.WholeNumberDefaultMax,
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
                    : (decimal?)ColumnDefaults.DecimalDefaultMin,
                MaxValue = maxValue.HasValue
                    ? (decimal?)maxValue.Value
                    : (decimal?)ColumnDefaults.DecimalDefaultMax,
                Precision = precision ?? ColumnDefaults.DecimalDefaultPrecision,
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

    internal static AttributeMetadata CreateUpdateMetadata(
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

    internal static Label CreateLabel(string text, int language)
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

    internal static string BuildPublishXml(string tableLogicalName)
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

    internal static DataverseColumn CreateColumn(
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

    internal static string GetLabel(
        Label? label,
        int? baseLanguage = null
    )
    {
        if( baseLanguage.HasValue )
        {
            return GetLabelForLanguage(label, baseLanguage.Value);
        }

        return label?.UserLocalizedLabel?.Label
            ?? label?.LocalizedLabels.FirstOrDefault()?.Label
            ?? string.Empty;
    }

    internal static int? ParseBaseLanguage(string? baseLanguage)
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

    internal static string GetLabelForLanguage(Label? label, int language)
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
