using System.Globalization;
using dvtui.Models;
using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal enum TableColumnsAction
{
    None,
    Close,
    NewColumn,
    SaveColumn,
    DeleteColumn,
    Publish
}

internal sealed class TableColumnsScreen
{
    private readonly DataverseService _service;
    private readonly DataverseEntity _entity;
    private readonly SolutionWriteContext _context;
    private bool _sessionPendingChanges;

    private List<DataverseColumn> _columns = [];
    private List<ColumnCapability> _capabilities = [];
    private int _hiddenSystemColumnCount;
    private int _selectedIndex;
    private int _firstVisible;
    private int _listHeight = 1;
    private bool _detailsFocused;
    private bool _loading;
    private string _status = "Loading columns...";
    private TableColumnsAction _pendingAction = TableColumnsAction.None;
    private DataverseColumn? _pendingColumn;
    private ColumnEditorScreen? _editor;
    private int _loadGeneration;
    private int _revision;

    public TableColumnsScreen(
        DataverseService service,
        DataverseEntity entity,
        SolutionWriteContext context,
        bool sessionPendingChanges = false
    )
    {
        _service = service;
        _entity = entity;
        _context = context;
        _sessionPendingChanges = sessionPendingChanges;
    }

    public TableColumnsAction PendingAction => _pendingAction;
    public DataverseColumn? PendingColumn => _pendingColumn;
    public ColumnEditorScreen? Editor => _editor;
    public int Revision => Volatile.Read(ref _revision);
    public bool Loading => _loading;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        SessionLog.Info(
            "UI.TableColumns",
            "Loading columns table=" + _entity.LogicalName
                + " metadataId=" + _entity.MetadataId
        );
        var generation = Interlocked.Increment(ref _loadGeneration);
        _loading = true;
        _status = "Loading columns...";
        Touch();
        return LoadCoreAsync(generation, cancellationToken);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if( Progress.IsActive )
        {
            return;
        }

        if( _editor != null )
        {
            _editor.HandleKey(key);
            Touch();
            if( _editor.PendingAction == FormAction.Close )
            {
                _editor = null;
                _detailsFocused = false;
                _status = BuildColumnStatus();
                Touch();
            }
            else if( _editor.PendingAction == FormAction.Submit )
            {
                _pendingAction = TableColumnsAction.SaveColumn;
                Touch();
            }

            return;
        }

        if( key.Key == ConsoleKey.Escape )
        {
            if( _detailsFocused )
            {
                _detailsFocused = false;
                Touch();
                return;
            }

            _pendingAction = TableColumnsAction.Close;
            Touch();
            return;
        }

        if( key.Key == ConsoleKey.Q || key.KeyChar == '\u0003' )
        {
            _pendingAction = TableColumnsAction.Close;
            Touch();
            return;
        }

        if( _loading )
        {
            return;
        }

        if( key.Key == ConsoleKey.Tab )
        {
            _detailsFocused = !_detailsFocused;
            Touch();
            return;
        }

        if( _detailsFocused )
        {
            return;
        }

