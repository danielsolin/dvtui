using System.Globalization;
using dvtui.Models;
using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class CreateTableScreen : IFormScreen
{
    private readonly DataverseService _service;
    private readonly SolutionWriteContext _context;

    private string _displayName = string.Empty;
    private string _pluralDisplayName = string.Empty;
    private string _schemaSuffix = string.Empty;
    private string _description = string.Empty;
    private string _primaryNameDisplay = "Name";
    private string _primaryNameSuffix = "name";
    private string _primaryNameLength = ColumnDefaults.PrimaryNameDefaultLength
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
    public string ProgressMessage => "Creating table...";
    public string Status => _status;
    public int Revision => Volatile.Read(ref _revision);

    public void ResetForRetry()
    {
        _pendingAction = FormAction.None;
        Touch();
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if( Progress.IsActive )
        {
            return;
        }

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
        SessionLog.Info(
            "UI.CreateTable",
            "Submit started displayName=" + _displayName
                + " schemaSuffix=" + _schemaSuffix
                + " primaryNameSuffix=" + _primaryNameSuffix
        );
        _submitting = true;
        _hasError = false;
        _status = "Creating table...";
        Touch();
        try
        {
            var validationError = Validate();
            if( validationError != null )
            {
                SessionLog.Warning(
                    "UI.CreateTable",
                    "Validation failed message=" + validationError
                );
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
            SessionLog.Info(
                "UI.CreateTable",
                "Submit succeeded tableId=" + tableId
            );
            _status = "Table created. ID: " + tableId;
            Touch();
        }
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
            SessionLog.Warning(
                "UI.CreateTable",
                "Submit cancelled after dispatch"
            );
            MarkOutcomeUnknown(
                "Operation cancelled after dispatch; verify before retrying."
            );
            Touch();
        }
        catch( SchemaWriteOutcomeUnknownException ex )
        {
            SessionLog.Exception(
                "UI.CreateTable",
                ex,
                "Submit outcome unknown"
            );
            MarkOutcomeUnknown(ex.Message);
            Touch();
        }
        catch( Exception ex )
        {
            SessionLog.Exception("UI.CreateTable", ex, "Submit failed");
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
            FormViewHelpers.BuildPredictedName(
                _context.PublisherPrefix,
                _schemaSuffix
            )
        );
        AddRow(
            table,
            "Predicted primary name",
            FormViewHelpers.BuildPredictedName(
                _context.PublisherPrefix,
                _primaryNameSuffix
            )
        );

        var fields = BuildFields();
        _activeField = Math.Clamp(_activeField, 0, fields.Count - 1);
        for( var index = 0; index < fields.Count; index++ )
        {
            var field = fields[index];
            var marker = index == _activeField ? "> " : "  ";
            var style = index == _activeField
                ? TuiColors.ActiveField
                : Style.Plain;
            table.AddRow(
                new Text(marker + field.Label, style),
                new Text(
                    FormViewHelpers.DisplayValue(field.Read()),
                    style
                )
            );
        }

        var panel = new Panel(table)
            .Header("Create table")
            .RoundedBorder()
            .Expand();
        panel.Height = height - 4;

        var hint = new Text(
            "  Tab: next  Shift+Tab: previous  Ctrl+S: create  Esc: back"
        );
        return new Layout()
            .SplitRows(
                new Layout().Update(panel),
                new Layout().Size(1).Update(
                    FormViewHelpers.RenderStatus(
                        width,
                        _status,
                        _hasError,
                        _submitting
                    )
                ),
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

    private List<FormField> BuildFields()
    {
        return
        [
            new FormField(
                "Display name",
                () => _displayName,
                value => _displayName = value
            ),
            new FormField(
                "Plural display name",
                () => _pluralDisplayName,
                value => _pluralDisplayName = value
            ),
            new FormField(
                "Schema suffix",
                () => _schemaSuffix,
                value => _schemaSuffix = value
            ),
            new FormField(
                "Description",
                () => _description,
                value => _description = value
            ),
            new FormField(
                "Primary name display",
                () => _primaryNameDisplay,
                value => _primaryNameDisplay = value
            ),
            new FormField(
                "Primary name suffix",
                () => _primaryNameSuffix,
                value => _primaryNameSuffix = value
            ),
            new FormField(
                "Primary name max length",
                () => _primaryNameLength,
                value => _primaryNameLength = value
            ),
            new FormField(
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

        if( _displayName.Length > ColumnDefaults.DisplayNameAllowedMaxLength )
        {
            return $"Display name must be {ColumnDefaults.DisplayNameAllowedMaxLength} "
                + "characters or fewer.";
        }

        if( string.IsNullOrWhiteSpace(_pluralDisplayName) )
        {
            _pluralDisplayName = _displayName + "s";
        }

        if( _pluralDisplayName.Length > ColumnDefaults.DisplayNameAllowedMaxLength )
        {
            return $"Plural display name must be "
                + $"{ColumnDefaults.DisplayNameAllowedMaxLength} "
                + "characters or fewer.";
        }

        if( string.IsNullOrWhiteSpace(_schemaSuffix) )
        {
            _schemaSuffix = FormViewHelpers.ToSchemaSuffix(
                _displayName,
                "table"
            );
        }

        if( _schemaSuffix.Length > ColumnDefaults.SchemaNameAllowedMaxLength )
        {
            return $"Schema suffix must be "
                + $"{ColumnDefaults.SchemaNameAllowedMaxLength} "
                + "characters or fewer.";
        }

        if( string.IsNullOrWhiteSpace(_primaryNameSuffix) )
        {
            _primaryNameSuffix = "name";
        }

        if( _primaryNameDisplay.Length > ColumnDefaults.DisplayNameAllowedMaxLength
            || string.IsNullOrWhiteSpace(_primaryNameDisplay) )
        {
            return $"Primary name display must be "
                + $"1-{ColumnDefaults.DisplayNameAllowedMaxLength} "
                + "characters.";
        }

        if( _description.Length > ColumnDefaults.DescriptionAllowedMaxLength )
        {
            return $"Description must be "
                + $"{ColumnDefaults.DescriptionAllowedMaxLength} "
                + "characters or fewer.";
        }

        if( !int.TryParse(
            _primaryNameLength,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var primaryLength
        )
            || primaryLength < 1
            || primaryLength > ColumnDefaults.TextAllowedMaxLength )
        {
            return "Primary name length must be between 1 and "
                + ColumnDefaults.TextAllowedMaxLength + ".";
        }

        return null;
    }

    private static void AddRow(
        Table table,
        string label,
        string value
    )
    {
        table.AddRow(
            new Text(label, TuiColors.SecondaryText),
            new Text(FormViewHelpers.DisplayValue(value))
        );
    }

    private void Touch()
    {
        Interlocked.Increment(ref _revision);
    }

}
