using System.Globalization;
using dvtui.Models;

namespace dvtui.Views;

internal sealed record ColumnCreateValues(
    string DisplayName,
    string SchemaSuffix,
    string Description,
    ColumnKind Kind,
    string MaxLength,
    string Minimum,
    string Maximum,
    string Precision,
    string BooleanTrueLabel,
    string BooleanFalseLabel
);

internal static class ColumnEditorValidator
{
    internal static string? ValidateCreate(
        ColumnCreateValues values,
        out int? maxLength,
        out decimal? minimum,
        out decimal? maximum,
        out int? precision
    )
    {
        maxLength = null;
        minimum = null;
        maximum = null;
        precision = null;
        if( string.IsNullOrWhiteSpace(values.DisplayName) )
        {
            return "Display name is required.";
        }

        if( values.DisplayName.Length > ColumnDefaults.DisplayNameAllowedMaxLength )
        {
            return $"Display name must be "
                + $"{ColumnDefaults.DisplayNameAllowedMaxLength} characters or fewer.";
        }

        if( string.IsNullOrWhiteSpace(values.SchemaSuffix) )
        {
            return "Schema suffix is required.";
        }

        if( values.SchemaSuffix.Length > ColumnDefaults.SchemaNameAllowedMaxLength )
        {
            return $"Schema suffix must be "
                + $"{ColumnDefaults.SchemaNameAllowedMaxLength} characters or fewer.";
        }

        if( values.Description.Length > ColumnDefaults.DescriptionAllowedMaxLength )
        {
            return $"Description must be "
                + $"{ColumnDefaults.DescriptionAllowedMaxLength} characters or fewer.";
        }

        if( values.Kind == ColumnKind.Text
            || values.Kind == ColumnKind.MultilineText )
        {
            var maximumLength = values.Kind == ColumnKind.Text
                ? ColumnDefaults.TextAllowedMaxLength
                : ColumnDefaults.MultilineAllowedMaxLength;
            if( !int.TryParse(
                values.MaxLength,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedLength
            )
                || parsedLength < 1
                || parsedLength > maximumLength )
            {
                return "Maximum length is outside the supported range.";
            }

            maxLength = parsedLength;
        }

        if( values.Kind == ColumnKind.WholeNumber
            || values.Kind == ColumnKind.Decimal )
        {
            var error = ParseDecimal(
                values.Minimum,
                "Minimum value",
                out minimum
            );
            if( error != null )
            {
                return error;
            }

            error = ParseDecimal(
                values.Maximum,
                "Maximum value",
                out maximum
            );
            if( error != null )
            {
                return error;
            }

            if( minimum > maximum )
            {
                return "Minimum value must not exceed maximum value.";
            }

            var lowerBound = values.Kind == ColumnKind.WholeNumber
                ? ColumnDefaults.WholeNumberAllowedMin
                : ColumnDefaults.DecimalAllowedMin;
            var upperBound = values.Kind == ColumnKind.WholeNumber
                ? ColumnDefaults.WholeNumberAllowedMax
                : ColumnDefaults.DecimalAllowedMax;
            if( (minimum.HasValue && minimum.Value < lowerBound)
                || (maximum.HasValue && maximum.Value > upperBound) )
            {
                return "Numeric bounds are outside the supported range.";
            }

            if( values.Kind == ColumnKind.WholeNumber
                && ((minimum.HasValue
                    && minimum.Value != decimal.Truncate(minimum.Value))
                    || (maximum.HasValue
                        && maximum.Value != decimal.Truncate(maximum.Value))) )
            {
                return "Whole number bounds must be whole numbers.";
            }
        }

        if( values.Kind == ColumnKind.Decimal )
        {
            if( !int.TryParse(
                values.Precision,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedPrecision
            )
                || parsedPrecision < 0
                || parsedPrecision > ColumnDefaults.DecimalAllowedMaxPrecision )
            {
                return "Precision must be between 0 and 10.";
            }

            precision = parsedPrecision;
        }

        if( values.Kind == ColumnKind.YesNo
            && (string.IsNullOrWhiteSpace(values.BooleanTrueLabel)
                || string.IsNullOrWhiteSpace(values.BooleanFalseLabel)) )
        {
            return "Yes and No labels are required.";
        }

        return null;
    }

    internal static bool IsAllowedCharacter(
        FormInputKind kind,
        char value
    )
    {
        if( kind == FormInputKind.Decimal )
        {
            return char.IsDigit(value) || value == '-' || value == '.';
        }

        if( kind == FormInputKind.Integer )
        {
            return char.IsDigit(value) || value == '-';
        }

        return true;
    }

    private static string? ParseDecimal(
        string value,
        string field,
        out decimal? result
    )
    {
        if( string.IsNullOrWhiteSpace(value) )
        {
            result = null;
            return null;
        }

        if( decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed
        ) )
        {
            result = parsed;
            return null;
        }

        result = null;
        return field + " must use a valid number with '.' as decimal separator.";
    }
}
