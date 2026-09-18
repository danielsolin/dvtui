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
                async (url, cancellationToken) =>
                {
                    var candidate = new DataverseService(url);
                    var assigned = false;
                    try
                    {
                        await Task.Run(
                            candidate.Connect,
                            cancellationToken
                        );
                        cancellationToken.ThrowIfCancellationRequested();
                        service = candidate;
                        assigned = true;
                    }
                    finally
                    {
                        if( !assigned )
                        {
                            candidate.Dispose();
                        }
                    }
                }
            );

            if( !connected || service == null )
            {
                return;
            }

            RunSolutionLoop(service);
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

    private static void RunSolutionLoop(DataverseService service)
    {
        while( true )
        {
            var solution = SolutionSelectionScreen.Show(
                service.GetSolutionsAsync
            );
            if( solution == null )
            {
                return;
            }

            var result = SolutionBrowserScreen.Show(
                solution,
                service.GetSolutionComponentsAsync,
                service.GetEntityAsync
            );
            if( result == SolutionBrowserResult.Quit )
            {
                return;
            }
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
