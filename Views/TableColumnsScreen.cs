using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Models;
using dvtui.Services;

namespace dvtui.Views;

internal enum TableColumnsAction
{
    None,
    Close,
    NewColumn,
    EditColumn,
    DeleteColumn,
    Publish
}

internal sealed class TableColumnsScreen
{
    private const int StatusHeight = 1;
    private const int HintHeight = 1;
    private const int HeaderHeight = 1;
    private const int FooterHeight = 1;

    private readonly DataverseService _service;
    private readonly DataverseEntity _entity;
    private readonly SolutionWriteContext _context;

    private List<DataverseColumn> _columns = [];
    private List<ColumnCapability> _capabilities = [];
    private int _selectedIndex;
    private int _height = 1;
    private bool _loading;
    private string _status = "Loading columns...";
    private TableColumnsAction _pendingAction = TableColumnsAction.None;
    private DataverseColumn? _pendingColumn;
    private int _revision;

    public TableColumnsScreen(
        DataverseService service,
        DataverseEntity entity,
        SolutionWriteContext context
    )
    {
        _service = service;
        _entity = entity;
        _context = context;
    }

    public TableColumnsAction PendingAction => _pendingAction;
    public DataverseColumn? PendingColumn => _pendingColumn;
    public int Revision => Volatile.Read(ref _revision);

    public void SetHeight(int height)
    {
        _height = Math.Max(1, height);
        Touch();
    }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        _loading = true;
        _status = "Loading columns...";
        Touch();
        return Task.Run(async () =>
        {
            try
            {
                var columns = await _service.GetColumnsAsync(
                    _entity.LogicalName,
                    _entity.MetadataId,
                    cancellationToken
                );
                _columns = columns.ToList();
                _capabilities = _columns
                    .Select(ColumnCapabilityPolicy.Evaluate)
                    .ToList();
                _selectedIndex = Math.Min(
                    _selectedIndex,
                    Math.Max(0, _columns.Count - 1)
                );
                _status = _columns.Count == 0
                    ? "No columns found."
                    : $"{_columns.Count} columns.";
                Touch();
            }
            catch( Exception ex )
            {
                _status = ex.Message;
                Touch();
            }
            finally
            {
                _loading = false;
                Touch();
            }
        }, cancellationToken);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if( key.Key == ConsoleKey.Escape
            || key.Key == ConsoleKey.Q
            || key.KeyChar == '\u0003' )
        {
            _pendingAction = TableColumnsAction.Close;
            Touch();
            return;
        }

        if( _loading )
        {
            return;
        }

