using Dvtui.Models;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace Dvtui.Views;

internal sealed class EntityBrowserScreen
{
    private const int RefreshIntervalMilliseconds = 80;
    private const int MinimumWidth = 60;
    private const int MinimumHeight = 10;
    private const int SidebarWidthDivisor = 4;
    private const int PanelHorizontalOverhead = 4;
    private readonly Func<CancellationToken, Task<List<DataverseEntity>>> _load;
    private List<DataverseEntity> _entities = [];
    private ScrollableContent? _details;
    private string _status = "Loading tables...";
    private int _selected;
    private int _firstVisible;
    private bool _detailsFocused;

    private EntityBrowserScreen(Func<CancellationToken, Task<List<DataverseEntity>>> load)
    {
        _load = load;
    }

    public static void Show(Func<CancellationToken, Task<List<DataverseEntity>>> load)
    {
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            AnsiConsole.WriteLine("The table browser needs a terminal.");
            return;
        }

        var screen = new EntityBrowserScreen(load);
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

                while( Console.KeyAvailable )
                {
                    var key = Console.ReadKey(intercept: true);
                    if( key.Key == ConsoleKey.Escape || key.KeyChar == '\u0003' )
                    {
                        return;
                    }

                    if( key.Key == ConsoleKey.R && pendingLoad == null )
                    {
                        pendingLoad = StartLoad(cancellation.Token);
                    }
                    else
                    {
                        HandleKey(key);
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
            if( pendingLoad != null )
            {
                await CompleteLoadAsync(pendingLoad, cancellation.Token);
            }
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
            SelectEntity();
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

    private void HandleKey(ConsoleKeyInfo key)
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
                SelectEntity();
            }
        }
    }

    private void SelectEntity()
    {
        _details = _entities.Count == 0
            ? null
            : new ScrollableContent(EntityDetailsView.Create(_entities[_selected]));
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
            return new Text("Enlarge the terminal (60 x 10). Esc: quit.");
        }

        var sidebarWidth = width / SidebarWidthDivisor;
        var list = new Panel(RenderEntities(sidebarWidth - PanelHorizontalOverhead))
            .Header("Tables")
            .RoundedBorder()
            .BorderColor(_detailsFocused ? Color.Grey : Color.Cyan1)
            .Expand();
        list.Height = height - 2;

        if( _details != null )
        {
            _details.Height = GetPageSize();
        }

        var details = new Panel(_details ?? (IRenderable)new Text(_status))
            .Header("Table details")
            .RoundedBorder()
            .BorderColor(_detailsFocused ? Color.Cyan1 : Color.Grey)
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
                    "↑↓: move | PgUp/PgDn | Tab: pane | R: reload | Esc: quit"
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
                ? new Style(Color.Black, Color.Cyan1)
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
