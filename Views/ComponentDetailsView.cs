using dvtui.Models;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal static class ComponentDetailsView
{
    public static IRenderable Create(DataverseSolutionComponent component)
    {
        var rows = new List<IRenderable>
        {
            CreateOverview(component)
        };
        if( component.ResolutionError != null )
        {
            rows.Add(new Text(component.ResolutionError, new Style(Color.Yellow)));
        }

        rows.Add(new Text(
            "Detailed inspection of this component type is not available "
            + "in step 1.",
            TuiColors.SecondaryText
        ));
        return new Rows(rows);
    }

    public static IRenderable CreateMembership(
        DataverseSolutionComponent component
    )
    {
        var table = new Table()
            .Border(TableBorder.Minimal)
            .ShowRowSeparators()
            .HideHeaders()
            .AddColumn(new TableColumn("Property").NoWrap())
            .AddColumn("Value");

        AddRow(table, "Component type",
            SolutionComponentTypes.GetDisplayName(component.ComponentType));
        AddRow(table, "Object ID", component.ObjectId?.ToString());
        AddRow(table, "Component row ID", component.Id.ToString());
        AddRow(table, "Parent component", component.RootComponentId?.ToString());
        AddRow(table, "Root behavior",
            RootComponentBehaviors.GetDisplayName(component.RootComponentBehavior));
        return table;
    }

    private static IRenderable CreateOverview(
        DataverseSolutionComponent component
    )
    {
        return CreateMembership(component);
    }

    private static void AddRow(Table table, string label, string? value)
    {
        table.AddRow(
            new Text(label, TuiColors.SecondaryText),
            new Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
        );
    }
}
