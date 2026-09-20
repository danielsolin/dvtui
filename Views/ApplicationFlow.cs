using dvtui.Services;

namespace dvtui.Views;

internal static class ApplicationFlow
{
    internal static DataverseConnectionManager? Run(string[] args)
    {
        DataverseConnectionManager? connection = null;
        SessionLog.Screen("StartupScreen", "enter");
        var connected = StartupScreen.Show(
            GetEnvironmentUrl(args),
            async (url, cancellationToken) =>
            {
                SessionLog.Info(
                    "UI.Connection",
                    "Connect requested environment=" + url
                );
                var candidate = new DataverseConnectionManager(url);
                var assigned = false;
                try
                {
                    await Task.Run(
                        candidate.Connect,
                        cancellationToken
                    );
                    cancellationToken.ThrowIfCancellationRequested();
                    connection = candidate;
                    assigned = true;
                    SessionLog.Info(
                        "UI.Connection",
                        "Connect callback completed environment=" + url
                    );
                }
                catch( Exception ex )
                {
                    SessionLog.Exception(
                        "UI.Connection",
                        ex,
                        "Connect callback failed environment=" + url
                    );
                    throw;
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
        SessionLog.Screen(
            "StartupScreen",
            "exit",
            "connected=" + connected
        );

        if( connected && connection != null )
        {
            new ApplicationScreenFlow(
                connection.QueryService,
                connection.SchemaService,
                connection.EnvironmentUrl
            ).Run();
        }

        return connection;
    }

    private static string GetEnvironmentUrl(string[] args)
    {
        const string environmentVariableName = "DVTUI_DEFAULT_ENV";
        return args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable(environmentVariableName)
                ?? string.Empty;
    }
}
