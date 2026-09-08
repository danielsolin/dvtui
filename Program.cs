using dvtui.Services;
using dvtui.Views;

using Spectre.Console;

namespace dvtui;

internal static class Program
{
    private static void Main(string[] args)
    {
        ConfigureWslBrowser();
        DataverseService? service = null;

        try
        {
            var connected = StartupScreen.Show(
                GetEnvUrl(args),
                url =>
                {
                    var candidate = new DataverseService(url);
                    try
                    {
                        candidate.Connect();
                        service = candidate;
                    }
                    catch
                    {
                        candidate.Dispose();
                        throw;
                    }
                }
            );

            if( !connected || service == null )
            {
                return;
            }

            EntityBrowserScreen.Show(service.GetEntitiesAsync);
        }
        catch( Exception ex )
        {
            AnsiConsole.MarkupLine(
                $"[red]Error: {Markup.Escape(ex.Message)}[/]"
            );
        }
        finally
        {
            service?.Dispose();
        }
    }

    private static void ConfigureWslBrowser()
    {
        if( !OperatingSystem.IsLinux() )
        {
            return;
        }

        var wslDistribution =
            Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");

        if( string.IsNullOrWhiteSpace(wslDistribution) )
        {
            return;
        }

        if( string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            "DE")) )
        {
            Environment.SetEnvironmentVariable("DE", "wsl");
        }
    }

    private static string GetEnvUrl(string[] args)
    {
        const string environmentVariableName = "DVTUI_DEFAULT_ENV";
        return args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable(environmentVariableName)
                ?? string.Empty;
    }
}
