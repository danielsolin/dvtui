using System.Text;
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
            AnsiConsole.WriteLine(
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
        if( screen.OutcomeUnknown )
        {
            AnsiConsole.WriteLine(screen.Status);
        }
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

        var sessionPendingChanges = pendingTables.Contains(entity.MetadataId);
        while( true )
        {
            var screen = new TableColumnsScreen(
                service,
                entity,
                context,
                sessionPendingChanges
            );
            var previousControlCMode = Console.TreatControlCAsInput;
            var action = TableColumnsAction.None;
            DataverseColumn? pendingColumn = null;
            AnsiConsole.AlternateScreen(() =>
            {
                Console.TreatControlCAsInput = true;
                AnsiConsole.Clear();
                try
                {
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
                                        if( key.Key == ConsoleKey.R
                                            && loadTask == null )
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
                                            action = screen.PendingAction;
                                            pendingColumn = screen.PendingColumn;
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
                }
                finally
                {
                    RestoreTerminal(previousControlCMode);
                }
            });

            if( action == TableColumnsAction.Close
                || action == TableColumnsAction.None )
            {
                return;
            }

            if( action == TableColumnsAction.NewColumn )
            {
                if( RunColumnEditor(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId,
                    null
                ) )
                {
                    sessionPendingChanges = true;
                    pendingTables.Add(entity.MetadataId);
                }
            }
            else if( action == TableColumnsAction.EditColumn
                && pendingColumn != null )
            {
                if( RunColumnEditor(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId,
                    pendingColumn
                ) )
                {
                    sessionPendingChanges = true;
                    pendingTables.Add(entity.MetadataId);
                }
            }
            else if( action == TableColumnsAction.DeleteColumn
                && pendingColumn != null )
            {
                if( DeleteColumn(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId,
                    pendingColumn
                ) )
                {
                    sessionPendingChanges = true;
                    pendingTables.Add(entity.MetadataId);
                }
            }
            else if( action == TableColumnsAction.Publish )
            {
                if( PublishTable(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId
                ) )
                {
                    sessionPendingChanges = false;
                    pendingTables.Remove(entity.MetadataId);
                }
            }
        }
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
                    AnsiConsole.WriteLine(
                        "The column identity changed; reload before editing."
                    );
                    return false;
                }

                existing = refreshed;
            }
            catch( Exception ex )
            {
                AnsiConsole.WriteLine(
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
        if( screen.OutcomeUnknown )
        {
            AnsiConsole.WriteLine(screen.Status);
        }

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
        var previousControlCMode = Console.TreatControlCAsInput;
        AnsiConsole.AlternateScreen(() =>
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Clear();
            try
            {
                AnsiConsole.Live(new Text(operationDescription))
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
                            displayContext.UpdateTarget(new Text(status));
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
                                displayContext.UpdateTarget(new Text(status));
                                return;
                            }

                            displayContext.UpdateTarget(new Text(status));
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

                        displayContext.UpdateTarget(new Text(status));
                    })
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                RestoreTerminal(previousControlCMode);
            }
        });
        resultStatus = status;
        return outcome;
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
                AnsiConsole.WriteLine("Column deleted.");
                return true;
            }

            AnsiConsole.WriteLine(status);
            return false;
        }
        catch( Exception ex )
        {
            AnsiConsole.WriteLine(ex.Message);
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
                AnsiConsole.WriteLine("Table published.");
                return true;
            }

            AnsiConsole.WriteLine(status);
            return false;
        }
        catch( Exception ex )
        {
            AnsiConsole.WriteLine(ex.Message);
            return false;
        }
    }

    private static void ShowDependencies(
        IReadOnlyList<DependencyInfo> dependencies
    )
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine(
            $"Column has {dependencies.Count} dependencies. Delete is blocked."
        );
        foreach( var dependency in dependencies )
        {
            var name = string.IsNullOrWhiteSpace(dependency.Name)
                ? dependency.ObjectId?.ToString() ?? "Unknown"
                : dependency.Name;
            AnsiConsole.WriteLine(
                $"- {name} (component type {dependency.ComponentType?.ToString()
                    ?? "unknown"})"
            );
        }

        AnsiConsole.WriteLine("Press Enter or Esc to return.");
        WaitForConfirmationKey();
    }

    private static bool ConfirmDeleteColumn(
        SolutionWriteContext context,
        string tableLogicalName,
        DataverseColumn column
    )
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine("Delete column");
        AnsiConsole.WriteLine("Environment: " + context.EnvironmentUrl);
        AnsiConsole.WriteLine("Solution: " + context.SolutionUniqueName);
        AnsiConsole.WriteLine("Table: " + tableLogicalName);
        AnsiConsole.WriteLine("Column: " + column.LogicalName);
        AnsiConsole.WriteLine(
            "This deletes the column and its stored data from the environment."
        );
        AnsiConsole.WriteLine(
            "It does not only remove the column from this solution."
        );
        AnsiConsole.WriteLine(
            "Type the full column logical name and press Enter. Esc cancels."
        );
        var entered = ReadConfirmationText();
        return string.Equals(
            entered,
            column.LogicalName,
            StringComparison.Ordinal
        );
    }

    private static bool ConfirmPublishTable(
        SolutionWriteContext context,
        string tableLogicalName
    )
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine("Publish table");
        AnsiConsole.WriteLine("Environment: " + context.EnvironmentUrl);
        AnsiConsole.WriteLine("Solution: " + context.SolutionUniqueName);
        AnsiConsole.WriteLine("Table: " + tableLogicalName);
        AnsiConsole.WriteLine(
            "This publishes pending customizations for this table, including "
            + "changes made by other users."
        );
        AnsiConsole.WriteLine("Press Y then Enter to publish. Esc cancels.");
        var entered = ReadConfirmationText();
        return string.Equals(entered, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entered, "Yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadConfirmationText()
    {
        var value = new StringBuilder();
        Console.Write("> ");
        while( true )
        {
            var key = Console.ReadKey(intercept: true);
            if( key.Key == ConsoleKey.Escape || key.KeyChar == '\u0003' )
            {
                Console.WriteLine();
                return null;
            }

            if( key.Key == ConsoleKey.Enter )
            {
                Console.WriteLine();
                return value.ToString();
            }

            if( key.Key == ConsoleKey.Backspace
                || key.KeyChar == '\b'
                || key.KeyChar == '\u007f' )
            {
                if( value.Length > 0 )
                {
                    value.Length--;
                }

                RenderConfirmationValue(value);
                continue;
            }

            if( !char.IsControl(key.KeyChar) )
            {
                value.Append(key.KeyChar);
                RenderConfirmationValue(value);
            }
        }
    }

    private static void RenderConfirmationValue(StringBuilder value)
    {
        Console.Write("\r> " + value + " ");
    }

    private static void WaitForConfirmationKey()
    {
        while( true )
        {
            var key = Console.ReadKey(intercept: true);
            if( key.Key == ConsoleKey.Enter
                || key.Key == ConsoleKey.Escape
                || key.KeyChar == '\u0003' )
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

    private enum SchemaMutationOutcome
    {
        Succeeded,
        Failed,
        Unknown
    }
}
