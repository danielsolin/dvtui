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
            .Border(TableBorder.Minimal)
            .ShowRowSeparators()
            .HideHeaders()
            .AddColumn(new TableColumn("Property").NoWrap())
            .AddColumn("Value");

        FormViewHelpers.AddRow(table, "Display name", entity.DisplayName);
        FormViewHelpers.AddRow(table, "Logical name", entity.LogicalName);
        FormViewHelpers.AddRow(table, "Schema name", entity.SchemaName);
        FormViewHelpers.AddRow(table, "Plural name", entity.CollectionName);
        FormViewHelpers.AddRow(table, "Entity set", entity.EntitySetName);
        FormViewHelpers.AddRow(table, "Primary key", entity.PrimaryIdAttribute);
        FormViewHelpers.AddRow(table, "Primary name", entity.PrimaryNameAttribute);
        FormViewHelpers.AddRow(table, "Ownership", entity.OwnershipType);
        FormViewHelpers.AddRow(
            table,
            "Object type code",
            entity.ObjectTypeCode?.ToString()
        );
        FormViewHelpers.AddRow(
            table,
            "Custom table",
            FormViewHelpers.FormatBoolean(entity.IsCustom)
        );
        FormViewHelpers.AddRow(
            table,
            "Customizable",
            FormViewHelpers.FormatBoolean(entity.IsCustomizable)
        );
        FormViewHelpers.AddRow(
            table,
            "Can create columns",
            FormViewHelpers.FormatBoolean(entity.CanCreateAttributes)
        );
        FormViewHelpers.AddRow(
            table,
            "Managed",
            FormViewHelpers.FormatBoolean(entity.IsManaged)
        );
        FormViewHelpers.AddRow(
            table,
            "Activity",
            FormViewHelpers.FormatBoolean(entity.IsActivity)
        );
        FormViewHelpers.AddRow(table, "Description", entity.Description);
        return table;
    }

    private static IRenderable CreateFieldsTable(
        DataverseEntity entity,
        IReadOnlyList<DataverseField> fields
    )
    {
        var table = new Table()
            .Border(TableBorder.Minimal)
            .ShowRowSeparators()
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

        if( MatchesAttribute(field, entity.PrimaryIdAttribute) )
        {
            return new Text(
                $"{name} (primary key)",
                new Style(Color.DarkOrange3)
            );
        }

        if( MatchesAttribute(field, entity.PrimaryNameAttribute) )
        {
            return new Text(
                $"{name} (primary name)",
                new Style(Color.DarkOrange3)
            );
        }

        return new Text(name);
    }

    private static bool MatchesAttribute(
        DataverseField field,
        string attributeName
    )
    {
        if( string.IsNullOrWhiteSpace(attributeName) )
        {
            return false;
        }

        return string.Equals(
                   field.LogicalName,
                   attributeName,
                   StringComparison.OrdinalIgnoreCase
               )
            || string.Equals(
                   field.SchemaName,
                   attributeName,
                   StringComparison.OrdinalIgnoreCase
               );
    }

}
