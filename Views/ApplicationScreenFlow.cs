using dvtui.Models;
using dvtui.Services;
using Progress = dvtui.Views.Progress;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class ApplicationScreenFlow
{
    private readonly IDataverseQueryService _queryService;
    private readonly IDataverseSchemaService _schemaService;
    private readonly string _environmentUrl;

    internal ApplicationScreenFlow(
        IDataverseQueryService queryService,
        IDataverseSchemaService schemaService,
        string environmentUrl
    )
    {
        _queryService = queryService;
        _schemaService = schemaService;
        _environmentUrl = environmentUrl;
    }

    internal void Run()
    {
        RunSolutionLoop();
    }

    private void RunSolutionLoop()
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
                    _queryService.GetSolutionsAsync
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

                activeContext = LoadContext(activeSolution);
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
                _queryService.GetSolutionComponentsAsync,
                _queryService.GetEntityAsync,
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
                RunCreateTable(activeContext);
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
                    activeContext,
                    selection.Entity,
                    pendingTables
                );
                SessionLog.Screen("TableColumnsScreen", "exit");
            }
        }
    }

    private SolutionWriteContext LoadContext(
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
                token => _schemaService
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
                EnvironmentUrl = _environmentUrl
            };
        }
    }

    private void RunCreateTable(
        SolutionWriteContext context
    )
    {
        SessionLog.Info(
            "UI.CreateTable",
            "Opening form solution=" + context.SolutionUniqueName
        );
        var screen = new CreateTableScreen(_schemaService, context);
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            return;
        }

        AnsiConsole.Clear();
        AnsiConsole.Live(screen.Render())
            .StartAsync(context => ScreenRunner.RunFormAsync(
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

    private void RunTableColumns(
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
            _queryService,
            _schemaService,
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
            .StartAsync(context => ScreenRunner.RunTableColumnsAsync(
                screen,
                context,
                loadColumns
            ))
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
            .StartAsync(context => ScreenRunner.RunFormAsync(
                editor,
                context,
                token => editor.SubmitAsync(token),
                host.Render,
                retryAfterError: false
            ))
            .GetAwaiter()
            .GetResult();
        return editor.MutationSucceeded;
    }

    private bool RunColumnEditor(
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
                    token => _schemaService.GetColumnDefinitionAsync(
                        tableLogicalName,
                        existing.LogicalName,
                        retrieveAsIfPublished: true,
                        token,
                        context.BaseLanguage
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
            _schemaService,
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
            .StartAsync(context => ScreenRunner.RunFormAsync(
                screen,
                context,
                token => screen.SubmitAsync(token)
            ))
            .GetAwaiter()
            .GetResult();

        return screen.MutationSucceeded;
    }

    private bool DeleteColumn(
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
                token => _schemaService.GetColumnDeleteDependenciesAsync(
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

            var outcome = ScreenRunner.RunSchemaMutation(
                "Deleting column " + column.LogicalName
                    + " from table " + tableLogicalName
                    + " in solution " + context.SolutionUniqueName
                    + " at " + context.EnvironmentUrl,
                token => _schemaService.DeleteColumnAsync(
                    new DeleteColumnRequest
                    {
                        TableLogicalName = tableLogicalName,
                        ColumnLogicalName = column.LogicalName,
                        ExpectedMetadataId = column.MetadataId,
                        TableMetadataId = tableMetadataId,
                        Context = context
                    },
                    token
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

    private bool PublishTable(
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

            var outcome = ScreenRunner.RunSchemaMutation(
                "Publishing table " + tableLogicalName
                    + " in solution " + context.SolutionUniqueName
                    + " at " + context.EnvironmentUrl,
                token => _schemaService.PublishTableAsync(
                    new PublishTableRequest
                    {
                        TableLogicalName = tableLogicalName,
                        TableMetadataId = tableMetadataId,
                        Context = context
                    },
                    token
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
}
