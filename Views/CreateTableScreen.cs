using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Models;
using dvtui.Services;

namespace dvtui.Views;

internal sealed class CreateTableScreen : IFormScreen
{
    private const int MaxDisplayLength = 125;
    private const int MaxSuffixLength = 80;
    private const int MaxDescriptionLength = 4000;

    private readonly DataverseService _service;
    private readonly SolutionWriteContext _context;

    private string _displayName = string.Empty;
    private string _schemaSuffix = string.Empty;
    private string _description = string.Empty;
    private string _primaryNameDisplay = "Name";
    private string _primaryNameSuffix = "name";
    private int _primaryNameLength = ColumnDefaults.PrimaryNameLength;

    private bool _submitting;
    private string _status = "Fill in the table details.";
    private FormAction _pendingAction = FormAction.None;
    private bool _hasError;

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

    public void HandleKey(ConsoleKeyInfo key)
    {
        if( _submitting )
        {
            return;
        }

        if( key.Key == ConsoleKey.Escape )
        {
            _pendingAction = FormAction.Close;
            return;
        }

        if( key.Key == ConsoleKey.Enter )
        {
            _pendingAction = FormAction.Submit;
            return;
        }

        if( char.IsControl(key.KeyChar) )
        {
            return;
        }

        if( key.KeyChar == '\b' )
        {
            if( _displayName.Length > 0 )
            {
                _displayName = _displayName[..^1];
            }
            return;
        }

        if( _displayName.Length < MaxDisplayLength )
        {
            _displayName += key.KeyChar;
        }
    }

    public Task SubmitAsync(CancellationToken cancellationToken)
    {
        _submitting = true;
        _hasError = false;
        _status = "Creating table...";
        return Task.Run(async () =>
        {
            try
            {
                var validationError = Validate();
                if( validationError != null )
                {
                    _status = validationError;
                    _hasError = true;
                    return;
                }

                var request = new CreateTableRequest
                {
                    Context = _context,
                    DisplayName = _displayName,
                    PluralDisplayName = _displayName + "s",
                    SchemaSuffix = _schemaSuffix,
                    Description = string.IsNullOrWhiteSpace(_description)
                        ? null
                        : _description,
                    IsUserOwned = false,
                    PrimaryNameDisplayName = _primaryNameDisplay,
                    PrimaryNameSchemaSuffix = _primaryNameSuffix,
                    PrimaryNameMaxLength = _primaryNameLength
                };

                await _service.CreateTableAsync(
                    request,
                    cancellationToken
                );
                _status = "Table created.";
            }
            catch( Exception ex )
            {
                _status = ex.Message;
                _hasError = true;
            }
            finally
            {
                _submitting = false;
            }
        }, cancellationToken);
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
        table.AddRow(
            new Text("Display name", Style.Parse("cyan")),
            new Text(_displayName)
        );
        table.AddRow(
            new Text("Schema suffix", Style.Parse("cyan")),
            new Text(_schemaSuffix)
        );
        table.AddRow(
            new Text("Description", Style.Parse("cyan")),
            new Text(_description)
        );
        table.AddRow(
            new Text("Primary name", Style.Parse("cyan")),
            new Text(
                $"{_primaryNameDisplay} ({_primaryNameSuffix}, "
                + $"max {_primaryNameLength})"
            )
        );
        table.AddRow(
            new Text("Publisher prefix", Style.Parse("cyan")),
            new Text(_context.PublisherPrefix)
        );

        var panel = new Panel(table)
            .Header("Create table")
            .RoundedBorder()
            .Expand();
        panel.Height = height - 4;

        var status = RenderStatus();
        var hint = new Text(
            "  Type display name, then Enter to create. Esc: back.",
            Style.Parse("dim")
        );

        return new Layout()
            .SplitRows(
                new Layout().Update(panel),
                new Layout().Size(1).Update(status),
                new Layout().Size(1).Update(hint)
            );
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

        if( string.IsNullOrWhiteSpace(_schemaSuffix) )
        {
            _schemaSuffix = ToSchemaSuffix(_displayName);
        }

        if( _schemaSuffix.Length > MaxSuffixLength )
        {
            return "Schema suffix must be 80 characters or fewer.";
        }

        if( _description.Length > MaxDescriptionLength )
        {
            return "Description must be 4000 characters or fewer.";
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
}
