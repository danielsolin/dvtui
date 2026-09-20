using dvtui.Models;

using Microsoft.Xrm.Sdk.Metadata;

namespace dvtui.Services;

internal static class SchemaValidation
{
    private const int MinTextLength = 1;
    private const int MaxTextLength = ColumnDefaults.TextAllowedMaxLength;
    private const int MinMemoLength = 1;
    private const int MaxMemoLength = ColumnDefaults.MultilineAllowedMaxLength;
    private const int MinPrecision = 0;
    private const int MaxPrecision = DecimalAttributeMetadata.MaxSupportedPrecision;
    private const int IntMin = ColumnDefaults.WholeNumberAllowedMin;
    private const int IntMax = ColumnDefaults.WholeNumberAllowedMax;
    private const decimal DecMin = ColumnDefaults.DecimalAllowedMin;
    private const decimal DecMax = ColumnDefaults.DecimalAllowedMax;

    public static void ValidateTableCreation(CreateTableRequest request)
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
            && request.Description.Length > ColumnDefaults.DescriptionAllowedMaxLength )
        {
            throw new InvalidOperationException(
                "Description is too long."
            );
        }
    }

    public static void ValidateColumnCreation(CreateColumnRequest request)
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
            && request.Description.Length > ColumnDefaults.DescriptionAllowedMaxLength )
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

    public static void ValidateColumnUpdate(
        UpdateColumnRequest request,
        DataverseColumn current
    )
    {
        if( request.SetDisplayName )
        {
            RequireText(request.DisplayName, "Display name");
        }

        if( request.SetDescription
            && request.Description.Length > ColumnDefaults.DescriptionAllowedMaxLength )
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

        if( value.Length > ColumnDefaults.DisplayNameAllowedMaxLength )
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

        if( suffix.Length > ColumnDefaults.SchemaNameAllowedMaxLength )
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
}
