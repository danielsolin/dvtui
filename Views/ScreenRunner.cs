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

internal enum SchemaMutationKind
{
    DeleteColumn,
    PublishTable
}

internal sealed record SchemaMutation(
    SchemaMutationKind Kind,
    string Description,
    Func<CancellationToken, Task> Operation
);

internal sealed record SchemaMutationResult(
    SchemaMutationKind Kind,
    SchemaMutationOutcome Outcome,
    string Status
);

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
        Func<CancellationToken, Task>? loadFactory = null,
        SchemaMutation? mutation = null
    )
    {
        loadFactory ??= screen.LoadAsync;
        using var cancellation = new CancellationTokenSource();
        Task? loadTask = loadColumns
            ? loadFactory(cancellation.Token)
            : null;
        Task? mutationTask = mutation == null
            ? null
            : Progress.Show(
                mutation.Description,
                cancellation.Token,
                mutation.Operation
            );
        if( mutation != null )
        {
            SessionLog.Info(
                "UI.SchemaMutation",
                "Started operation=" + mutation.Description
            );
        }
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
                if( mutation != null
                    && mutationTask != null
                    && mutationTask.IsCompleted )
                {
                    var finished = mutationTask;
                    mutationTask = null;
                    var result = await CollectMutationResult(
                        mutation,
                        finished
                    );
                    screen.CompleteMutation(result);
                    if( result.Outcome == SchemaMutationOutcome.Succeeded )
                    {
                        if( result.Kind == SchemaMutationKind.PublishTable )
                        {
                            screen.SetPendingChanges(false);
                        }
                        else
                        {
                            screen.SetPendingChanges(true);
                            loadTask = loadFactory(cancellation.Token);
                        }
                    }
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
            if( mutationTask != null )
            {
                ObserveLateTask(mutationTask);
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

    private static async Task<SchemaMutationResult> CollectMutationResult(
        SchemaMutation mutation,
        Task operation
    )
    {
        try
        {
            await operation;
            SessionLog.Info(
                "UI.SchemaMutation",
                "Succeeded operation=" + mutation.Description
            );
            return new SchemaMutationResult(
                mutation.Kind,
                SchemaMutationOutcome.Succeeded,
                mutation.Description + " completed."
            );
        }
        catch( OperationCanceledException )
        {
            SessionLog.Warning(
                "UI.SchemaMutation",
                "Cancelled after dispatch operation="
                    + mutation.Description
            );
            return new SchemaMutationResult(
                mutation.Kind,
                SchemaMutationOutcome.Unknown,
                "The outcome of " + mutation.Description
                    + " is unknown. Verify Dataverse before retrying."
            );
        }
        catch( SchemaWriteOutcomeUnknownException ex )
        {
            SessionLog.Exception(
                "UI.SchemaMutation",
                ex,
                "Outcome unknown operation=" + mutation.Description
            );
            return new SchemaMutationResult(
                mutation.Kind,
                SchemaMutationOutcome.Unknown,
                ex.Message
            );
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "UI.SchemaMutation",
                ex,
                "Failed operation=" + mutation.Description
            );
            return new SchemaMutationResult(
                mutation.Kind,
                SchemaMutationOutcome.Failed,
                ex.Message
            );
        }
    }
}
