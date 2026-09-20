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
        DataverseService? service = null;
        Console.CancelKeyPress += TerminalSession.HandleCancelKeyPress;

        try
        {
            TerminalSession.RunApplication(
                () => service = ApplicationFlow.Run(args)
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
                service?.Dispose();
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
