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
            CreateMembership(component)
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

        FormViewHelpers.AddRow(
            table,
            "Component type",
            SolutionComponentTypes.GetDisplayName(component.ComponentType)
        );
        FormViewHelpers.AddRow(table, "Object ID", component.ObjectId?.ToString());
        FormViewHelpers.AddRow(table, "Component row ID", component.Id.ToString());
        FormViewHelpers.AddRow(
            table,
            "Parent component",
            component.RootComponentId?.ToString()
        );
        FormViewHelpers.AddRow(
            table,
            "Root behavior",
            RootComponentBehaviors.GetDisplayName(component.RootComponentBehavior)
        );
        return table;
    }
}