        switch( key.Key )
        {
            case ConsoleKey.UpArrow:
                if( _columns.Count > 0 )
                {
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    Touch();
                }
                break;
            case ConsoleKey.DownArrow:
                if( _columns.Count > 0 )
                {
                    _selectedIndex = Math.Min(
                        _columns.Count - 1,
                        _selectedIndex + 1
                    );
                    Touch();
                }
                break;
            case ConsoleKey.N:
                if( _columns.Count > 0
                    && _capabilities[_selectedIndex].CanCreateColumn )
                {
                    _pendingAction = TableColumnsAction.NewColumn;
                    Touch();
                }
                break;
            case ConsoleKey.Enter:
            case ConsoleKey.E:
                if( _columns.Count > 0 )
                {
                    var capability = _capabilities[_selectedIndex];
                    if( capability.CanEdit )
                    {
                        _pendingColumn = _columns[_selectedIndex];
                        _pendingAction = TableColumnsAction.EditColumn;
                        Touch();
                    }
                    else
                    {
                        _status = capability.EditReason
                            ?? ColumnCapabilityPolicy.ReasonUnknown;
                        Touch();
                    }
                }
                break;
            case ConsoleKey.D:
                if( _columns.Count > 0 )
                {
                    var capability = _capabilities[_selectedIndex];
                    if( capability.CanDelete )
                    {
                        _pendingColumn = _columns[_selectedIndex];
                        _pendingAction = TableColumnsAction.DeleteColumn;
                        Touch();
                    }
                    else
                    {
                        _status = capability.DeleteReason
                            ?? ColumnCapabilityPolicy.ReasonUnknown;
                        Touch();
                    }
                }
                break;
            case ConsoleKey.P:
                _pendingAction = TableColumnsAction.Publish;
                Touch();
                break;
        }
    }

    private void Touch()
    {
        Interlocked.Increment(ref _revision);
    }

    public IRenderable Render()
    {
        var width = AnsiConsole.Profile.Width;
        var height = AnsiConsole.Profile.Height;
        if( width < 60 || height < 10 )
        {
            return new Text(
                "Enlarge the terminal (60 x 10). Esc: back."
            );
        }

        var panel = new Panel(RenderColumns())
            .Header(
                $"Columns - {_entity.DisplayName} ({_entity.LogicalName})"
            )
            .RoundedBorder()
            .Expand();
        panel.Height = height - 2;

        return new Layout()
            .SplitRows(
                new Layout().Size(1).Update(RenderStatus()),
                new Layout().Update(panel),
                new Layout().Size(1).Update(RenderHint())
            );
    }

    private IRenderable RenderStatus()
    {
        var style = _loading ? Style.Parse("yellow") : Style.Parse("green");
        var prefix = _loading ? "  " : "  ";
        return new Text(prefix + _status, style);
    }

    private IRenderable RenderColumns()
    {
        if( _columns.Count == 0 )
        {
            return new Text(
                "  No columns. Press N to create one.",
                Style.Parse("dim")
            );
        }

        var table = new Table()
            .AddColumn(new TableColumn("Name").NoWrap())
            .AddColumn(new TableColumn("Type").NoWrap())
            .AddColumn(new TableColumn("Req.").NoWrap())
            .AddColumn(new TableColumn("Edit").NoWrap())
            .AddColumn(new TableColumn("Del").NoWrap());

        for( var index = 0; index < _columns.Count; index++ )
        {
            var column = _columns[index];
            var capability = _capabilities[index];
            var isSelected = index == _selectedIndex;
            var rowStyle = isSelected
                ? Style.Parse("black on cyan1")
                : Style.Parse("white");
            var nameStyle = isSelected
                ? Style.Parse("black on cyan1")
                : Style.Parse("white");
            var iconStyle = isSelected
                ? Style.Parse("black on cyan1")
                : Style.Parse("green");
            var noIconStyle = isSelected
                ? Style.Parse("black on cyan1")
                : Style.Parse("red");

            table.AddRow(
                new Text(column.SchemaName, nameStyle),
                new Text(GetKindText(column.Kind), rowStyle),
                new Text(GetRequirementText(column), rowStyle),
                new Text(
                    capability.CanEdit ? "Y" : "N",
                    capability.CanEdit ? iconStyle : noIconStyle
                ),
                new Text(
                    capability.CanDelete ? "Y" : "N",
                    capability.CanDelete ? iconStyle : noIconStyle
                )
            );
        }

        return table;
    }

    private IRenderable RenderHint()
    {
        return new Text(
            "  N: New  E: Edit  D: Delete  P: Publish  R: Refresh  Esc: Back",
            Style.Parse("dim")
        );
    }

    private static string GetKindText(ColumnKind kind)
    {
        return kind switch
        {
            ColumnKind.Text => "Text",
            ColumnKind.MultilineText => "Memo",
            ColumnKind.WholeNumber => "Int",
            ColumnKind.Decimal => "Decimal",
            ColumnKind.YesNo => "Yes/No",
            _ => "Other"
        };
    }

    private static string GetRequirementText(DataverseColumn column)
    {
        return column.RequirementLevel switch
        {
            RequirementLevels.Required => "Req",
            RequirementLevels.Recommended => "Rec",
            _ => "Opt"
        };
    }
}
