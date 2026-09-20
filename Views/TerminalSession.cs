using dvtui.Services;

using Spectre.Console;

namespace dvtui.Views;

internal static class TerminalSession
{
    internal static void ConfigureWslBrowser()
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
            "DE"
        )) )
        {
            Environment.SetEnvironmentVariable("DE", "wsl");
        }
    }

    internal static string GetDiagnostics()
    {
        return "inputRedirected=" + Console.IsInputRedirected
            + " outputRedirected=" + Console.IsOutputRedirected
            + " size=" + GetSize()
            + " controlCAsInput=" + Console.TreatControlCAsInput;
    }

    internal static void RunApplication(Action action)
    {
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            action();
            return;
        }

        var previousControlCMode = Console.TreatControlCAsInput;
        AnsiConsole.AlternateScreen(() =>
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Clear();
            try
            {
                action();
            }
            finally
            {
                Restore(previousControlCMode);
            }
        });
    }

    internal static void HandleCancelKeyPress(
        object? sender,
        ConsoleCancelEventArgs args
    )
    {
        SessionLog.Warning(
            "UI.Signal",
            "Console.CancelKeyPress received type=" + args.SpecialKey
        );
        Restore(false);
    }

    internal static void Restore(bool treatControlCAsInput)
    {
        try
        {
            AnsiConsole.Cursor.Show();
        }
        finally
        {
            Console.TreatControlCAsInput = treatControlCAsInput;
        }
    }

    internal static void ShowError(Exception exception)
    {
        AnsiConsole.MarkupLine(
            $"[red]Error: {Markup.Escape(exception.Message)}[/]"
        );
    }

    private static string GetSize()
    {
        try
        {
            return Console.WindowWidth + "x" + Console.WindowHeight;
        }
        catch( IOException )
        {
            return "unknown";
        }
        catch( PlatformNotSupportedException )
        {
            return "unsupported";
        }
    }
}
