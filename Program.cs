using dvtui.Models;
using dvtui.Services;
using dvtui.Views;
using Progress = dvtui.Views.Progress;

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
        var logPath = SessionLog.Start(args);
        SessionLog.Info(
            "Application",
            "Session log path=" + (logPath ?? "unavailable")
        );
        SessionLog.Info(
            "Application",
            "terminal inputRedirected=" + Console.IsInputRedirected
                + " outputRedirected=" + Console.IsOutputRedirected
                + " size=" + GetTerminalSize()
                + " controlCAsInput=" + Console.TreatControlCAsInput
        );
        ConfigureWslBrowser();
        DataverseService? service = null;
        Console.CancelKeyPress += HandleCancelKeyPress;

        try
        {
            void RunApplication()
            {
                SessionLog.Screen("StartupScreen", "enter");
                var connected = StartupScreen.Show(
                    GetEnvUrl(args),
                    async (url, cancellationToken) =>
                    {
                        SessionLog.Info(
                            "UI.Connection",
                            "Connect requested environment=" + url
                        );
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
            SessionLog.Exception("Application", ex, "Unhandled application error");
            AnsiConsole.MarkupLine(
                $"[red]Error: {Markup.Escape(ex.Message)}[/]"
            );
        }
        finally
        {
            RestoreTerminal(false);
            Console.CancelKeyPress -= HandleCancelKeyPress;
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

    private static void HandleCancelKeyPress(
        object? sender,
        ConsoleCancelEventArgs args
    )
    {
        SessionLog.Warning(
            "UI.Signal",
            "Console.CancelKeyPress received type=" + args.SpecialKey
        );
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
        SessionLog.Screen("SolutionLoop", "enter");
        DataverseSolution? activeSolution = null;
        SolutionWriteContext? activeContext = null;
        var pendingTables = new HashSet<Guid>();

        while( true )
        {
            if( activeSolution == null )
            {
                pendingTables.Clear();
                SessionLog.Screen("SolutionSelectionScreen", "enter");
                activeSolution = SolutionSelectionScreen.Show(
                    service.GetSolutionsAsync
                );
                SessionLog.Screen(
                    "SolutionSelectionScreen",
                    "exit",
                    activeSolution == null
                        ? "selected=none"
                        : "selected=" + activeSolution.UniqueName
                );
                if( activeSolution == null )
                {
                    SessionLog.Screen("SolutionLoop", "exit", "reason=no-solution");
                    return;
                }

                activeContext = LoadContext(service, activeSolution);
                SessionLog.Info(
                    "UI.Solution",
                    "Selected solution uniqueName=" + activeSolution.UniqueName
                        + " id=" + activeSolution.Id
                        + " canWrite=" + activeContext.CanWrite
                );
            }

            SessionLog.Screen(
                "SolutionBrowserScreen",
                "enter",
                "solution=" + activeSolution.UniqueName
            );
            var selection = SolutionBrowserScreen.Show(
                activeSolution,
                service.GetSolutionComponentsAsync,
                service.GetEntityAsync,
                activeContext?.CanWrite == true,
                activeContext?.WriteDisabledReason
            );
            SessionLog.Screen(
                "SolutionBrowserScreen",
                "exit",
                "result=" + selection.Result
                    + " entity=" + (selection.Entity?.LogicalName ?? "none")
            );
            if( selection.Result == SolutionBrowserResult.Quit )
            {
                SessionLog.Screen("SolutionLoop", "exit", "reason=quit");
                return;
            }

            if( selection.Result == SolutionBrowserResult.BackToSolutions )
            {
                SessionLog.Info(
                    "UI.Solution",
                    "Returning to solution selection"
                );
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
                SessionLog.Screen("CreateTableScreen", "enter");
                RunCreateTable(service, activeContext);
                SessionLog.Screen("CreateTableScreen", "exit");
                continue;
            }

            if( selection.Result == SolutionBrowserResult.OpenColumns
                && selection.Entity != null )
            {
                SessionLog.Screen(
                    "TableColumnsScreen",
                    "enter",
                    "table=" + selection.Entity.LogicalName
                );
                RunTableColumns(
                    service,
                    activeContext,
                    selection.Entity,
                    pendingTables
                );
                SessionLog.Screen("TableColumnsScreen", "exit");
            }
        }
    }

    private static SolutionWriteContext LoadContext(
        DataverseService service,
        DataverseSolution solution
    )
    {
        SessionLog.Info(
            "UI.Solution",
            "Loading write context solution=" + solution.UniqueName
                + " id=" + solution.Id
        );
        try
        {
            var context = Progress.Show(
                "Loading solution context...",
                token => service.CreateSchemaService()
                    .LoadWriteContextAsync(solution, token)
            )
                .GetAwaiter().GetResult();
            SessionLog.Info(
                "UI.Solution",
                "Write context loaded solution=" + solution.UniqueName
                    + " canWrite=" + context.CanWrite
                    + " publisherPrefix=" + context.PublisherPrefix
                    + " baseLanguage=" + context.BaseLanguage
            );
            return context;
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "UI.Solution",
                ex,
                "Could not load write context solution=" + solution.UniqueName
            );
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
        SessionLog.Info(
            "UI.CreateTable",
            "Opening form solution=" + context.SolutionUniqueName
        );
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
        SessionLog.Info(
            "UI.CreateTable",
            "Form closed status=" + screen.Status
        );
    }

    private static void RunTableColumns(
        DataverseService service,
        SolutionWriteContext context,
        DataverseEntity entity,
        HashSet<Guid> pendingTables
    )
    {
        SessionLog.Info(
            "UI.TableColumns",
            "Opening table=" + entity.LogicalName
                + " metadataId=" + entity.MetadataId
        );
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
            SessionLog.Info(
                "UI.TableColumns",
                "Screen action=" + action
                    + " table=" + entity.LogicalName
            );
            loadColumns = false;
            var pendingColumn = screen.PendingColumn;

            if( action == TableColumnsAction.Close
                || action == TableColumnsAction.None )
            {
                return;
            }

            if( action == TableColumnsAction.NewColumn )
            {
                SessionLog.Screen(
                    "ColumnEditorScreen",
                    "enter",
                    "mode=create table=" + entity.LogicalName
                );
                screen.ResetAction();
                var created = RunColumnEditor(
                    service,
                    context,
                    entity.LogicalName,
                    entity.MetadataId,
                    null
                );
                if( created )
                {
                    pendingTables.Add(entity.MetadataId);
                    screen.SetPendingChanges(true);
                    loadColumns = true;
                }
                SessionLog.Screen(
                    "ColumnEditorScreen",
                    "exit",
                    "mode=create succeeded=" + created
                );
            }
            else if( action == TableColumnsAction.SaveColumn
                && screen.Editor != null )
            {
                SessionLog.Screen(
                    "ColumnEditorScreen",
                    "submit",
                    "mode=edit column="
                        + (pendingColumn?.LogicalName ?? "unknown")
                );
                var saved = RunEmbeddedColumnEditor(screen, screen.Editor);
                SessionLog.Info(
                    "UI.ColumnEditor",
                    "Embedded edit completed succeeded=" + saved
                );
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
                SessionLog.Info(
                    "UI.DeleteColumn",
                    "Delete requested table=" + entity.LogicalName
                        + " column=" + pendingColumn.LogicalName
                        + " metadataId=" + pendingColumn.MetadataId
                );
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
                SessionLog.Info(
                    "UI.Publish",
                    "Publish requested table=" + entity.LogicalName
                        + " metadataId=" + entity.MetadataId
                );
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
        SessionLog.Info(
            "UI.TableColumns",
            "Render loop started loadColumns=" + loadColumns
        );
        AnsiConsole.Clear();
        AnsiConsole.Live(screen.Render())
            .StartAsync(async context =>
            {
                using var progressHost = Progress.Attach();
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
                        var refresh = false;
                        if( loadTask?.IsCompleted == true )
                        {
                            loadTask = null;
                            refresh = true;
                        }

                        var inputLocked = Progress.DiscardPendingInput(
                            "TableColumnsScreen"
                        );
                        while( !inputLocked && Console.KeyAvailable )
                        {
                            if( Progress.DiscardPendingInput(
                                "TableColumnsScreen"
                            ) )
                            {
                                inputLocked = true;
                                break;
                            }

                            var key = Console.ReadKey(intercept: true);
                            SessionLog.Key(
                                "TableColumnsScreen",
                                key,
                                "loading=" + screen.Loading
                            );
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
                        if( refresh
                            || screen.Revision != lastRevision
                            || Progress.IsActive
                            || size != lastSize )
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
        SessionLog.Info("UI.ColumnEditor", "Embedded submit loop started");
        AnsiConsole.Clear();
        AnsiConsole.Live(host.Render())
            .StartAsync(async context =>
            {
                using var progressHost = Progress.Attach();
                using var cancellation = new CancellationTokenSource();
                var submitTask = Progress.Show(
                    editor.ProgressMessage,
                    cancellation.Token,
                    token => editor.SubmitAsync(token)
                );
                var timeoutAt = DateTime.UtcNow + FormOperationTimeout;
                DateTime? cancellationStarted = null;
                while( !submitTask.IsCompleted )
                {
                    Progress.DiscardPendingInput("ColumnEditorScreen");

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
                        SessionLog.Warning(
                            "UI.ColumnEditor",
                            "Embedded submit cancellation grace period expired"
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
        SessionLog.Info(
            "UI.ColumnEditor",
            "Opening editor mode=" + (existing == null ? "create" : "edit")
                + " table=" + tableLogicalName
                + " column=" + (existing?.LogicalName ?? "none")
        );
        if( existing != null )
        {
            try
            {
                var refreshed = Progress.Show(
                    "Loading column definition...",
                    token => service.GetColumnDefinitionAsync(
                        tableLogicalName,
                        existing.LogicalName,
                        retrieveAsIfPublished: true,
                        context.BaseLanguage,
                        token
                    )
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
                SessionLog.Info(
                    "UI.ColumnEditor",
                    "Fresh column definition loaded column=" + refreshed.LogicalName
                        + " metadataId=" + refreshed.MetadataId
                );
            }
            catch( Exception ex )
            {
                SessionLog.Exception(
                    "UI.ColumnEditor",
                    ex,
                    "Could not load column for editing column=" + existing.LogicalName
                );
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
        using var progressHost = Progress.Attach();
        while( true )
        {
            var lastRevision = -1;
            var lastSize = (Width: 0, Height: 0);
            while( screen.PendingAction == FormAction.None )
            {
                var inputLocked = Progress.DiscardPendingInput(
                    typeof(TScreen).Name
                );
                while( !inputLocked && Console.KeyAvailable )
                {
                    if( Progress.DiscardPendingInput(typeof(TScreen).Name) )
                    {
                        inputLocked = true;
                        break;
                    }

                    var key = Console.ReadKey(intercept: true);
                    SessionLog.Key(
                        typeof(TScreen).Name,
                        key,
                        "form=" + screen.PendingAction
                    );
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
            var submitTask = Progress.Show(
                screen.ProgressMessage,
                cancellation.Token,
                submit
            );
            var timeoutAt = DateTime.UtcNow + FormOperationTimeout;
            DateTime? cancellationStarted = null;
            lastRevision = -1;
            lastSize = (Width: 0, Height: 0);

            while( !submitTask.IsCompleted )
            {
                Progress.DiscardPendingInput(typeof(TScreen).Name);

                var now = DateTime.UtcNow;
                if( !cancellation.IsCancellationRequested
                    && now >= timeoutAt )
                {
                    screen.RequestCancellation();
                    cancellation.Cancel();
                    cancellationStarted = now;
                    SessionLog.Warning(
                        "UI.Form",
                        "Operation timeout screen=" + typeof(TScreen).Name
                    );
                }

                if( cancellationStarted.HasValue
                    && now - cancellationStarted.Value
                        >= FormCancellationGracePeriod )
                {
                    screen.MarkOutcomeUnknown(
                        "The request did not finish after cancellation. "
                        + "Verify Dataverse before retrying."
                    );
                    SessionLog.Warning(
                        "UI.Form",
                        "Cancellation grace period expired screen="
                            + typeof(TScreen).Name
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
                    || Progress.IsActive
                    || size != lastSize )
                {
                    context.UpdateTarget(screen.Render());
                    lastRevision = revision;
                    lastSize = size;
                }

                await Task.Delay(FormPollIntervalMilliseconds);
            }

            await submitTask;
            SessionLog.Info(
                "UI.Form",
                "Submit completed screen=" + typeof(TScreen).Name
                    + " status=" + screen.Status
            );
            context.UpdateTarget(screen.Render());
            if( screen.OutcomeUnknown )
            {
                return;
            }

            if( screen.HasError )
            {
                SessionLog.Warning(
                    "UI.Form",
                    "Submit returned validation/error screen=" + typeof(TScreen).Name
                        + " status=" + screen.Status
                );
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

    private static SchemaMutationOutcome RunSchemaMutation(
        string operationDescription,
        Func<CancellationToken, Task> operation,
        out string resultStatus
    )
    {
        SessionLog.Info(
            "UI.SchemaMutation",
            "Started operation=" + operationDescription
        );
        var outcome = SchemaMutationOutcome.Failed;
        var status = operationDescription;
        var panel = CreateOperationPanel(operationDescription, status);
        AnsiConsole.Clear();
        AnsiConsole.Live(panel)
            .StartAsync(async displayContext =>
            {
                using var progressHost = Progress.Attach();
                using var cancellation = new CancellationTokenSource();
                Task mutationTask;
                try
                {
                    SessionLog.Debug(
                        "UI.SchemaMutation",
                        "Dispatching operation=" + operationDescription
                    );
                    mutationTask = Progress.Show(
                        operationDescription,
                        cancellation.Token,
                        operation
                    );
                }
                catch( Exception ex )
                {
                    SessionLog.Exception(
                        "UI.SchemaMutation",
                        ex,
                        "Could not dispatch operation=" + operationDescription
                    );
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
                    Progress.DiscardPendingInput("SchemaMutation");

                    var now = DateTime.UtcNow;
                    if( !cancellation.IsCancellationRequested
                        && now >= timeoutAt )
                    {
                        cancellationStarted = now;
                        cancellation.Cancel();
                        status = "Cancelling "
                            + operationDescription + " after timeout...";
                        SessionLog.Warning(
                            "UI.SchemaMutation",
                            "Timeout requested operation=" + operationDescription
                        );
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
                        SessionLog.Warning(
                            "UI.SchemaMutation",
                            "Outcome unknown operation=" + operationDescription
                        );
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
                    SessionLog.Info(
                        "UI.SchemaMutation",
                        "Succeeded operation=" + operationDescription
                    );
                }
                catch( OperationCanceledException )
                {
                    outcome = SchemaMutationOutcome.Unknown;
                    status = "The outcome of "
                        + operationDescription
                        + " is unknown. Verify Dataverse before retrying.";
                    SessionLog.Warning(
                        "UI.SchemaMutation",
                        "Cancelled after dispatch operation="
                            + operationDescription
                    );
                }
                catch( SchemaWriteOutcomeUnknownException ex )
                {
                    outcome = SchemaMutationOutcome.Unknown;
                    status = ex.Message;
                    SessionLog.Exception(
                        "UI.SchemaMutation",
                        ex,
                        "Outcome unknown operation=" + operationDescription
                    );
                }
                catch( Exception ex )
                {
                    status = ex.Message;
                    SessionLog.Exception(
                        "UI.SchemaMutation",
                        ex,
                        "Failed operation=" + operationDescription
                    );
                }

                displayContext.UpdateTarget(
                    CreateOperationPanel(operationDescription, status)
                );
            })
            .GetAwaiter()
            .GetResult();
        resultStatus = status;
        SessionLog.Info(
            "UI.SchemaMutation",
            "Finished outcome=" + outcome
                + " status=" + status
        );
        return outcome;
    }

    private static IRenderable CreateOperationPanel(
        string operationDescription,
        string status
    )
    {
        var width = Math.Max(1, AnsiConsole.Profile.Width);
        return new Panel(new Rows(
            new Text(operationDescription),
            Progress.RenderStatus(width, status, Style.Parse("yellow"))
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
        SessionLog.Info(
            "UI.DeleteColumn",
            "Starting delete workflow table=" + tableLogicalName
                + " column=" + column.LogicalName
                + " metadataId=" + column.MetadataId
        );
        try
        {
            var dependencies = Progress.Show(
                "Checking column dependencies...",
                token => service.GetColumnDeleteDependenciesAsync(
                    column.MetadataId,
                    token
                )
            )
                .GetAwaiter().GetResult();
            if( dependencies.Count > 0 )
            {
                SessionLog.Warning(
                    "UI.DeleteColumn",
                    "Delete blocked by dependencies count=" + dependencies.Count
                );
                ShowDependencies(dependencies);
                return false;
            }

            if( !ConfirmDeleteColumn(context, tableLogicalName, column) )
            {
                SessionLog.Info("UI.DeleteColumn", "Delete confirmation cancelled");
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
                SessionLog.Info("UI.DeleteColumn", "Delete completed");
                return true;
            }

            ShowOperationResult("Delete column", status);
            return false;
        }
        catch( Exception ex )
        {
            SessionLog.Exception("UI.DeleteColumn", ex, "Delete workflow failed");
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
        SessionLog.Info(
            "UI.Publish",
            "Starting publish workflow table=" + tableLogicalName
                + " metadataId=" + tableMetadataId
        );
        try
        {
            if( !ConfirmPublishTable(context, tableLogicalName) )
            {
                SessionLog.Info("UI.Publish", "Publish confirmation cancelled");
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
                SessionLog.Info("UI.Publish", "Publish completed");
                return true;
            }

            ShowOperationResult("Publish table", status);
            return false;
        }
        catch( Exception ex )
        {
            SessionLog.Exception("UI.Publish", ex, "Publish workflow failed");
            ShowOperationResult("Publish table", ex.Message);
            return false;
        }
    }

    private static void ShowDependencies(
        IReadOnlyList<DependencyInfo> dependencies
    )
    {
        SessionLog.Info(
            "UI.DeleteColumn",
            "Showing dependency block count=" + dependencies.Count
        );
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
        SessionLog.Screen(
            "ConfirmationScreen",
            "enter",
            "operation=delete-column column=" + column.LogicalName
        );
        var confirmed = new ConfirmationScreen(
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
        SessionLog.Screen(
            "ConfirmationScreen",
            "exit",
            "operation=delete-column confirmed=" + confirmed
        );
        return confirmed;
    }

    private static bool ConfirmPublishTable(
        SolutionWriteContext context,
        string tableLogicalName
    )
    {
        SessionLog.Screen(
            "ConfirmationScreen",
            "enter",
            "operation=publish table=" + tableLogicalName
        );
        var confirmed = new ConfirmationScreen(
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
        SessionLog.Screen(
            "ConfirmationScreen",
            "exit",
            "operation=publish confirmed=" + confirmed
        );
        return confirmed;
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
        SessionLog.Info(
            "UI.Message",
            "Showing result title=" + title + " status=" + status
        );
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

    private static string GetTerminalSize()
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

    private enum SchemaMutationOutcome
    {
        Succeeded,
        Failed,
        Unknown
    }
}
