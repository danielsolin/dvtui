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
    internal static readonly TimeSpan FormOperationTimeout =
        TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan FormCancellationGracePeriod =
        TimeSpan.FromSeconds(3);

    internal static async Task RunFormAsync<TScreen>(
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
}
