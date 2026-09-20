using dvtui.Services;
using dvtui.Views;

namespace dvtui;

internal static class Program
{
    private static void Main(string[] args)
    {
        var logPath = SessionLog.Start(args);
        SessionLog.Info(
            "Application",
            "Session log path=" + (logPath ?? "unavailable")
        );
        SessionLog.Info(
            "Application",
            "terminal " + TerminalSession.GetDiagnostics()
        );
        TerminalSession.ConfigureWslBrowser();
        DataverseConnectionManager? connection = null;
        Console.CancelKeyPress += TerminalSession.HandleCancelKeyPress;

        try
        {
            TerminalSession.RunApplication(
                () => connection = ApplicationFlow.Run(args)
            );
        }
        catch( Exception ex )
        {
            SessionLog.Exception("Application", ex, "Unhandled application error");
            TerminalSession.ShowError(ex);
        }
        finally
        {
            TerminalSession.Restore(false);
            Console.CancelKeyPress -= TerminalSession.HandleCancelKeyPress;
            try
            {
                connection?.Dispose();
            }
            catch( Exception ex )
            {
                SessionLog.Exception("Dataverse.Client", ex, "Dispose failed");
            }
            finally
            {
                SessionLog.Stop();
            }
        }
    }
}
