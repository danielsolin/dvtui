using dvtui.Models;
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

            var selection = SolutionBrowserScreen.Show(
                solution,
                service.GetSolutionComponentsAsync,
                service.GetEntityAsync
            );
            if( selection.Result == SolutionBrowserResult.Quit )
            {
                return;
            }

            var context = LoadContext(service, solution);
            if( context == null )
            {
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
                AnsiConsole.Cursor.Show();
                Console.TreatControlCAsInput = previousControlCMode;
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
                        var loadTask = screen.LoadAsync(
                            cancellation.Token
                        );
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

                            ctx.UpdateTarget(screen.Render());
                            await Task.Delay(50);
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
                AnsiConsole.Cursor.Show();
                Console.TreatControlCAsInput = previousControlCMode;
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
                AnsiConsole.Cursor.Show();
                Console.TreatControlCAsInput = previousControlCMode;
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
        while( screen.PendingAction == FormAction.None )
        {
            if( Console.KeyAvailable )
            {
                var key = Console.ReadKey(intercept: true);
                screen.HandleKey(key);
            }

            context.UpdateTarget(screen.Render());
            await Task.Delay(50);
        }

        if( screen.PendingAction == FormAction.Submit )
        {
            context.UpdateTarget(screen.Render());
            await submit(cancellation.Token);
            context.UpdateTarget(screen.Render());
            await Task.Delay(500);
        }
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
