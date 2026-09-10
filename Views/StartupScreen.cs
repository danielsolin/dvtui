using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class StartupScreen
{
    private const string ApplicationName = "dvtui";
    private const int RefreshIntervalMilliseconds = 80;
    private const int ConnectionRefreshIntervalMilliseconds = 250;
    private const int ConnectionShutdownTimeoutMilliseconds = 2000;
    private const int FormWidth = 60;
    private readonly UrlTextBox _url;
    private string _message = "Enter: connect | Q: quit";
    private Task? _connection;
    private CancellationTokenSource? _connectionCancellation;
    private int _frame;

    private StartupScreen(string initialUrl)
    {
        _url = new UrlTextBox(initialUrl);
    }

    public static bool Show(
        string initialUrl,
        Func<string, CancellationToken, Task> connect
    )
    {
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            var url = string.IsNullOrWhiteSpace(initialUrl)
                ? AnsiConsole.Ask<string>("Enter Dataverse URL:")
                : initialUrl;
            connect(url, CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }

        var screen = new StartupScreen(initialUrl);
        var connected = false;
        var previousControlCMode = Console.TreatControlCAsInput;

        try
        {
            AnsiConsole.AlternateScreen(() =>
            {
                Console.TreatControlCAsInput = true;
                AnsiConsole.Clear();

                try
                {
                    connected = AnsiConsole.Live(screen.Render())
                        .Start(context => screen.Run(context, connect));
                }
                finally
                {
                    AnsiConsole.Cursor.Show();
                    Console.TreatControlCAsInput = previousControlCMode;
                }
            });
        }
        finally
        {
            screen.StopConnection();
        }

        return connected;
    }

    private bool Run(
        LiveDisplayContext context,
        Func<string, CancellationToken, Task> connect
    )
    {
        var lastSize = (Width: 0, Height: 0);
        while( true )
        {
            var refresh = false;
            if( _connection?.IsCompleted == true )
            {
                try
                {
                    _connection.GetAwaiter().GetResult();
                    return true;
                }
                catch( Exception ex )
                {
                    _message = $"Connection failed: {ex.Message}";
                    ReleaseConnection();
                    refresh = true;
                }
            }

            while( Console.KeyAvailable )
            {
                var key = Console.ReadKey(intercept: true);
                if( key.Key == ConsoleKey.Q || key.KeyChar == '\u0003' )
                {
                    _connectionCancellation?.Cancel();
                    return false;
                }

                if( _connection != null )
                {
                    continue;
                }

                if( key.Key == ConsoleKey.Enter )
                {
                    StartConnection(connect);
                }
                else
                {
                    _url.HandleKey(key);
                }

                refresh = true;
            }

            var size = (AnsiConsole.Profile.Width, AnsiConsole.Profile.Height);
            if( refresh || _connection != null || size != lastSize )
            {
                context.UpdateTarget(Render());
                lastSize = size;
            }

            var delay = _connection == null
                ? RefreshIntervalMilliseconds
                : ConnectionRefreshIntervalMilliseconds;
            Thread.Sleep(delay);
        }
    }

    private void StartConnection(Func<string, CancellationToken, Task> connect)
    {
        var url = _url.Value.Trim();
        if( !url.Contains("://", StringComparison.Ordinal) )
        {
            url = $"https://{url}";
        }

        if( !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) )
        {
            _message = "Enter a valid HTTPS Dataverse URL.";
            return;
        }

        var connectionCancellation = new CancellationTokenSource();
        _connectionCancellation = connectionCancellation;
        _connection = Task.Run(
            () => connect(uri.AbsoluteUri, connectionCancellation.Token),
            connectionCancellation.Token
        );
    }

    private void ReleaseConnection()
    {
        _connection = null;
        _connectionCancellation?.Dispose();
        _connectionCancellation = null;
    }

    private void StopConnection()
    {
        var connection = _connection;
        _connectionCancellation?.Cancel();
        if( connection == null )
        {
            return;
        }

        try
        {
            if( !connection.Wait(
                TimeSpan.FromMilliseconds(ConnectionShutdownTimeoutMilliseconds)
            ))
            {
                ObserveFaults(connection);
                return;
            }
        }
        catch( AggregateException )
        {
        }
        finally
        {
            if( connection.IsCompleted )
            {
                _connectionCancellation?.Dispose();
                _connectionCancellation = null;
            }
        }
    }

    private static void ObserveFaults(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private IRenderable Render()
    {
        var width = Math.Max(1, AnsiConsole.Profile.Width);
        var height = Math.Max(1, AnsiConsole.Profile.Height - 1);
        var formWidth = Math.Min(FormWidth, width);
        var connecting = _connection != null;
        IRenderable logo = width >= 40 && height >= 16
            ? new FigletText(ApplicationName).Color(Color.Cyan1)
            : new Markup($"[bold cyan1]{ApplicationName}[/]");

        var input = new Panel(_url.Render(formWidth - 4, !connecting))
            .Header("[cyan1]Dataverse URL[/]")
            .RoundedBorder()
            .BorderColor(connecting ? Color.Grey : Color.Cyan1);
        input.Width = formWidth;

        var status = connecting
            ? "Connecting... Sign in in your browser if prompted."
            : _message;
        var content = new Rows(
            Align.Center(logo),
            Text.Empty,
            input,
            connecting ? RenderProgress(formWidth) : Text.Empty,
            new Text(status, new Style(Color.Grey))
        );

        var form = new Panel(content)
            .NoBorder()
            .Padding(0, 0);
        form.Width = formWidth;

        return Align.Center(form, VerticalAlignment.Middle)
            .Width(width)
            .Height(height);
    }

    private IRenderable RenderProgress(int width)
    {
        var pulseWidth = Math.Min(8, width);
        var travel = width - pulseWidth;
        var position = _frame++ % Math.Max(1, travel * 2);
        var offset = position <= travel ? position : travel * 2 - position;
        var before = new string('─', offset);
        var pulse = new string('━', pulseWidth);
        var after = new string('─', Math.Max(0, width - offset - pulseWidth));

        return new Markup($"[grey23]{before}[/][cyan1]{pulse}[/][grey23]{after}[/]");
    }
}
