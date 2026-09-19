using System.Globalization;
using dvtui.Models;
using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class CreateTableScreen : IFormScreen
{
    private const int MaxDisplayLength = 125;
    private const int MaxSuffixLength = 80;
    private const int MaxDescriptionLength = 4000;

    private readonly DataverseService _service;
    private readonly SolutionWriteContext _context;

    private string _displayName = string.Empty;
    private string _pluralDisplayName = string.Empty;
    private string _schemaSuffix = string.Empty;
    private string _description = string.Empty;
    private string _primaryNameDisplay = "Name";
    private string _primaryNameSuffix = "name";
    private string _primaryNameLength = ColumnDefaults.PrimaryNameLength
        .ToString(CultureInfo.InvariantCulture);
    private bool _isUserOwned;
    private int _activeField;

    private bool _submitting;
    private string _status = "Fill in the table details.";
    private FormAction _pendingAction = FormAction.None;
    private bool _hasError;
    private bool _outcomeUnknown;
    private int _revision;

    public CreateTableScreen(
        DataverseService service,
        SolutionWriteContext context
    )
    {
        _service = service;
        _context = context;
    }

    public FormAction PendingAction => _pendingAction;
    public bool HasError => _hasError;
    public bool OutcomeUnknown => _outcomeUnknown;
    public string Status => _status;
    public int Revision => Volatile.Read(ref _revision);

    public void RequestCancellation()
    {
        if( _submitting )
        {
            _status = "Cancelling...";
            Touch();
        }
    }

    public void ResetForRetry()
    {
        _pendingAction = FormAction.None;
        Touch();
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if( _submitting )
        {
            return;
        }

        if( key.Key == ConsoleKey.Escape || key.KeyChar == '\u0003' )
        {
            _pendingAction = FormAction.Close;
            Touch();
            return;
        }

        if( _outcomeUnknown )
        {
            return;
        }

        if( key.KeyChar == '\u0013'
            || (key.Key == ConsoleKey.S
                && key.Modifiers.HasFlag(ConsoleModifiers.Control)) )
        {
            _pendingAction = FormAction.Submit;
            Touch();
            return;
        }

        var fields = BuildFields();
        _activeField = Math.Clamp(_activeField, 0, fields.Count - 1);
        if( key.Key == ConsoleKey.Tab )
        {
            var direction = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                ? -1
                : 1;
            _activeField = (_activeField + direction + fields.Count)
                % fields.Count;
            Touch();
            return;
        }

        var field = fields[_activeField];
        if( field.Choice )
        {
            if( key.Key == ConsoleKey.UpArrow
                || key.Key == ConsoleKey.LeftArrow
                || key.KeyChar == ' '
                || key.Key == ConsoleKey.DownArrow
                || key.Key == ConsoleKey.RightArrow )
            {
                _isUserOwned = !_isUserOwned;
                Touch();
            }

            return;
        }

        if( key.Key == ConsoleKey.Backspace
            || key.KeyChar == '\b'
            || key.KeyChar == '\u007f' )
        {
            var value = field.Read();
            if( value.Length > 0 )
            {
                field.Write(value[..^1]);
                Touch();
            }

            return;
        }

        if( char.IsControl(key.KeyChar) )
        {
            return;
        }

        field.Write(field.Read() + key.KeyChar);
        Touch();
    }

    public async Task SubmitAsync(CancellationToken cancellationToken)
    {
        _submitting = true;
        _hasError = false;
        _status = "Creating table...";
        Touch();
        try
        {
            var validationError = Validate();
            if( validationError != null )
            {
                _status = validationError;
                _hasError = true;
                Touch();
                return;
            }

            var request = new CreateTableRequest
            {
                Context = _context,
                DisplayName = _displayName,
                PluralDisplayName = _pluralDisplayName,
                SchemaSuffix = _schemaSuffix,
                Description = string.IsNullOrWhiteSpace(_description)
                    ? null
                    : _description,
                IsUserOwned = _isUserOwned,
                PrimaryNameDisplayName = _primaryNameDisplay,
                PrimaryNameSchemaSuffix = _primaryNameSuffix,
                PrimaryNameMaxLength = int.Parse(
                    _primaryNameLength,
                    CultureInfo.InvariantCulture
                )
            };

            var tableId = await _service.CreateTableAsync(
                request,
                cancellationToken
            );
            _status = "Table created. ID: " + tableId;
            Touch();
        }
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
            MarkOutcomeUnknown(
                "Operation cancelled after dispatch; verify before retrying."
            );
            Touch();
        }
        catch( SchemaWriteOutcomeUnknownException ex )
        {
            MarkOutcomeUnknown(ex.Message);
            Touch();
        }
        catch( Exception ex )
        {
            _status = ex.Message;
            _hasError = true;
            Touch();
        }
        finally
        {
            _submitting = false;
            Touch();
        }
    }

    public IRenderable Render()
    {
        var width = AnsiConsole.Profile.Width;
        var height = AnsiConsole.Profile.Height;
        if( width < 60 || height < 10 )
        {
            return new Text("Enlarge the terminal (60 x 10). Esc: back.");
        }

        var table = new Table()
            .AddColumn(new TableColumn("Field").NoWrap())
            .AddColumn("Value");
        AddRow(table, "Environment", _context.EnvironmentUrl);
        AddRow(table, "Solution", _context.SolutionUniqueName);
        AddRow(table, "Publisher prefix", _context.PublisherPrefix);
        AddRow(
            table,
            "Predicted logical name",
            BuildPredictedName(_schemaSuffix)
        );
        AddRow(
            table,
            "Predicted primary name",
            BuildPredictedName(_primaryNameSuffix)
        );

        var fields = BuildFields();
        _activeField = Math.Clamp(_activeField, 0, fields.Count - 1);
        for( var index = 0; index < fields.Count; index++ )
        {
            var field = fields[index];
            var marker = index == _activeField ? "> " : "  ";
            var style = index == _activeField
                ? Style.Parse("cyan")
                : Style.Plain;
            table.AddRow(
                new Text(marker + field.Label, style),
                new Text(DisplayValue(field.Read()))
            );
        }

        var panel = new Panel(table)
            .Header("Create table")
            .RoundedBorder()
            .Expand();
        panel.Height = height - 4;

        var hint = new Text(
            "  Tab: next  Shift+Tab: previous  Ctrl+S: create  Esc: back",
            Style.Parse("dim")
        );
        return new Layout()
            .SplitRows(
                new Layout().Update(panel),
                new Layout().Size(1).Update(RenderStatus()),
                new Layout().Size(1).Update(hint)
            );
    }

    public void MarkOutcomeUnknown(string message)
    {
        _outcomeUnknown = true;
        _hasError = true;
        _status = message;
        Touch();
    }

    private List<TableEditorField> BuildFields()
    {
        return
        [
            new TableEditorField(
                "Display name",
                () => _displayName,
                value => _displayName = value
            ),
            new TableEditorField(
                "Plural display name",
                () => _pluralDisplayName,
                value => _pluralDisplayName = value
            ),
            new TableEditorField(
                "Schema suffix",
                () => _schemaSuffix,
                value => _schemaSuffix = value
            ),
            new TableEditorField(
                "Description",
                () => _description,
                value => _description = value
            ),
            new TableEditorField(
                "Primary name display",
                () => _primaryNameDisplay,
                value => _primaryNameDisplay = value
            ),
            new TableEditorField(
                "Primary name suffix",
                () => _primaryNameSuffix,
                value => _primaryNameSuffix = value
            ),
            new TableEditorField(
                "Primary name max length",
                () => _primaryNameLength,
                value => _primaryNameLength = value
            ),
            new TableEditorField(
                "Ownership",
                () => _isUserOwned ? "User/team-owned" : "Organization-owned",
                _ => { },
                choice: true
            )
        ];
    }

    private string? Validate()
    {
        if( string.IsNullOrWhiteSpace(_displayName) )
        {
            return "Display name is required.";
        }

        if( _displayName.Length > MaxDisplayLength )
        {
            return "Display name must be 125 characters or fewer.";
        }

        if( string.IsNullOrWhiteSpace(_pluralDisplayName) )
        {
            _pluralDisplayName = _displayName + "s";
        }

        if( _pluralDisplayName.Length > MaxDisplayLength )
        {
            return "Plural display name must be 125 characters or fewer.";
        }

        if( string.IsNullOrWhiteSpace(_schemaSuffix) )
        {
            _schemaSuffix = ToSchemaSuffix(_displayName);
        }

        if( _schemaSuffix.Length > MaxSuffixLength )
        {
            return "Schema suffix must be 80 characters or fewer.";
        }

        if( string.IsNullOrWhiteSpace(_primaryNameSuffix) )
        {
            _primaryNameSuffix = "name";
        }

        if( _primaryNameDisplay.Length > MaxDisplayLength
            || string.IsNullOrWhiteSpace(_primaryNameDisplay) )
        {
            return "Primary name display must be 1-125 characters.";
        }

        if( _description.Length > MaxDescriptionLength )
        {
            return "Description must be 4000 characters or fewer.";
        }

        if( !int.TryParse(
            _primaryNameLength,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var primaryLength
        )
            || primaryLength < 1
            || primaryLength > ColumnDefaults.TextMaxLength )
        {
            return "Primary name length must be between 1 and "
                + ColumnDefaults.TextMaxLength + ".";
        }

        return null;
    }

    private static string ToSchemaSuffix(string displayName)
    {
        var chars = displayName
            .Where(char.IsLetterOrDigit)
            .ToArray();
        var suffix = new string(chars);
        if( suffix.Length == 0 )
        {
            return "table";
        }

        return char.ToLowerInvariant(suffix[0]) + suffix[1..];
    }

    private string BuildPredictedName(string suffix)
    {
        if( string.IsNullOrWhiteSpace(suffix) )
        {
            return "—";
        }

        return _context.PublisherPrefix + "_" + suffix;
    }

    private static string DisplayValue(string value)
    {
        return string.IsNullOrEmpty(value) ? "—" : value;
    }

    private IRenderable RenderStatus()
    {
        var style = _hasError
            ? Style.Parse("red")
            : _submitting
                ? Style.Parse("yellow")
                : Style.Parse("green");
        return new Text("  " + _status, style);
    }

    private static void AddRow(
        Table table,
        string label,
        string value
    )
    {
        table.AddRow(
            new Text(label, TuiColors.SecondaryText),
            new Text(DisplayValue(value))
        );
    }

    private void Touch()
    {
        Interlocked.Increment(ref _revision);
    }

    private sealed class TableEditorField
    {
        public TableEditorField(
            string label,
            Func<string> read,
            Action<string> write,
            bool choice = false
        )
        {
            Label = label;
            Read = read;
            Write = write;
            Choice = choice;
        }

        public string Label { get; }
        public Func<string> Read { get; }
        public Action<string> Write { get; }
        public bool Choice { get; }
    }
}
