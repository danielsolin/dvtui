using dvtui.Models;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class EntityBrowserScreen
{
    private const int RefreshIntervalMilliseconds = 80;
    private const int MinimumWidth = 60;
    private const int MinimumHeight = 10;
    private const int SidebarWidthDivisor = 4;
    private const int PanelHorizontalOverhead = 4;
    private const int ShutdownTimeoutMilliseconds = 2000;
    private readonly Func<CancellationToken, Task<List<DataverseEntity>>> _load;
    private readonly Func<string, CancellationToken, Task<DataverseEntityDetails>>
        _loadDetails;
    private List<DataverseEntity> _entities = [];
    private ScrollableContent? _details;
    private string _status = "Loading tables...";
    private string? _detailsRequest;
    private Task<DataverseEntityDetails>? _pendingDetails;
    private CancellationTokenSource? _detailsCancellation;
    private int _selected;
    private int _firstVisible;
    private bool _detailsFocused;

    private EntityBrowserScreen(
        Func<CancellationToken, Task<List<DataverseEntity>>> load,
        Func<string, CancellationToken, Task<DataverseEntityDetails>> loadDetails
    )
    {
        _load = load;
        _loadDetails = loadDetails;
    }

    public static void Show(
        Func<CancellationToken, Task<List<DataverseEntity>>> load,
        Func<string, CancellationToken, Task<DataverseEntityDetails>> loadDetails
    )
    {
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            AnsiConsole.WriteLine("The table browser needs a terminal.");
            return;
        }

        var screen = new EntityBrowserScreen(load, loadDetails);
        var previousControlCMode = Console.TreatControlCAsInput;
        AnsiConsole.AlternateScreen(() =>
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Clear();
            try
            {
                AnsiConsole.Live(screen.Render())
                    .StartAsync(screen.RunAsync)
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

    private async Task RunAsync(LiveDisplayContext context)
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
                    if( key.Key == ConsoleKey.Q || key.KeyChar == '\u0003' )
                    {
                        return;
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

    private Task<List<DataverseEntity>> StartLoad(CancellationToken cancellationToken)
    {
        _status = "Loading tables...";
        return Task.Run(() => _load(cancellationToken), cancellationToken);
    }

    private async Task CompleteLoadAsync(
        Task<List<DataverseEntity>> pendingLoad,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var entities = await pendingLoad;
            _entities = entities.Where(entity => entity.IsCustomizable == true).ToList();
            _selected = 0;
            _firstVisible = 0;
            SelectEntity(cancellationToken);
            _status = _entities.Count == 0
                ? "No customizable tables found. R: reload."
                : $"{_entities.Count} tables | Customizable only";
        }
        catch( OperationCanceledException ) when( cancellationToken.IsCancellationRequested )
        {
        }
        catch( Exception ex )
        {
            _status = "Could not load tables. Press R to retry.";
            _details = new ScrollableContent(new Text(ex.Message));
        }
    }

    private async Task CompleteDetailsLoadAsync(
        Task<DataverseEntityDetails> pendingDetails,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var details = await pendingDetails;
            if( cancellationToken.IsCancellationRequested )
            {
                return;
            }

            if( _entities.Count == 0
                || _detailsRequest != _entities[_selected].LogicalName )
            {
                return;
            }

            _details = new ScrollableContent(
                EntityDetailsView.Create(_entities[_selected], details.Fields)
            );
        }
        catch( OperationCanceledException )
        {
        }
        catch( Exception ex )
        {
            _details = new ScrollableContent(
                new Text($"Could not load fields: {ex.Message}")
            );
        }
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
            : Math.Max(0, _entities.Count - 1);
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
        else if( _entities.Count > 0 )
        {
            var selected = Math.Clamp(next, 0, _entities.Count - 1);
            if( selected != _selected )
            {
                _selected = selected;
                SelectEntity(cancellationToken);
            }
        }
    }

    private void SelectEntity(CancellationToken cancellationToken)
    {
        _detailsCancellation?.Cancel();
        if( _entities.Count == 0 )
        {
            _details = null;
            _detailsRequest = null;
            _pendingDetails = null;
            return;
        }

        var entity = _entities[_selected];
        _detailsRequest = entity.LogicalName;
        _details = new ScrollableContent(new Text("Loading fields..."));
        _detailsCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        _pendingDetails = Task.Run(
            () => _loadDetails(entity.LogicalName, _detailsCancellation.Token),
            _detailsCancellation.Token
        );
    }

    private static int GetPageSize()
    {
        return Math.Max(1, AnsiConsole.Profile.Height - 4);
    }

    private IRenderable Render()
    {
        var width = AnsiConsole.Profile.Width;
        var height = AnsiConsole.Profile.Height;
        if( width < MinimumWidth || height < MinimumHeight )
        {
            return new Text("Enlarge the terminal (60 x 10). Q: quit.");
        }

        var sidebarWidth = width / SidebarWidthDivisor;
        var list = new Panel(RenderEntities(sidebarWidth - PanelHorizontalOverhead))
            .Header("Tables")
            .RoundedBorder()
            .BorderColor(_detailsFocused ? Color.Grey : Color.Grey58)
            .Expand();
        list.Height = height - 2;

        if( _details != null )
        {
            _details.Height = GetPageSize();
        }

        var details = new Panel(_details ?? (IRenderable)new Text(_status))
            .Header("Table details")
            .RoundedBorder()
            .BorderColor(_detailsFocused ? Color.Grey58 : Color.Grey)
            .Expand();
        details.Height = height - 2;

        return new Layout()
            .SplitRows(
                new Layout().Size(1).Update(new Text($"DVTUI | {_status}")),
                new Layout().SplitColumns(
                    new Layout().Size(sidebarWidth).Update(list),
                    new Layout().Update(details)
                ),
                new Layout().Size(1).Update(new Text(
                    "↑↓: move | PgUp/PgDn | Tab: pane | R: reload | Q: quit"
                ))
            );
    }

    private IRenderable RenderEntities(int width)
    {
        var pageSize = GetPageSize();
        _firstVisible = Math.Clamp(
            _firstVisible,
            Math.Max(0, _selected - pageSize + 1),
            _selected
        );
        var rows = new List<IRenderable>();
        for( var index = _firstVisible;
            index < Math.Min(_entities.Count, _firstVisible + pageSize);
            index++ )
        {
            var selected = index == _selected;
            var style = selected
                ? new Style(Color.White, Color.LightSlateGrey)
                : Style.Plain;
            var prefix = selected ? "> " : "  ";
            var label = prefix + _entities[index].LogicalName;
            if( label.Length > width )
            {
                label = label[..(width - 1)] + "…";
            }

            rows.Add(new Text(label, style));
        }

        return rows.Count == 0 ? new Text(_status) : new Rows(rows);
    }
}
