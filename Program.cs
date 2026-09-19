using dvtui.Models;
using dvtui.Services;
using dvtui.Views;

using Spectre.Console;

namespace dvtui;

internal static class Program
{
    private const int FormPollIntervalMilliseconds = 50;
    private static readonly TimeSpan FormOperationTimeout =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FormCancellationGracePeriod =
        TimeSpan.FromSeconds(3);

    private static void Main(string[] args)
    {
        ConfigureWslBrowser();
        DataverseService? service = null;
        Console.CancelKeyPress += HandleCancelKeyPress;

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
            RestoreTerminal(false);
            Console.CancelKeyPress -= HandleCancelKeyPress;
            service?.Dispose();
        }
    }

    private static void HandleCancelKeyPress(
        object? sender,
        ConsoleCancelEventArgs args
    )
    {
        RestoreTerminal(false);
    }

    private static void RestoreTerminal(bool treatControlCAsInput)
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

    private static void RunSolutionLoop(DataverseService service)
    {
        DataverseSolution? activeSolution = null;

        while( true )
        {
            if( activeSolution == null )
            {
                activeSolution = SolutionSelectionScreen.Show(
                    service.GetSolutionsAsync
                );
                if( activeSolution == null )
                {
                    return;
                }
            }

            var selection = SolutionBrowserScreen.Show(
                activeSolution,
                service.GetSolutionComponentsAsync,
                service.GetEntityAsync
            );
            if( selection.Result == SolutionBrowserResult.Quit )
            {
                return;
            }

            if( selection.Result == SolutionBrowserResult.BackToSolutions )
            {
                activeSolution = null;
                continue;
            }

            var context = LoadContext(service, activeSolution);
            if( context == null )
            {
                activeSolution = null;
                continue;
            }

            if( selection.Result == SolutionBrowserResult.CreateTable )
            {
                RunCreateTable(service, context);
                continue;
            }

            if( selection.Result == SolutionBrowserResult.OpenColumns
                && selection.Entity != null )
            {
                RunTableColumns(service, context, selection.Entity);
            }
        }
    }

    private static SolutionWriteContext? LoadContext(
        DataverseService service,
        DataverseSolution solution
    )
    {
        try
        {
            return service.CreateSchemaService()
                .LoadWriteContextAsync(
                    solution,
                    CancellationToken.None
                )
                .GetAwaiter().GetResult();
        }
        catch( Exception ex )
        {
            AnsiConsole.WriteLine(
                "Could not load write context: " + ex.Message
            );
            return null;
        }
    }

    private static void RunCreateTable(
        DataverseService service,
        SolutionWriteContext context
    )
    {
        var screen = new CreateTableScreen(service, context);
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            return;
        }

        var previousControlCMode = Console.TreatControlCAsInput;
        AnsiConsole.AlternateScreen(() =>
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Clear();
            try
            {
                AnsiConsole.Live(screen.Render())
                    .StartAsync(context => RunFormAsync(
                        screen,
                        context,
                        token => screen.SubmitAsync(token)
                    ))
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                RestoreTerminal(previousControlCMode);
            }
        });
    }

    private static void RunTableColumns(
        DataverseService service,
        SolutionWriteContext context,
        DataverseEntity entity
    )
    {
        var screen = new TableColumnsScreen(service, entity, context);
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            return;
        }

        var previousControlCMode = Console.TreatControlCAsInput;
        AnsiConsole.AlternateScreen(() =>
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Clear();
            try
            {
                var action = new TableColumnsAction[1];
                var column = new DataverseColumn?[1];
                AnsiConsole.Live(screen.Render())
                    .StartAsync(async ctx =>
                    {
                        using var cancellation = new CancellationTokenSource();
                        try
                        {
                            var loadTask = screen.LoadAsync(
                                cancellation.Token
                            );
                            var lastRevision = -1;
                            var lastSize = (Width: 0, Height: 0);
                            while( true )
                            {
                                if( loadTask != null
                                    && loadTask.IsCompleted )
                                {
                                    loadTask = null;
                                }

                                while( Console.KeyAvailable )
                                {
                                    var key = Console.ReadKey(
                                        intercept: true
                                    );
                                    if( key.Key == ConsoleKey.R )
                                    {
                                        loadTask = screen.LoadAsync(
                                            cancellation.Token
                                        );
                                    }
                                    else
                                    {
                                        screen.HandleKey(key);
                                    }

                                    if( screen.PendingAction !=
                                        TableColumnsAction.None )
                                    {
                                        action[0] = screen.PendingAction;
                                        column[0] = screen.PendingColumn;
                                        return;
                                    }
                                }

                                var size = (
                                    AnsiConsole.Profile.Width,
                                    AnsiConsole.Profile.Height
                                );
                                var revision = screen.Revision;
                                if( revision != lastRevision
                                    || size != lastSize )
                                {
                                    ctx.UpdateTarget(screen.Render());
                                    lastRevision = revision;
                                    lastSize = size;
                                }

                                await Task.Delay(
                                    FormPollIntervalMilliseconds
                                );
                            }
                        }
                        finally
                        {
                            cancellation.Cancel();
                        }
                    })
                    .GetAwaiter()
                    .GetResult();

                var pendingColumn = column[0];
                if( action[0] == TableColumnsAction.NewColumn )
                {
                    RunColumnEditor(
                        service,
                        context,
                        entity.LogicalName,
                        null
                    );
                }
                else if( action[0] == TableColumnsAction.EditColumn
                    && pendingColumn != null )
                {
                    RunColumnEditor(
                        service,
                        context,
                        entity.LogicalName,
                        pendingColumn
                    );
                }
                else if( action[0] == TableColumnsAction.DeleteColumn
                    && pendingColumn != null )
                {
                    DeleteColumn(
                        service,
                        entity.LogicalName,
                        pendingColumn
                    );
                }
                else if( action[0] == TableColumnsAction.Publish )
                {
                    PublishTable(service, entity.LogicalName);
                }
            }
            finally
            {
                RestoreTerminal(previousControlCMode);
            }
        });
    }

    private static void RunColumnEditor(
        DataverseService service,
        SolutionWriteContext context,
        string tableLogicalName,
        DataverseColumn? existing
    )
    {
        var screen = new ColumnEditorScreen(
            service,
            context,
            tableLogicalName,
            existing
        );
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            return;
        }

        var previousControlCMode = Console.TreatControlCAsInput;
        AnsiConsole.AlternateScreen(() =>
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Clear();
            try
            {
                AnsiConsole.Live(screen.Render())
                    .StartAsync(context => RunFormAsync(
                        screen,
                        context,
                        token => screen.SubmitAsync(token)
                    ))
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                RestoreTerminal(previousControlCMode);
            }
        });
    }

    private static async Task RunFormAsync<TScreen>(
        TScreen screen,
        LiveDisplayContext context,
        Func<CancellationToken, Task> submit
    )
        where TScreen : IFormScreen
    {
        using var cancellation = new CancellationTokenSource();
        while( true )
        {
            var lastRevision = -1;
            var lastSize = (Width: 0, Height: 0);
            while( screen.PendingAction == FormAction.None )
            {
                while( Console.KeyAvailable )
                {
                    var key = Console.ReadKey(intercept: true);
                    screen.HandleKey(key);
                }

                var size = (
                    AnsiConsole.Profile.Width,
                    AnsiConsole.Profile.Height
                );
                var revision = screen.Revision;
                if( revision != lastRevision
                    || size != lastSize )
                {
                    context.UpdateTarget(screen.Render());
                    lastRevision = revision;
                    lastSize = size;
                }

                await Task.Delay(FormPollIntervalMilliseconds);
            }

            if( screen.PendingAction == FormAction.Close )
            {
                return;
            }

            var submitTask = submit(cancellation.Token);
            var timeoutAt = DateTime.UtcNow + FormOperationTimeout;
            DateTime? cancellationStarted = null;
            lastRevision = -1;
            lastSize = (Width: 0, Height: 0);

            while( !submitTask.IsCompleted )
            {
                while( Console.KeyAvailable )
                {
                    var key = Console.ReadKey(intercept: true);
                    if( !IsCancelKey(key)
                        || cancellation.IsCancellationRequested )
                    {
                        continue;
                    }

                    screen.RequestCancellation();
                    cancellation.Cancel();
                    cancellationStarted = DateTime.UtcNow;
                }

                var now = DateTime.UtcNow;
                if( !cancellation.IsCancellationRequested
                    && now >= timeoutAt )
                {
                    screen.RequestCancellation();
                    cancellation.Cancel();
                    cancellationStarted = now;
                }

                if( cancellationStarted.HasValue
                    && now - cancellationStarted.Value
                        >= FormCancellationGracePeriod )
                {
                    throw new TimeoutException(
                        "The request did not finish after cancellation. "
                        + "Verify Dataverse before retrying."
                    );
                }

                var size = (
                    AnsiConsole.Profile.Width,
                    AnsiConsole.Profile.Height
                );
                var revision = screen.Revision;
                if( revision != lastRevision
                    || size != lastSize )
                {
                    context.UpdateTarget(screen.Render());
                    lastRevision = revision;
                    lastSize = size;
                }

                await Task.Delay(FormPollIntervalMilliseconds);
            }

            await submitTask;
            context.UpdateTarget(screen.Render());
            if( screen.HasError )
            {
                screen.ResetForRetry();
                continue;
            }

            await Task.Delay(500);
            return;
        }
    }

    private static bool IsCancelKey(ConsoleKeyInfo key)
    {
        return key.Key == ConsoleKey.Escape
            || key.KeyChar == '\u0003';
    }

    private static void DeleteColumn(
        DataverseService service,
        string tableLogicalName,
        DataverseColumn column
    )
    {
        try
        {
            var dependencies = service
                .GetColumnDeleteDependenciesAsync(
                    column.MetadataId,
                    CancellationToken.None
                )
                .GetAwaiter().GetResult();
            if( dependencies.Count > 0 )
            {
                AnsiConsole.WriteLine(
                    $"Column has {dependencies.Count} dependencies. "
                    + "Delete is blocked."
                );
                return;
            }

            service.DeleteColumnAsync(
                tableLogicalName,
                column.LogicalName,
                column.MetadataId,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            AnsiConsole.WriteLine("Column deleted.");
        }
        catch( Exception ex )
        {
            AnsiConsole.WriteLine(ex.Message);
        }
    }

    private static void PublishTable(
        DataverseService service,
        string tableLogicalName
    )
    {
        try
        {
            service.PublishTableAsync(
                tableLogicalName,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            AnsiConsole.WriteLine("Table published.");
        }
        catch( Exception ex )
        {
            AnsiConsole.WriteLine(ex.Message);
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
