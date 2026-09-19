using dvtui.Models;
using dvtui.Services;
using dvtui.Views;

using Spectre.Console;
using Spectre.Console.Rendering;

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
            void RunApplication()
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

                if( connected && service != null )
                {
                    RunSolutionLoop(service);
                }
            }

            if( Console.IsInputRedirected || Console.IsOutputRedirected )
            {
                RunApplication();
            }
            else
            {
                var previousControlCMode = Console.TreatControlCAsInput;
                AnsiConsole.AlternateScreen(() =>
                {
                    Console.TreatControlCAsInput = true;
                    AnsiConsole.Clear();
                    try
                    {
                        RunApplication();
                    }
                    finally
                    {
                        RestoreTerminal(previousControlCMode);
                    }
                });
            }
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
        SolutionWriteContext? activeContext = null;
        var pendingTables = new HashSet<Guid>();

        while( true )
        {
            if( activeSolution == null )
            {
                pendingTables.Clear();
                activeSolution = SolutionSelectionScreen.Show(
                    service.GetSolutionsAsync
                );
                if( activeSolution == null )
                {
                    return;
                }

                activeContext = LoadContext(service, activeSolution);
            }

            var selection = SolutionBrowserScreen.Show(
                activeSolution,
                service.GetSolutionComponentsAsync,
                service.GetEntityAsync,
                activeContext?.CanWrite == true,
                activeContext?.WriteDisabledReason
            );
            if( selection.Result == SolutionBrowserResult.Quit )
            {
                return;
            }

            if( selection.Result == SolutionBrowserResult.BackToSolutions )
            {
                activeSolution = null;
                activeContext = null;
                continue;
            }

            if( activeContext == null )
            {
                activeSolution = null;
                continue;
            }

            if( selection.Result == SolutionBrowserResult.CreateTable )
            {
                RunCreateTable(service, activeContext);
                continue;
            }

            if( selection.Result == SolutionBrowserResult.OpenColumns
                && selection.Entity != null )
            {
                RunTableColumns(
                    service,
                    activeContext,
                    selection.Entity,
                    pendingTables
                );
            }
        }
    }

    private static SolutionWriteContext LoadContext(
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
            ShowOperationResult(
                "Solution unavailable",
                "Could not load write context: " + ex.Message
            );
            return new SolutionWriteContext
            {
                SolutionId = solution.Id,
                SolutionUniqueName = solution.UniqueName,
                IsManaged = solution.IsManaged == true,
                CanWrite = false,
                WriteDisabledReason = ex.Message,
                EnvironmentUrl = service.EnvironmentUrl
            };
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

        AnsiConsole.Clear();
        AnsiConsole.Live(screen.Render())
            .StartAsync(context => RunFormAsync(
                screen,
                context,
                token => screen.SubmitAsync(token)
            ))
            .GetAwaiter()
            .GetResult();
    }

    private static void RunTableColumns(
        DataverseService service,
        SolutionWriteContext context,
        DataverseEntity entity,
        HashSet<Guid> pendingTables
    )
    {
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            return;
        }

        var screen = new TableColumnsScreen(
            service,
            entity,
            context,
            pendingTables.Contains(entity.MetadataId)
        );
        var loadColumns = true;
        while( true )
        {
            var action = RunTableColumnsScreen(screen, loadColumns);
            loadColumns = false;
            var pendingColumn = screen.PendingColumn;

            if( action == TableColumnsAction.Close
                || action == TableColumnsAction.None )
            {
                return;
            }

            if( action == TableColumnsAction.NewColumn )
            {
                screen.ResetAction();
                if( RunColumnEditor(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId,
                    null
                ) )
                {
                    pendingTables.Add(entity.MetadataId);
                    screen.SetPendingChanges(true);
                    loadColumns = true;
                }
            }
            else if( action == TableColumnsAction.SaveColumn
                && screen.Editor != null )
            {
                var saved = RunEmbeddedColumnEditor(screen, screen.Editor);
                screen.CompleteEdit(saved);
                if( saved )
                {
                    pendingTables.Add(entity.MetadataId);
                    loadColumns = true;
                }
            }
            else if( action == TableColumnsAction.DeleteColumn
                && pendingColumn != null )
            {
                screen.ResetAction();
                if( DeleteColumn(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId,
                    pendingColumn
                ) )
                {
                    pendingTables.Add(entity.MetadataId);
                    screen.SetPendingChanges(true);
                    loadColumns = true;
                }
            }
            else if( action == TableColumnsAction.Publish )
            {
                screen.ResetAction();
                if( PublishTable(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId
                ) )
                {
                    pendingTables.Remove(entity.MetadataId);
                    screen.SetPendingChanges(false);
                }
            }
        }
    }

    private static TableColumnsAction RunTableColumnsScreen(
        TableColumnsScreen screen,
        bool loadColumns
    )
    {
        AnsiConsole.Clear();
        AnsiConsole.Live(screen.Render())
            .StartAsync(async context =>
            {
                using var cancellation = new CancellationTokenSource();
                Task? loadTask = loadColumns
                    ? screen.LoadAsync(cancellation.Token)
                    : null;
                try
                {
                    var lastRevision = -1;
                    var lastSize = (Width: 0, Height: 0);
                    while( screen.PendingAction == TableColumnsAction.None )
                    {
                        if( loadTask?.IsCompleted == true )
                        {
                            loadTask = null;
                        }

                        while( Console.KeyAvailable )
                        {
                            var key = Console.ReadKey(intercept: true);
                            if( key.Key == ConsoleKey.R && loadTask == null )
                            {
                                loadTask = screen.LoadAsync(cancellation.Token);
                            }
                            else
                            {
                                screen.HandleKey(key);
                            }
                        }

                        var size = (
                            AnsiConsole.Profile.Width,
                            AnsiConsole.Profile.Height
                        );
                        if( screen.Revision != lastRevision || size != lastSize )
                        {
                            context.UpdateTarget(screen.Render());
                            lastRevision = screen.Revision;
                            lastSize = size;
                        }

                        await Task.Delay(FormPollIntervalMilliseconds);
                    }
                }
                finally
                {
                    cancellation.Cancel();
                }
            })
            .GetAwaiter()
            .GetResult();
        return screen.PendingAction;
    }

    private static bool RunEmbeddedColumnEditor(
        TableColumnsScreen host,
        ColumnEditorScreen editor
    )
    {
        AnsiConsole.Clear();
        AnsiConsole.Live(host.Render())
            .StartAsync(async context =>
            {
                using var cancellation = new CancellationTokenSource();
                var submitTask = editor.SubmitAsync(cancellation.Token);
                var timeoutAt = DateTime.UtcNow + FormOperationTimeout;
                DateTime? cancellationStarted = null;
                while( !submitTask.IsCompleted )
                {
                    while( Console.KeyAvailable )
                    {
                        var key = Console.ReadKey(intercept: true);
                        if( IsCancelKey(key)
                            && !cancellation.IsCancellationRequested )
                        {
                            editor.RequestCancellation();
                            cancellation.Cancel();
                            cancellationStarted = DateTime.UtcNow;
                        }
                    }

                    var now = DateTime.UtcNow;
                    if( !cancellation.IsCancellationRequested
                        && now >= timeoutAt )
                    {
                        editor.RequestCancellation();
                        cancellation.Cancel();
                        cancellationStarted = now;
                    }

                    if( cancellationStarted.HasValue
                        && now - cancellationStarted.Value
                            >= FormCancellationGracePeriod )
                    {
                        editor.MarkOutcomeUnknown(
                            "The request did not finish after cancellation. "
                            + "Verify Dataverse before retrying."
                        );
                        ObserveLateTask(submitTask);
                        context.UpdateTarget(host.Render());
                        return;
                    }

                    context.UpdateTarget(host.Render());
                    await Task.Delay(FormPollIntervalMilliseconds);
                }

                await submitTask;
                context.UpdateTarget(host.Render());
            })
            .GetAwaiter()
            .GetResult();
        return editor.MutationSucceeded;
    }

    private static bool RunColumnEditor(
        DataverseService service,
        SolutionWriteContext context,
        string tableLogicalName,
        Guid tableMetadataId,
        DataverseColumn? existing
    )
    {
        if( existing != null )
        {
            try
            {
                var refreshed = service.GetColumnDefinitionAsync(
                    tableLogicalName,
                    existing.LogicalName,
                    retrieveAsIfPublished: false,
                    context.BaseLanguage,
                    CancellationToken.None
                ).GetAwaiter().GetResult();
                if( refreshed.MetadataId != existing.MetadataId )
                {
                    ShowOperationResult(
                        "Edit column",
                        "The column identity changed; reload before editing."
                    );
                    return false;
                }

                existing = refreshed;
            }
            catch( Exception ex )
            {
                ShowOperationResult(
                    "Edit column",
                    "Could not load the column for editing: " + ex.Message
                );
                return false;
            }
        }

        var screen = new ColumnEditorScreen(
            service,
            context,
            tableLogicalName,
            tableMetadataId,
            existing
        );
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            return false;
        }

        AnsiConsole.Clear();
        AnsiConsole.Live(screen.Render())
            .StartAsync(context => RunFormAsync(
                screen,
                context,
                token => screen.SubmitAsync(token)
            ))
            .GetAwaiter()
            .GetResult();

        return screen.MutationSucceeded;
    }

    private static async Task RunFormAsync<TScreen>(
        TScreen screen,
        LiveDisplayContext context,
        Func<CancellationToken, Task> submit
    )
        where TScreen : IFormScreen
    {
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

            using var cancellation = new CancellationTokenSource();
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
                    screen.MarkOutcomeUnknown(
                        "The request did not finish after cancellation. "
                        + "Verify Dataverse before retrying."
                    );
                    ObserveLateTask(submitTask);
                    context.UpdateTarget(screen.Render());
                    return;
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
            if( screen.OutcomeUnknown )
            {
                return;
            }

            if( screen.HasError )
            {
                screen.ResetForRetry();
                continue;
            }

            await Task.Delay(500);
            return;
        }
    }

    private static void ObserveLateTask(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static bool IsCancelKey(ConsoleKeyInfo key)
    {
        return key.Key == ConsoleKey.Escape
            || key.KeyChar == '\u0003';
    }

    private static SchemaMutationOutcome RunSchemaMutation(
        string operationDescription,
        Func<CancellationToken, Task> operation,
        out string resultStatus
    )
    {
        var outcome = SchemaMutationOutcome.Failed;
        var status = operationDescription;
        var panel = CreateOperationPanel(operationDescription, status);
        AnsiConsole.Clear();
        AnsiConsole.Live(panel)
            .StartAsync(async displayContext =>
            {
                using var cancellation = new CancellationTokenSource();
                Task mutationTask;
                try
                {
                    mutationTask = operation(cancellation.Token);
                }
                catch( Exception ex )
                {
                    status = ex.Message;
                    displayContext.UpdateTarget(
                        CreateOperationPanel(operationDescription, status)
                    );
                    return;
                }

                var timeoutAt = DateTime.UtcNow + FormOperationTimeout;
                DateTime? cancellationStarted = null;
                while( !mutationTask.IsCompleted )
                {
                    while( Console.KeyAvailable )
                    {
                        var key = Console.ReadKey(intercept: true);
                        if( IsCancelKey(key)
                            && !cancellation.IsCancellationRequested )
                        {
                            cancellationStarted = DateTime.UtcNow;
                            cancellation.Cancel();
                            status = "Cancelling "
                                + operationDescription + "...";
                        }
                    }

                    var now = DateTime.UtcNow;
                    if( !cancellation.IsCancellationRequested
                        && now >= timeoutAt )
                    {
                        cancellationStarted = now;
                        cancellation.Cancel();
                        status = "Cancelling "
                            + operationDescription + " after timeout...";
                    }

                    if( cancellationStarted.HasValue
                        && now - cancellationStarted.Value
                            >= FormCancellationGracePeriod )
                    {
                        outcome = SchemaMutationOutcome.Unknown;
                        status = "The outcome of "
                            + operationDescription
                            + " is unknown. Verify Dataverse before retrying.";
                        ObserveLateTask(mutationTask);
                        displayContext.UpdateTarget(
                            CreateOperationPanel(operationDescription, status)
                        );
                        return;
                    }

                    displayContext.UpdateTarget(
                        CreateOperationPanel(operationDescription, status)
                    );
                    await Task.Delay(FormPollIntervalMilliseconds);
                }

                try
                {
                    await mutationTask;
                    outcome = SchemaMutationOutcome.Succeeded;
                    status = operationDescription + " completed.";
                }
                catch( OperationCanceledException )
                {
                    outcome = SchemaMutationOutcome.Unknown;
                    status = "The outcome of "
                        + operationDescription
                        + " is unknown. Verify Dataverse before retrying.";
                }
                catch( SchemaWriteOutcomeUnknownException ex )
                {
                    outcome = SchemaMutationOutcome.Unknown;
                    status = ex.Message;
                }
                catch( Exception ex )
                {
                    status = ex.Message;
                }

                displayContext.UpdateTarget(
                    CreateOperationPanel(operationDescription, status)
                );
            })
            .GetAwaiter()
            .GetResult();
        resultStatus = status;
        return outcome;
    }

    private static IRenderable CreateOperationPanel(
        string operationDescription,
        string status
    )
    {
        return new Panel(new Rows(
            new Text(operationDescription),
            new Text(status, Style.Parse("yellow"))
        ))
            .Header("Dataverse operation")
            .RoundedBorder()
            .Expand();
    }

    private static bool DeleteColumn(
        DataverseService service,
        SolutionWriteContext context,
        string tableLogicalName,
        Guid tableMetadataId,
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
                ShowDependencies(dependencies);
                return false;
            }

            if( !ConfirmDeleteColumn(context, tableLogicalName, column) )
            {
                return false;
            }

            var outcome = RunSchemaMutation(
                "Deleting column " + column.LogicalName
                    + " from table " + tableLogicalName
                    + " in solution " + context.SolutionUniqueName
                    + " at " + context.EnvironmentUrl,
                token => service.DeleteColumnAsync(
                    tableLogicalName,
                    column.LogicalName,
                    column.MetadataId,
                    token,
                    tableMetadataId,
                    context
                ),
                out var status
            );
            if( outcome == SchemaMutationOutcome.Succeeded )
            {
                return true;
            }

            ShowOperationResult("Delete column", status);
            return false;
        }
        catch( Exception ex )
        {
            ShowOperationResult("Delete column", ex.Message);
            return false;
        }
    }

    private static bool PublishTable(
        DataverseService service,
        SolutionWriteContext context,
        string tableLogicalName,
        Guid tableMetadataId
    )
    {
        try
        {
            if( !ConfirmPublishTable(context, tableLogicalName) )
            {
                return false;
            }

            var outcome = RunSchemaMutation(
                "Publishing table " + tableLogicalName
                    + " in solution " + context.SolutionUniqueName
                    + " at " + context.EnvironmentUrl,
                token => service.PublishTableAsync(
                    tableLogicalName,
                    token,
                    context,
                    tableMetadataId
                ),
                out var status
            );
            if( outcome == SchemaMutationOutcome.Succeeded )
            {
                return true;
            }

            ShowOperationResult("Publish table", status);
            return false;
        }
        catch( Exception ex )
        {
            ShowOperationResult("Publish table", ex.Message);
            return false;
        }
    }

    private static void ShowDependencies(
        IReadOnlyList<DependencyInfo> dependencies
    )
    {
        var content = new List<IRenderable>
        {
            new Text(
                $"Column has {dependencies.Count} dependencies. Delete is blocked.",
                Style.Parse("yellow")
            )
        };
        foreach( var dependency in dependencies )
        {
            var name = string.IsNullOrWhiteSpace(dependency.Name)
                ? dependency.ObjectId?.ToString() ?? "Unknown"
                : dependency.Name;
            var type = dependency.ComponentType?.ToString() ?? "unknown";
            content.Add(new Text($"- {name} (component type {type})"));
        }

        MessageScreen.Show("Delete blocked", content);
    }

    private static bool ConfirmDeleteColumn(
        SolutionWriteContext context,
        string tableLogicalName,
        DataverseColumn column
    )
    {
        return new ConfirmationScreen(
            "Delete column",
            BuildOperationDetails(context, tableLogicalName, column.LogicalName),
            [
                "This deletes the column and its stored data.",
                "It does not only remove the column from this solution."
            ],
            "Type the full column logical name to confirm.",
            value => string.Equals(
                value,
                column.LogicalName,
                StringComparison.Ordinal
            )
        ).Show();
    }

    private static bool ConfirmPublishTable(
        SolutionWriteContext context,
        string tableLogicalName
    )
    {
        return new ConfirmationScreen(
            "Publish table",
            BuildOperationDetails(context, tableLogicalName, null),
            [
                "This publishes all pending table customizations, including",
                "changes made by other users."
            ],
            "Type Y or Yes to publish.",
            value => string.Equals(
                value,
                "Y",
                StringComparison.OrdinalIgnoreCase
            ) || string.Equals(
                value,
                "Yes",
                StringComparison.OrdinalIgnoreCase
            )
        ).Show();
    }

    private static IReadOnlyList<(string Label, string Value)>
        BuildOperationDetails(
            SolutionWriteContext context,
            string tableLogicalName,
            string? columnLogicalName
        )
    {
        var details = new List<(string Label, string Value)>
        {
            ("Environment", context.EnvironmentUrl),
            ("Solution", context.SolutionUniqueName),
            ("Table", tableLogicalName)
        };
        if( columnLogicalName != null )
        {
            details.Add(("Column", columnLogicalName));
        }

        return details;
    }

    private static void ShowOperationResult(string title, string status)
    {
        MessageScreen.Show(title, [new Text(status)]);
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

    private enum SchemaMutationOutcome
    {
        Succeeded,
        Failed,
        Unknown
    }
}
