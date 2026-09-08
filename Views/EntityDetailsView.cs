using dvtui.Models;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal static class EntityDetailsView
{
    public static IRenderable Create(DataverseEntity entity)
    {
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("Property").NoWrap())
            .AddColumn("Value");

        AddRow(table, "Display name", entity.DisplayName);
        AddRow(table, "Logical name", entity.LogicalName);
        AddRow(table, "Schema name", entity.SchemaName);
        AddRow(table, "Plural name", entity.CollectionName);
        AddRow(table, "Entity set", entity.EntitySetName);
        AddRow(table, "Primary key", entity.PrimaryIdAttribute);
        AddRow(table, "Primary name", entity.PrimaryNameAttribute);
        AddRow(table, "Ownership", entity.OwnershipType);
        AddRow(table, "Object type code", entity.ObjectTypeCode?.ToString());
        AddRow(table, "Custom table", FormatBoolean(entity.IsCustom));
        AddRow(table, "Customizable", FormatBoolean(entity.IsCustomizable));
        AddRow(table, "Managed", FormatBoolean(entity.IsManaged));
        AddRow(table, "Activity", FormatBoolean(entity.IsActivity));
        AddRow(table, "Description", entity.Description);
        return table;
    }

    private static void AddRow(Table table, string label, string? value)
    {
        table.AddRow(
            new Text(label, new Style(Color.Cyan1)),
            new Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
        );
    }

    private static string FormatBoolean(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            null => "Unknown"
        };
    }
}
