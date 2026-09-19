using dvtui.Models;
using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal enum SolutionBrowserResult
{
    BackToSolutions,
    Quit,
    OpenColumns,
    CreateTable
}

internal sealed class SolutionBrowserSelection
{
    public SolutionBrowserResult Result { get; init; }
    public DataverseEntity? Entity { get; init; }
}

internal sealed class SolutionBrowserScreen
{
    private const int RefreshIntervalMilliseconds = 80;
    private const int MinimumWidth = 60;
    private const int MinimumHeight = 10;
    private const int ShutdownTimeoutMilliseconds = 2000;
    private readonly DataverseSolution _solution;
    private readonly Func<CancellationToken, Task<List<DataverseSolutionComponent>>>
        _load;
    private readonly Func<string, Guid, CancellationToken, Task<DataverseEntityDetails>>
        _loadDetails;
    private readonly bool _canWrite;
    private readonly string _writeDisabledReason;
    private List<DataverseSolutionComponent> _components = [];
    private ScrollableContent? _details;
    private string _status = "Loading components...";
    private bool _loadFailed;
    private int _generation;
    private (int Generation, Guid ComponentId)? _detailsRequest;
    private Task<DataverseEntityDetails>? _pendingDetails;
    private CancellationTokenSource? _detailsCancellation;
    private CancellationTokenSource? _loadCancellation;
    private Guid? _reloadSelectionId;
    private int _selected;
    private int _firstVisible;
    private bool _detailsFocused;

    private SolutionBrowserScreen(
        DataverseSolution solution,
        Func<CancellationToken, Task<List<DataverseSolutionComponent>>> load,
        Func<string, Guid, CancellationToken, Task<DataverseEntityDetails>> loadDetails,
        bool canWrite,
        string? writeDisabledReason
    )
    {
        _solution = solution;
        _load = load;
        _loadDetails = loadDetails;
        _canWrite = canWrite;
        _writeDisabledReason = string.IsNullOrWhiteSpace(writeDisabledReason)
            ? "Solution is read-only."
            : writeDisabledReason;
    }

    public static SolutionBrowserSelection Show(
        DataverseSolution solution,
        Func<Guid, CancellationToken, Task<List<DataverseSolutionComponent>>>
            load,
        Func<string, Guid, CancellationToken, Task<DataverseEntityDetails>> loadDetails,
        bool canWrite = true,
        string? writeDisabledReason = null
    )
    {
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            AnsiConsole.WriteLine(
                "The solution browser needs an interactive terminal."
            );
            return new SolutionBrowserSelection
            {
                Result = SolutionBrowserResult.Quit
            };
        }

        var screen = new SolutionBrowserScreen(
            solution,
            token => load(solution.Id, token),
            loadDetails,
            canWrite,
            writeDisabledReason
        );
        SessionLog.Info(
            "UI.SolutionBrowser",
            "Screen started solution=" + solution.UniqueName
                + " id=" + solution.Id
                + " canWrite=" + canWrite
        );
        var result = new SolutionBrowserSelection[1];
        AnsiConsole.Clear();
        AnsiConsole.Live(screen.Render())
            .StartAsync(
                context => screen.RunAsync(context, result)
            )
            .GetAwaiter()
            .GetResult();

