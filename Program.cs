using System;
using System.Collections.Generic;
using System.Linq;
using Dvtui.Models;
using Dvtui.Services;
using Spectre.Console;

AnsiConsole.MarkupLine("[yellow]DVTUI - Dataverse Text User Interface[/]");

ConfigureWslBrowser();

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
    using var service = new DataverseService(url);

    AnsiConsole.MarkupLine(
        "[grey]Connecting; sign in in your browser if prompted...[/]"
    );

    AnsiConsole.Status().Start(
        "Connecting to Dataverse...",
        _ => service.Connect()
    );

    AnsiConsole.MarkupLine("[grey]Fetching metadata for 'account'...[/]");
    var columns = await service.GetColumnsAsync("account");

    if (columns.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No columns found or error.[/]");
        return;
    }

    AnsiConsole.MarkupLine(
        $"[green]Success! Found {columns.Count} columns.[/]"
    );
    AnsiConsole.Write(new Rule());

    RenderColumnPreview(columns);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
}

static void RenderColumnPreview(List<DataverseColumn> columns)
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

static void ConfigureWslBrowser()
{
    if (!OperatingSystem.IsLinux())
    {
        return;
    }

    var wslDistribution = Environment.GetEnvironmentVariable(
        "WSL_DISTRO_NAME"
    );

    if (string.IsNullOrWhiteSpace(wslDistribution))
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DE")))
    {
        Environment.SetEnvironmentVariable("DE", "wsl");
    }
}