        switch( key.Key )
        {
            case ConsoleKey.UpArrow:
                MoveSelection(-1);
                break;
            case ConsoleKey.DownArrow:
                MoveSelection(1);
                break;
            case ConsoleKey.PageUp:
                MoveSelection(-Math.Max(1, _listHeight));
                break;
            case ConsoleKey.PageDown:
                MoveSelection(Math.Max(1, _listHeight));
                break;
            case ConsoleKey.Home:
                SelectIndex(0);
                break;
            case ConsoleKey.End:
                SelectIndex(Math.Max(0, _columns.Count - 1));
                break;
            case ConsoleKey.N:
                BeginCreate();
                break;
            case ConsoleKey.Enter:
            case ConsoleKey.E:
                BeginEdit();
                break;
            case ConsoleKey.D:
                BeginDelete();
                break;
            case ConsoleKey.P:
                BeginPublish();
                break;
        }
    }

    public void ResetAction()
    {
        _pendingAction = TableColumnsAction.None;
        _pendingColumn = null;
        Touch();
    }

    public void CompleteEdit(bool succeeded)
    {
        if( succeeded )
        {
            _editor = null;
            _sessionPendingChanges = true;
            _status = "Column saved. Reloading columns...";
        }
        else
        {
            _editor?.ResetForRetry();
            _status = _editor?.Status ?? "Column could not be saved.";
        }

        ResetAction();
    }

    public void SetPendingChanges(bool value)
    {
        _sessionPendingChanges = value;
        Touch();
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

        _listHeight = Math.Max(1, height - 6);
        KeepSelectionVisible();
        var sidebarWidth = TuiLayout.GetSidebarWidth(width);
        var list = new Panel(
            RenderColumns(sidebarWidth - TuiLayout.PanelHorizontalOverhead)
        )
            .Header("Columns")
            .RoundedBorder()
            .BorderColor(
                _detailsFocused ? Color.Grey : Color.Grey58
            )
            .Expand();
        var details = new Panel(RenderDetails())
            .Header("Column details")
            .RoundedBorder()
            .BorderColor(
                _detailsFocused ? Color.Grey58 : Color.Grey
            )
            .Expand();
        list.Height = height - 2;
        details.Height = height - 2;

        return new Layout()
            .SplitRows(
                new Layout().Size(1).Update(RenderStatus()),
                new Layout().SplitColumns(
                    new Layout().Size(sidebarWidth).Update(list),
                    new Layout().Update(details)
                ),
                new Layout().Size(1).Update(RenderHint())
            );
    }

    private async Task LoadCoreAsync(
        int generation,
        CancellationToken cancellationToken
    )
    {
        var selectedColumn = _columns.Count > 0
            && _selectedIndex < _columns.Count
            ? _columns[_selectedIndex]
            : null;
        try
        {
            var columns = await Progress.Show(
                "Loading columns...",
                cancellationToken,
                token => _service.GetColumnsAsync(
                    _entity.LogicalName,
                    _entity.MetadataId,
                    token
                )
            );
            if( generation != Volatile.Read(ref _loadGeneration) )
            {
                return;
            }

            _hiddenSystemColumnCount = columns.Count(
                column => column.IsCustom != true
            );
            _columns = columns
                .OrderBy(column => column.IsCustom == true ? 0 : 1)
                .ThenBy(column => column.SchemaName, StringComparer.Ordinal)
                .ToList();
            _capabilities = _columns
                .Select(ColumnCapabilityPolicy.Evaluate)
                .ToList();
            var refreshedIndex = selectedColumn == null
                ? -1
                : _columns.FindIndex(column =>
                    selectedColumn.MetadataId != Guid.Empty
                        ? column.MetadataId == selectedColumn.MetadataId
                        : string.Equals(
                            column.LogicalName,
                            selectedColumn.LogicalName,
                            StringComparison.OrdinalIgnoreCase
                        )
                );
            _selectedIndex = refreshedIndex >= 0
                ? refreshedIndex
                : Math.Min(
                    _selectedIndex,
                    Math.Max(0, _columns.Count - 1)
                );
            KeepSelectionVisible();
            _status = BuildColumnStatus();
            SessionLog.Info(
                "UI.TableColumns",
                "Columns loaded table=" + _entity.LogicalName
                    + " count=" + _columns.Count
                    + " systemCount=" + _hiddenSystemColumnCount
            );
            Touch();
        }
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "UI.TableColumns",
                ex,
                "Loading columns failed table=" + _entity.LogicalName
            );
            if( generation == Volatile.Read(ref _loadGeneration) )
            {
                _status = ex.Message;
                Touch();
            }
        }
        finally
        {
            if( generation == Volatile.Read(ref _loadGeneration) )
            {
                _loading = false;
                Touch();
            }
        }
    }

    private void BeginCreate()
    {
        SessionLog.Info(
            "UI.TableColumns",
            "Create column action selected table=" + _entity.LogicalName
        );
        var capability = GetTableCapability();
        if( !capability.CanCreateColumn )
        {
            _status = capability.CreateColumnReason
                ?? ColumnCapabilityPolicy.ReasonUnknownTable;
            Touch();
            return;
        }

        _pendingAction = TableColumnsAction.NewColumn;
        Touch();
    }

    private void BeginEdit()
    {
        SessionLog.Info(
            "UI.TableColumns",
            "Edit column action selected index=" + _selectedIndex
        );
        if( IsWriteDisabled(out var writeReason) )
        {
            _status = writeReason;
            Touch();
            return;
        }

        if( _columns.Count == 0 )
        {
            return;
        }

        var capability = _capabilities[_selectedIndex];
        if( !capability.CanEdit )
        {
            _status = capability.EditReason
                ?? ColumnCapabilityPolicy.ReasonUnknown;
            Touch();
            return;
        }

        _pendingColumn = _columns[_selectedIndex];
        _editor = new ColumnEditorScreen(
            _service,
            _context,
            _entity.LogicalName,
            _entity.MetadataId,
            _pendingColumn
        );
        _detailsFocused = true;
        _status = "Editing " + _pendingColumn.LogicalName + ".";
        Touch();
    }

    private void BeginDelete()
    {
        SessionLog.Info(
            "UI.TableColumns",
            "Delete column action selected index=" + _selectedIndex
        );
        if( IsWriteDisabled(out var writeReason) )
        {
            _status = writeReason;
            Touch();
            return;
        }

        if( _columns.Count == 0 )
        {
            return;
        }

        var capability = _capabilities[_selectedIndex];
        if( !capability.CanDelete )
        {
            _status = capability.DeleteReason
                ?? ColumnCapabilityPolicy.ReasonUnknown;
            Touch();
            return;
        }

        _pendingColumn = _columns[_selectedIndex];
        _pendingAction = TableColumnsAction.DeleteColumn;
        Touch();
    }

    private void BeginPublish()
    {
        SessionLog.Info(
            "UI.TableColumns",
            "Publish action selected table=" + _entity.LogicalName
        );
        if( IsWriteDisabled(out var writeReason) )
        {
            _status = writeReason;
            Touch();
            return;
        }

        _pendingAction = TableColumnsAction.Publish;
        Touch();
    }

    private ColumnCapability GetTableCapability()
    {
        if( IsWriteDisabled(out var writeReason) )
        {
            return new ColumnCapability
            {
                CreateColumnReason = writeReason
            };
        }

        return ColumnCapabilityPolicy.EvaluateTable(
            _entity.IsCustomizable,
            _entity.CanCreateAttributes
        );
    }

    private bool IsWriteDisabled(out string reason)
    {
        if( _context.CanWrite && !_context.IsManaged )
        {
            reason = string.Empty;
            return false;
        }

        reason = _context.WriteDisabledReason
            ?? ( _context.IsManaged
                ? ColumnCapabilityPolicy.ReasonManagedSolution
                : "Writes are disabled because the solution context is unavailable." );
        return true;
    }

    private void MoveSelection(int delta)
    {
        SelectIndex(_selectedIndex + delta);
    }

    private void SelectIndex(int index)
    {
        if( _columns.Count == 0 )
        {
            return;
        }

        var next = Math.Clamp(index, 0, _columns.Count - 1);
        if( next == _selectedIndex )
        {
            return;
        }

        _selectedIndex = next;
        KeepSelectionVisible();
        Touch();
    }

    private void KeepSelectionVisible()
    {
        if( _columns.Count == 0 )
        {
            _firstVisible = 0;
            return;
        }

        if( _selectedIndex < _firstVisible )
        {
            _firstVisible = _selectedIndex;
        }
        else if( _selectedIndex >= _firstVisible + _listHeight )
        {
            _firstVisible = _selectedIndex - _listHeight + 1;
        }

        _firstVisible = Math.Clamp(
            _firstVisible,
            0,
            Math.Max(0, _columns.Count - _listHeight)
        );
    }

    private IRenderable RenderStatus()
    {
        var style = _loading ? Style.Parse("yellow") : Style.Parse("green");
        var pending = _sessionPendingChanges
            ? " Pending changes in this session; publish explicitly."
            : string.Empty;
        return Progress.RenderStatus(
            AnsiConsole.Profile.Width,
            "  " + _status + pending,
            style
        );
    }

    private string BuildColumnStatus()
    {
        if( _columns.Count == 0 )
        {
            return "No columns found.";
        }

        return _hiddenSystemColumnCount == 0
            ? $"{_columns.Count} columns."
            : $"{_columns.Count} columns. "
                + $"{_hiddenSystemColumnCount} system columns last.";
    }

    private IRenderable RenderColumns(int width)
    {
        if( _columns.Count == 0 )
        {
            return new Text(
                "  No columns. Press N to create one.",
                Style.Parse("dim")
            );
        }

        var rows = new List<IRenderable>();
        var writesAllowed = _context.CanWrite && !_context.IsManaged;
        var end = Math.Min(
            _columns.Count,
            _firstVisible + Math.Max(1, _listHeight)
        );
        for( var index = _firstVisible; index < end; index++ )
        {
            var column = _columns[index];
            var capability = _capabilities[index];
            var canEdit = writesAllowed && capability.CanEdit;
            var selected = index == _selectedIndex;
            var rowStyle = selected
                ? Style.Parse("black on cyan1")
                : Style.Parse("white");
            var name = string.IsNullOrWhiteSpace(column.SchemaName)
                ? column.LogicalName
                : column.SchemaName;
            var line = (selected ? "> " : "  ") + name;
            if( line.Length > width )
            {
                line = line[..Math.Max(1, width - 1)] + "…";
            }

            rows.Add(new Text(
                line,
                selected
                    ? rowStyle
                    : canEdit
                        ? TuiColors.EditableColumn
                        : Style.Parse("red")
            ));
        }

        return new Rows(rows);
    }

    private IRenderable RenderDetails()
    {
        if( _editor != null )
        {
            return _editor.RenderForm();
        }

        var table = new Table()
            .AddColumn(new TableColumn("Property").NoWrap())
            .AddColumn("Value");
        AddDetail(table, "Environment", _context.EnvironmentUrl);
        AddDetail(table, "Solution", _context.SolutionUniqueName);
        AddDetail(table, "Table", _entity.LogicalName);
        AddDetail(table, "Table metadata ID", _entity.MetadataId.ToString());
        if( IsWriteDisabled(out var writeReason) )
        {
            AddDetail(table, "Write actions", "Disabled (" + writeReason + ")");
        }
        else if( _sessionPendingChanges )
        {
            AddDetail(table, "Session pending changes", "Yes; publish explicitly");
        }
        if( _columns.Count == 0 )
        {
            AddDetail(table, "Column", "No column is selected");
            return table;
        }

        var column = _columns[_selectedIndex];
        var capability = _capabilities[_selectedIndex];
        var writesAllowed = _context.CanWrite && !_context.IsManaged;
        var editReason = writesAllowed
            ? capability.EditReason ?? "blocked"
            : _context.WriteDisabledReason
                ?? ColumnCapabilityPolicy.ReasonManagedSolution;
        var deleteReason = writesAllowed
            ? capability.DeleteReason ?? "blocked"
            : _context.WriteDisabledReason
                ?? ColumnCapabilityPolicy.ReasonManagedSolution;
        AddDetail(table, "Column", column.SchemaName);
        AddDetail(table, "Logical name", column.LogicalName);
        AddDetail(table, "Metadata ID", column.MetadataId.ToString());
        AddDetail(table, "Display name", column.DisplayName);
        AddDetail(table, "Type", GetKindText(column.Kind));
        if( !string.IsNullOrWhiteSpace(column.AttributeFormat) )
        {
            AddDetail(table, "Format", column.AttributeFormat);
        }
        AddDetail(table, "Requirement", GetRequirementText(column));
        if( column.MaxLength.HasValue )
        {
            AddDetail(table, "Maximum length", column.MaxLength.Value.ToString());
        }

        if( column.MinValue.HasValue || column.MaxValue.HasValue )
        {
            AddDetail(
                table,
                "Numeric bounds",
                DisplayDecimal(column.MinValue)
                + " .. "
                + DisplayDecimal(column.MaxValue)
            );
        }

        if( column.Precision.HasValue )
        {
            AddDetail(table, "Precision", column.Precision.Value.ToString());
        }

        if( column.Kind == ColumnKind.YesNo )
        {
            AddDetail(
                table,
                "Default",
                column.BooleanDefaultValue == true ? "Yes" : "No"
            );
            AddDetail(
                table,
                "Yes/No labels",
                Display(column.BooleanTrueLabel ?? string.Empty)
                + " / "
                + Display(column.BooleanFalseLabel ?? string.Empty)
            );
        }
        AddDetail(table, "Description", column.Description);
        AddDetail(
            table,
            "Edit",
            capability.CanEdit
                && writesAllowed
                ? "available"
                : editReason
        );
        AddDetail(
            table,
            "Delete",
            capability.CanDelete
                && writesAllowed
                ? "available"
                : deleteReason
        );
        return table;
    }

    private IRenderable RenderHint()
    {
        if( _editor != null )
        {
            return new Text(
                "  Tab: next  Shift+Tab: previous  Ctrl+S: save  Esc: list",
                Style.Parse("dim")
            );
        }

        var writeHint = _context.CanWrite && !_context.IsManaged
            ? "N: New  E: Edit  D: Delete  P: Publish"
            : "N/E/D/P: disabled";
        return new Text(
            "  Green: editable  Orange/red: read-only  " + writeHint
                + "  R: Refresh  "
            + "Tab: Details  Esc: list/back",
            Style.Parse("dim")
        );
    }

    private static void AddDetail(Table table, string property, string value)
    {
        table.AddRow(new Text(property), new Text(Display(value)));
    }

    private static string GetKindText(ColumnKind kind)
    {
        return kind switch
        {
            ColumnKind.Text => "Text",
            ColumnKind.MultilineText => "Memo",
            ColumnKind.WholeNumber => "Whole number",
            ColumnKind.Decimal => "Decimal",
            ColumnKind.YesNo => "Yes/No",
            _ => "Other"
        };
    }

    private static string GetRequirementText(DataverseColumn column)
    {
        return column.RequirementLevel switch
        {
            RequirementLevels.Required => "Business required",
            RequirementLevels.Recommended => "Business recommended",
            RequirementLevels.Optional => "Optional",
            _ => "Unknown"
        };
    }

    private static string Display(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static string DisplayDecimal(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "—";
    }

    private void Touch()
    {
        Interlocked.Increment(ref _revision);
    }
}
