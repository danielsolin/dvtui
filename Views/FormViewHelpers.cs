using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal static class FormViewHelpers
{
    internal static void AddRow(Table table, string label, string? value)
    {
        table.AddRow(
            new Text(label, TuiColors.SecondaryText),
            new Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
        );
    }

    internal static string FormatBoolean(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            null => "Unknown"
        };
    }

    internal static string DisplayValue(string value)
    {
        return string.IsNullOrEmpty(value) ? "—" : value;
    }

    internal static string ToSchemaSuffix(
        string displayName,
        string fallback
    )
    {
        var chars = displayName
            .Where(char.IsLetterOrDigit)
            .ToArray();
        var suffix = new string(chars);
        if( suffix.Length == 0 )
        {
            return fallback;
        }

        return char.ToLowerInvariant(suffix[0]) + suffix[1..];
    }

    internal static string BuildPredictedName(
        string publisherPrefix,
        string suffix
    )
    {
        if( string.IsNullOrWhiteSpace(suffix) )
        {
            return "—";
        }

        return publisherPrefix + "_" + suffix;
    }

    internal static IRenderable RenderStatus(
        int width,
        string status,
        bool hasError,
        bool submitting
    )
    {
        var style = hasError
            ? Style.Parse("red")
            : submitting
                ? Style.Parse("yellow")
                : Style.Parse("green");
        return Progress.RenderStatus(width, "  " + status, style);
    }
}