        SessionLog.Info(
            "UI.SolutionBrowser",
            "Screen completed result=" + (result[0]?.Result.ToString() ?? "none")
        );
        return result[0];
    }

    private async Task RunAsync(
        LiveDisplayContext context,
        SolutionBrowserSelection[] result
    )
    {
        using var cancellation = new CancellationTokenSource();
        var pendingLoad = StartLoad(cancellation.Token);
        var lastSize = (Width: 0, Height: 0);
        try
        {
            while( true )
            {
                var refresh = false;
                if( pendingLoad != null && pendingLoad.IsCompleted )
                {
                    await CompleteLoadAsync(pendingLoad, cancellation.Token);
                    pendingLoad = null;
                    refresh = true;
                }

                if( _pendingDetails != null && _pendingDetails.IsCompleted )
                {
                    await CompleteDetailsLoadAsync(
                        _pendingDetails,
                        cancellation.Token
                    );
                    _pendingDetails = null;
                    refresh = true;
                }

                while( Console.KeyAvailable )
                {
                    var key = Console.ReadKey(intercept: true);
                    SessionLog.Key(
                        "SolutionBrowserScreen",
                        key,
                        "pendingLoad=" + (pendingLoad != null)
                            + " details=" + (_pendingDetails != null)
                    );
                    if( key.Key == ConsoleKey.Q || key.KeyChar == '\u0003' )
                    {
                        result[0] = new SolutionBrowserSelection
                        {
                            Result = SolutionBrowserResult.Quit
                        };
                        return;
                    }

                    if( key.Key == ConsoleKey.Escape )
                    {
                        result[0] = new SolutionBrowserSelection
                        {
                            Result = SolutionBrowserResult.BackToSolutions
                        };
                        return;
                    }

                    if( key.Key == ConsoleKey.N )
                    {
                        if( !_canWrite )
                        {
                            _status = _writeDisabledReason;
                            refresh = true;
                            continue;
                        }

                        result[0] = new SolutionBrowserSelection
                        {
                            Result = SolutionBrowserResult.CreateTable
                        };
                        return;
                    }

                    if( key.Key == ConsoleKey.Enter )
                    {
                        var entity = GetSelectedEntity();
                        if( entity != null )
                        {
                            result[0] = new SolutionBrowserSelection
                            {
                                Result = SolutionBrowserResult.OpenColumns,
                                Entity = entity
                            };
                            return;
                        }
                    }

                    if( key.Key == ConsoleKey.R && pendingLoad == null )
                    {
                        pendingLoad = StartLoad(cancellation.Token);
                    }
                    else
                    {
                        HandleKey(key, cancellation.Token);
                    }

                    refresh = true;
                }

                var size = (AnsiConsole.Profile.Width, AnsiConsole.Profile.Height);
                if( refresh || size != lastSize )
                {
                    context.UpdateTarget(Render());
                    lastSize = size;
                }

                await Task.Delay(RefreshIntervalMilliseconds);
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            _detailsCancellation?.Cancel();
            if( pendingLoad != null )
            {
                await WaitForCleanupAsync(
                    CompleteLoadAsync(pendingLoad, cancellation.Token)
                );
            }
            if( _pendingDetails != null )
            {
                await WaitForCleanupAsync(
                    CompleteDetailsLoadAsync(
                        _pendingDetails,
                        cancellation.Token
                    )
                );
            }
        }
    }

    private static async Task WaitForCleanupAsync(Task cleanup)
    {
        var timeout = Task.Delay(ShutdownTimeoutMilliseconds);
        if( await Task.WhenAny(cleanup, timeout) == cleanup )
        {
            await cleanup;
        }
    }

    private Task<List<DataverseSolutionComponent>> StartLoad(
        CancellationToken cancellationToken
    )
    {
        _reloadSelectionId = _components.Count > 0
            && _selected < _components.Count
            ? _components[_selected].Id
            : null;
        _generation++;
        _status = "Loading components...";
        _loadFailed = false;
        _detailsCancellation?.Cancel();
        _details = null;
        _detailsRequest = null;
        _pendingDetails = null;
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        return Task.Run(
            () => _load(_loadCancellation!.Token),
            _loadCancellation.Token
        );
    }

    private async Task CompleteLoadAsync(
        Task<List<DataverseSolutionComponent>> pendingLoad,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var components = await pendingLoad;
            _components = components
                .OrderBy(component => component.ComponentType)
                .ThenBy(component => GetSortName(component), StringComparer.Ordinal)
                .ThenBy(component => component.Id)
                .ToList();
            var refreshedIndex = _reloadSelectionId.HasValue
                ? _components.FindIndex(
                    component => component.Id == _reloadSelectionId.Value
                )
                : -1;
            _selected = refreshedIndex >= 0 ? refreshedIndex : 0;
            _firstVisible = 0;
            _reloadSelectionId = null;
            _loadFailed = false;
            SelectComponent(cancellationToken);
            _status = _components.Count == 0
                ? "No components in this solution. N: new table | R: reload | Esc: solutions"
                : $"{_components.Count} component rows";
            SessionLog.Info(
                "UI.SolutionBrowser",
                "Loaded components count=" + _components.Count
            );
        }
        catch( OperationCanceledException ) when( cancellationToken.IsCancellationRequested )
        {
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "UI.SolutionBrowser",
                ex,
                "Loading solution components failed"
            );
            _loadFailed = true;
            _components = [];
            _status = "Could not load components. R: retry | Esc: solutions";
            _details = new ScrollableContent(new Text(ex.Message));
        }
    }

    private async Task CompleteDetailsLoadAsync(
        Task<DataverseEntityDetails> pendingDetails,
        CancellationToken cancellationToken
    )
    {
        var request = _detailsRequest;
        try
        {
            var details = await pendingDetails;
            if( !IsCurrentDetailsRequest(request) )
            {
                return;
            }

            _details = new ScrollableContent(
                RenderComponentDetails(_components[_selected], details)
            );
        }
        catch( OperationCanceledException )
        {
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "UI.SolutionBrowser",
                ex,
                "Loading component details failed"
            );
            if( !IsCurrentDetailsRequest(request) )
            {
                return;
            }

            _details = new ScrollableContent(
                new Text($"Could not load table details: {ex.Message}")
            );
        }
    }

    private bool IsCurrentDetailsRequest(
        (int Generation, Guid ComponentId)? request
    )
    {
        return request != null
            && request.Value.Generation == _generation
            && _components.Count > 0
            && _selected < _components.Count
            && request.Value.ComponentId == _components[_selected].Id;
    }

    private DataverseEntity? GetSelectedEntity()
    {
        if( _components.Count == 0 )
        {
            return null;
        }

        var component = _components[_selected];
        if( component.ComponentType != SolutionComponentTypes.Entity
            || component.Entity == null )
        {
            return null;
        }

        return component.Entity;
    }

    private void HandleKey(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        if( key.Key == ConsoleKey.Tab )
        {
            _detailsFocused = !_detailsFocused;
            return;
        }

        var current = _detailsFocused ? _details?.Offset ?? 0 : _selected;
        var last = _detailsFocused
            ? _details?.MaximumOffset ?? 0
            : Math.Max(0, _components.Count - 1);
        var pageSize = GetPageSize();
        var next = key.Key switch
        {
            ConsoleKey.UpArrow => current - 1,
            ConsoleKey.DownArrow => current + 1,
            ConsoleKey.PageUp => current - pageSize,
            ConsoleKey.PageDown => current + pageSize,
            ConsoleKey.Home => 0,
            ConsoleKey.End => last,
            _ => current
        };

        if( _detailsFocused && _details != null )
        {
            _details.Offset = Math.Clamp(next, 0, last);
        }
        else if( _components.Count > 0 )
        {
            var selected = Math.Clamp(next, 0, _components.Count - 1);
            if( selected != _selected )
            {
                _selected = selected;
                SelectComponent(cancellationToken);
            }
        }
    }

    private void SelectComponent(CancellationToken cancellationToken)
    {
        _detailsCancellation?.Cancel();
        _detailsCancellation?.Dispose();
        _detailsCancellation = null;
        if( _components.Count == 0 )
        {
            _details = null;
            _detailsRequest = null;
            _pendingDetails = null;
            return;
        }

        var component = _components[_selected];
        if( component.ComponentType != SolutionComponentTypes.Entity
            || component.ObjectId == null )
        {
            _details = new ScrollableContent(
                ComponentDetailsView.Create(component)
            );
            _detailsRequest = null;
            _pendingDetails = null;
            return;
        }

        var metadataId = component.ObjectId.Value;
        var logicalName = component.Entity?.LogicalName ?? string.Empty;
        if( string.IsNullOrWhiteSpace(logicalName) )
        {
            _details = new ScrollableContent(
                ComponentDetailsView.Create(component)
            );
            _detailsRequest = null;
            _pendingDetails = null;
            return;
        }

        _detailsRequest = (_generation, component.Id);
        _details = new ScrollableContent(new Text("Loading table details..."));
        _detailsCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        _pendingDetails = Task.Run(
            () => _loadDetails(logicalName, metadataId, _detailsCancellation!.Token),
            _detailsCancellation.Token
        );
    }

    private static int GetPageSize()
    {
        return Math.Max(1, AnsiConsole.Profile.Height - 4);
    }

    private static string GetSortName(DataverseSolutionComponent component)
    {
        if( component.Entity != null )
        {
            return string.IsNullOrWhiteSpace(component.Entity.LogicalName)
                ? component.Entity.DisplayName
                : component.Entity.LogicalName;
        }

        return component.ObjectId?.ToString() ?? string.Empty;
    }

    private IRenderable Render()
    {
        var width = AnsiConsole.Profile.Width;
        var height = AnsiConsole.Profile.Height;
        if( width < MinimumWidth || height < MinimumHeight )
        {
            return new Text(
                "Enlarge the terminal (60 x 10). Q: quit | Esc: solutions."
            );
        }

        var sidebarWidth = TuiLayout.GetSidebarWidth(width);
        var list = new Panel(
            RenderComponents(sidebarWidth - TuiLayout.PanelHorizontalOverhead)
        )
            .Header("Components")
            .RoundedBorder()
            .BorderColor(_detailsFocused ? Color.Grey : Color.Grey58)
            .Expand();
        list.Height = height - 2;

        if( _details != null )
        {
            _details.Height = GetPageSize();
        }

        var details = new Panel(RenderDetails())
            .Header("Component details")
            .RoundedBorder()
            .BorderColor(_detailsFocused ? Color.Grey58 : Color.Grey)
            .Expand();
        details.Height = height - 2;

        var status = "DVTUI | " + _status
            + " | Solution: " + _solution.UniqueName;
        if( !_canWrite )
        {
            status += " | Writes disabled: " + _writeDisabledReason;
        }

        var newTableHint = _canWrite
            ? "N: new"
            : "N: new (disabled)";
        return new Layout()
            .SplitRows(
                new Layout().Size(1).Update(new Text(status)),
                new Layout().SplitColumns(
                    new Layout().Size(sidebarWidth).Update(list),
                    new Layout().Update(details)
                ),
                new Layout().Size(1).Update(new Text(
                    $"↑↓: move | PgUp/PgDn | Tab: pane | {newTableHint} | "
                    + "R: reload | Esc: solutions | Q: quit"
                ))
            );
    }

    private IRenderable RenderDetails()
    {
        if( _details != null )
        {
            return _details;
        }

        if( _loadFailed )
        {
            return new Text(_status);
        }

        if( _components.Count > 0 && _selected < _components.Count )
        {
            var component = _components[_selected];
            return component.Entity != null
                ? new Text("Loading table details...")
                : ComponentDetailsView.Create(component);
        }

        return new Text(_status);
    }

    private IRenderable RenderComponents(int width)
    {
        var pageSize = GetPageSize();
        _firstVisible = Math.Clamp(
            _firstVisible,
            Math.Max(0, _selected - pageSize + 1),
            _selected
        );
        var rows = new List<IRenderable>();
        for( var index = _firstVisible;
            index < Math.Min(_components.Count, _firstVisible + pageSize);
            index++ )
        {
            var selected = index == _selected;
            var style = selected
                ? new Style(Color.White, Color.LightSlateGrey)
                : Style.Plain;
            var prefix = selected ? "> " : "  ";
            var label = prefix + GetListLabel(_components[index]);
            if( label.Length > width )
            {
                label = label[..(width - 1)] + "…";
            }

            rows.Add(new Text(label, style));
        }

        return rows.Count == 0 ? new Text(_status) : new Rows(rows);
    }

    private static string GetListLabel(DataverseSolutionComponent component)
    {
        var type = SolutionComponentTypes.GetDisplayName(component.ComponentType);
        var name = component.Entity != null
            ? component.Entity.LogicalName
            : component.ObjectId?.ToString() ?? string.Empty;
        return $"{type}: {name}";
    }

    private IRenderable RenderComponentDetails(
        DataverseSolutionComponent component,
        DataverseEntityDetails details
    )
    {
        var rows = new List<IRenderable>
        {
            ComponentDetailsView.CreateMembership(component)
        };
        if( component.ResolutionError != null )
        {
            rows.Add(new Text(component.ResolutionError, new Style(Color.Yellow)));
        }

        rows.Add(EntityDetailsView.Create(details.Entity, details.Fields));
        rows.Add(new Text(
            "Environment columns (not a solution membership list)",
            TuiColors.SecondaryText
        ));
        return new Rows(rows);
    }
}
