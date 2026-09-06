using Dvtui.Models;
using Dvtui.Services;

using Spectre.Console;

ConfigureWslBrowser();
AnsiConsole.MarkupLine("[yellow]DVTUI - Dataverse Text User Interface[/]");

var url = GetEnvUrl(args);

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

   AnsiConsole.WriteLine("Press any key to continue");
   AnsiConsole.Console.Input.ReadKey(false);
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

   foreach (var col in columns)
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

static string GetEnvUrl(string[] args)
{
   string envVarName = "DVTUI_DEFAULT_ENV";
   string url = "";

   if (args.Length > 0)
   {
      url = args[0];
   }
   else if (!string.IsNullOrWhiteSpace(
       Environment.GetEnvironmentVariable(envVarName)
   ))
   {
      url = Environment.GetEnvironmentVariable(envVarName)!;
   }
   else
   {
      url = AnsiConsole.Ask<string>("Enter Dataverse URL:");
   }

   return url;
}