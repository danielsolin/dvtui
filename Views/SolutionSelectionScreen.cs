using dvtui.Models;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class SolutionSelectionScreen
{
    private const int RefreshIntervalMilliseconds = 80;
    private const int MinimumWidth = 60;
    private const int MinimumHeight = 10;
    private const int ShutdownTimeoutMilliseconds = 2000;
    private readonly Func<CancellationToken, Task<List<DataverseSolution>>>
        _load;
    private List<DataverseSolution> _solutions = [];
    private ScrollableContent? _details;
    private string _status = "Loading solutions...";
    private bool _loadFailed;
    private CancellationTokenSource? _loadCancellation;
    private int _selected;
    private int _firstVisible;
    private bool _detailsFocused;

    private SolutionSelectionScreen(
        Func<CancellationToken, Task<List<DataverseSolution>>> load
    )
    {
        _load = load;
    }

    public static DataverseSolution? Show(
        Func<CancellationToken, Task<List<DataverseSolution>>> load
    )
    {
        if( Console.IsInputRedirected || Console.IsOutputRedirected )
        {
            AnsiConsole.WriteLine(
                "Solution selection needs an interactive terminal."
            );
            return null;
        }

        var screen = new SolutionSelectionScreen(load);
        var previousControlCMode = Console.TreatControlCAsInput;
        var selected = new DataverseSolution?[1];
        AnsiConsole.AlternateScreen(() =>
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Clear();
            try
            {
                AnsiConsole.Live(screen.Render())
                    .StartAsync(context => screen.RunAsync(context, selected))
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                AnsiConsole.Cursor.Show();
                Console.TreatControlCAsInput = previousControlCMode;
            }
        });

        return selected[0];
    }

    private async Task RunAsync(
        LiveDisplayContext context,
        DataverseSolution?[] selected
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

                while( Console.KeyAvailable )
                {
                    var key = Console.ReadKey(intercept: true);
                    if( key.Key == ConsoleKey.Q
                        || key.Key == ConsoleKey.Escape
                        || key.KeyChar == '\u0003' )
                    {
                        return;
                    }

                    if( key.Key == ConsoleKey.R && pendingLoad == null )
                    {
                        pendingLoad = StartLoad(cancellation.Token);
                    }
                    else if(
                        key.Key == ConsoleKey.Enter
                        && _solutions.Count > 0
                        && pendingLoad == null
                        && !_loadFailed
                    )
                    {
                        selected[0] = _solutions[_selected];
                        return;
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
                await WaitForCleanupAsync(
                    CompleteLoadAsync(pendingLoad, cancellation.Token)
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

    private Task<List<DataverseSolution>> StartLoad(CancellationToken token)
    {
        _status = "Loading solutions...";
        _loadFailed = false;
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            token
        );
        return Task.Run(
            () => _load(_loadCancellation!.Token),
            _loadCancellation.Token
        );
    }

    private async Task CompleteLoadAsync(
        Task<List<DataverseSolution>> pendingLoad,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var solutions = await pendingLoad;
            _solutions = solutions;
            _loadFailed = false;
            _selected = 0;
            _firstVisible = 0;
            _details = null;
            _status = _solutions.Count == 0
                ? "No visible solutions found. R: reload | Q: quit"
                : $"{_solutions.Count} visible solutions | Enter: open";
        }
        catch( OperationCanceledException ) when( cancellationToken.IsCancellationRequested )
        {
        }
        catch( Exception ex )
        {
            _loadFailed = true;
            _solutions = [];
            _status = "Could not load solutions. R: retry | Q: quit";
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
            : Math.Max(0, _solutions.Count - 1);
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
        else if( _solutions.Count > 0 )
        {
            _selected = Math.Clamp(next, 0, _solutions.Count - 1);
        }
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

        var sidebarWidth = TuiLayout.GetSidebarWidth(width);
        var list = new Panel(
            RenderSolutions(sidebarWidth - TuiLayout.PanelHorizontalOverhead)
        )
            .Header("Solutions")
            .RoundedBorder()
            .BorderColor(_detailsFocused ? Color.Grey : Color.Grey58)
            .Expand();
        list.Height = height - 2;

        if( _details != null )
        {
            _details.Height = GetPageSize();
        }

        var details = new Panel(RenderDetails())
            .Header("Solution details")
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

    private IRenderable RenderDetails()
    {
        if( _details != null )
        {
            return _details;
        }

        if( _solutions.Count > 0 && _selected < _solutions.Count )
        {
            return RenderSolutionDetails(_solutions[_selected]);
        }

        return new Text(_status);
    }

    private static IRenderable RenderSolutionDetails(DataverseSolution solution)
    {
        var table = new Table()
            .Border(TableBorder.Minimal)
            .ShowRowSeparators()
            .HideHeaders()
            .AddColumn(new TableColumn("Property").NoWrap())
            .AddColumn("Value");

        AddRow(table, "Friendly name", solution.FriendlyName);
        AddRow(table, "Unique name", solution.UniqueName);
        AddRow(table, "Version", solution.Version);
        AddRow(table, "Managed", FormatBoolean(solution.IsManaged));
        AddRow(table, "Description", solution.Description);
        return table;
    }

    private static void AddRow(Table table, string label, string? value)
    {
        table.AddRow(
            new Text(label, new Style(Color.Grey58)),
            new Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
        );
    }

    private static string FormatBoolean(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            null => "Unknown"
        };
    }

    private IRenderable RenderSolutions(int width)
    {
        var pageSize = GetPageSize();
        _firstVisible = Math.Clamp(
            _firstVisible,
            Math.Max(0, _selected - pageSize + 1),
            _selected
        );
        var rows = new List<IRenderable>();
        for( var index = _firstVisible;
            index < Math.Min(_solutions.Count, _firstVisible + pageSize);
            index++ )
        {
            var selected = index == _selected;
            var style = selected
                ? new Style(Color.White, Color.LightSlateGrey)
                : Style.Plain;
            var prefix = selected ? "> " : "  ";
            var label = prefix + GetListLabel(_solutions[index]);
            if( label.Length > width )
            {
                label = label[..(width - 1)] + "…";
            }

            rows.Add(new Text(label, style));
        }

        return rows.Count == 0 ? new Text(_status) : new Rows(rows);
    }

    private static string GetListLabel(DataverseSolution solution)
    {
        return string.IsNullOrWhiteSpace(solution.FriendlyName)
            ? solution.UniqueName
            : solution.FriendlyName;
    }
}
