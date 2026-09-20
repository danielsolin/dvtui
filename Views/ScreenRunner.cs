using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal enum SchemaMutationOutcome
{
    Succeeded,
    Failed,
    Unknown
}

internal static class ScreenRunner
{
    internal const int FormPollIntervalMilliseconds = 50;

    internal static async Task RunFormAsync<TScreen>(
        TScreen screen,
        LiveDisplayContext context,
        Func<CancellationToken, Task> submit,
        Func<IRenderable>? render = null,
        bool retryAfterError = true
    )
        where TScreen : IFormScreen
    {
        var renderTarget = render ?? screen.Render;
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
                    context.UpdateTarget(renderTarget());
                    lastRevision = revision;
                    lastSize = size;
                }

                await Task.Delay(FormPollIntervalMilliseconds);
            }

            if( screen.PendingAction == FormAction.Close )
            {
                return;
            }

            var submitTask = Progress.Show(
                screen.ProgressMessage,
                submit
            );
            lastRevision = -1;
            lastSize = (Width: 0, Height: 0);

            while( !submitTask.IsCompleted )
            {
                Progress.DiscardPendingInput(typeof(TScreen).Name);

                var size = (
                    AnsiConsole.Profile.Width,
                    AnsiConsole.Profile.Height
                );
                var revision = screen.Revision;
                if( revision != lastRevision
                    || Progress.IsActive
                    || size != lastSize )
                {
                    context.UpdateTarget(renderTarget());
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
            context.UpdateTarget(renderTarget());
            if( screen.OutcomeUnknown )
            {
                return;
            }

            if( screen.HasError )
            {
                if( !retryAfterError )
                {
                    return;
                }

                SessionLog.Warning(
                    "UI.Form",
                    "Submit returned validation/error screen="
                        + typeof(TScreen).Name
                        + " status=" + screen.Status
                );
                screen.ResetForRetry();
                continue;
            }

            await Task.Delay(500);
            return;
        }
    }

    internal static async Task RunTableColumnsAsync(
        TableColumnsScreen screen,
        LiveDisplayContext context,
        bool loadColumns,
        Func<CancellationToken, Task>? loadFactory = null
    )
    {
        loadFactory ??= screen.LoadAsync;
        using var progressHost = Progress.Attach();
        using var cancellation = new CancellationTokenSource();
        Task? loadTask = loadColumns
            ? loadFactory(cancellation.Token)
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
                    if( key.Key == ConsoleKey.R
                        && loadTask == null
                        && screen.Editor == null )
                    {
                        loadTask = loadFactory(cancellation.Token);
                    }
                    else
                    {
                        screen.HandleKey(key);
                    }

                    if( screen.PendingAction != TableColumnsAction.None )
                    {
                        break;
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
            if( loadTask != null )
            {
                ObserveLateTask(loadTask);
            }
        }
    }

    internal static void ObserveLateTask(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    internal static SchemaMutationOutcome RunSchemaMutation(
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
                Task mutationTask;
                try
                {
                    SessionLog.Debug(
                        "UI.SchemaMutation",
                        "Dispatching operation=" + operationDescription
                    );
                    mutationTask = Progress.Show(
                        operationDescription,
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

                while( !mutationTask.IsCompleted )
                {
                    Progress.DiscardPendingInput("SchemaMutation");

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
            Progress.RenderStatus(
                width,
                status,
                Style.Parse("yellow"),
                showActiveMessage: false
            )
        ))
            .Header("Dataverse operation")
            .RoundedBorder()
            .Expand();
    }
}
