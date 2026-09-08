using dvtui.Models;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal static class EntityDetailsView
{
    public static IRenderable Create(
        DataverseEntity entity,
        IReadOnlyList<DataverseField>? fields
    )
    {
        var content = new List<IRenderable>
        {
            CreatePropertiesTable(entity)
        };
        if( fields != null )
        {
            content.Add(CreateFieldsTable(entity, fields));
        }

        return new Rows(content);
    }

    private static IRenderable CreatePropertiesTable(DataverseEntity entity)
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

    private static IRenderable CreateFieldsTable(
        DataverseEntity entity,
        IReadOnlyList<DataverseField> fields
    )
    {
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("Field").NoWrap())
            .AddColumn(new TableColumn("Type").NoWrap())
            .AddColumn("Description");

        if( fields.Count == 0 )
        {
            table.AddRow(new Text("No fields found."));
            return table;
        }

        foreach( var field in fields )
        {
            table.AddRow(
                FieldLabel(entity, field),
                new Text(field.Type),
                new Text(field.Description)
            );
        }

        return table;
    }

    private static IRenderable FieldLabel(
        DataverseEntity entity,
        DataverseField field
    )
    {
        var name = field.SchemaName;

        if( name == entity.PrimaryIdAttribute )
        {
            return new Text(
                $"{name} (primary key)",
                new Style(Color.DarkOrange3)
            );
        }

        if( name == entity.PrimaryNameAttribute )
        {
            return new Text(
                $"{name} (primary name)",
                new Style(Color.DarkOrange3)
            );
        }

        return new Text(name);
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
