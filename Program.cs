using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dvtui.Services;
using Dvtui.Models;
using Spectre.Console;

AnsiConsole.MarkupLine("[yellow]DVTUI - Dataverse Text User Interface[/]");

string url;
if (args.Length > 0)
{
    url = args[0];
}
else
{
    url = AnsiConsole.Ask<string>("Enter Dataverse URL:");
}

try
{
    var service = new DataverseService(url);

    AnsiConsole.MarkupLine("[grey]Fetching metadata for 'account'...[/]");
    var columns = await service.GetColumnsAsync(url, "account");
    
    if (columns == null || columns.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No columns found or error.[/]");
        return;
    }

    AnsiConsole.MarkupLine($"[green]Success! Found {columns.Count} columns.[/]");
    AnsiConsole.Write(new Rule());

    await RenderColumnPreview(columns);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
}

static async Task RenderColumnPreview(List<DataverseColumn> columns)
{
    var table = new Table();
    table.AddColumn("Logical Name");
    table.AddColumn("Display Name");

    foreach (var col in columns.Take(15))
    {
        table.AddRow(col.Name, col.DisplayName);
    }

    AnsiConsole.Write(table);
    if (columns.Count > 15)
    {
        AnsiConsole.MarkupLine("[grey]... and more[/]");
    }
}
