using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Models;
using dvtui.Services;

namespace dvtui.Views;

internal sealed class ColumnEditorScreen : IFormScreen
{
    private const int MaxDisplayLength = 125;
    private const int MaxSuffixLength = 80;
    private const int MaxDescriptionLength = 4000;

    private readonly DataverseService _service;
    private readonly SolutionWriteContext _context;
    private readonly string _tableLogicalName;
    private readonly DataverseColumn? _existing;
    private readonly bool _isEdit;

    private string _displayName = string.Empty;
    private string _schemaSuffix = string.Empty;
    private string _description = string.Empty;
    private ColumnKind _kind = ColumnKind.Text;
    private int _maxLength = ColumnDefaults.TextLength;
    private int _precision = ColumnDefaults.DecimalPrecision;
    private string _requirement = RequirementLevels.Optional;

    private bool _submitting;
    private string _status = string.Empty;
    private FormAction _pendingAction = FormAction.None;
    private bool _hasError;

    public ColumnEditorScreen(
        DataverseService service,
        SolutionWriteContext context,
        string tableLogicalName,
        DataverseColumn? existing
    )
    {
        _service = service;
        _context = context;
        _tableLogicalName = tableLogicalName;
        _existing = existing;
        _isEdit = existing != null;
        if( existing != null )
        {
            _displayName = existing.DisplayName;
            _description = existing.Description;
            _kind = existing.Kind;
            _maxLength = existing.MaxLength ?? ColumnDefaults.TextLength;
            _precision = existing.Precision ?? ColumnDefaults.DecimalPrecision;
            _requirement = existing.RequirementLevel
                ?? RequirementLevels.Optional;
            _status = "Edit column: " + existing.SchemaName;
        }
        else
        {
            _status = "Fill in the column details.";
        }
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
        _status = _isEdit ? "Saving column..." : "Creating column...";
        return Task.Run(async () =>
        {
            try
            {
                if( _isEdit && _existing != null )
                {
                    await SubmitEditAsync(cancellationToken);
                }
                else
                {
                    await SubmitCreateAsync(cancellationToken);
                }
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

    private async Task SubmitCreateAsync(
        CancellationToken cancellationToken
    )
    {
        var validationError = ValidateCreate();
        if( validationError != null )
        {
            _status = validationError;
            _hasError = true;
            return;
        }

        var request = new CreateColumnRequest
        {
            Context = _context,
            TableLogicalName = _tableLogicalName,
            DisplayName = _displayName,
            SchemaSuffix = _schemaSuffix,
            Description = string.IsNullOrWhiteSpace(_description)
                ? null
                : _description,
            Kind = _kind,
            MaxLength = GetMaxLengthForKind(),
            MinValue = null,
            MaxValue = null,
            Precision = _kind == ColumnKind.Decimal ? _precision : null,
            RequirementLevel = _requirement
        };

        await _service.CreateColumnAsync(request, cancellationToken);
        _status = "Column created.";
    }

    private async Task SubmitEditAsync(
        CancellationToken cancellationToken
    )
    {
        if( _existing == null )
        {
            return;
        }

        var capability = ColumnCapabilityPolicy.Evaluate(_existing);
        if( !capability.CanEdit )
        {
            _status = capability.EditReason
                ?? ColumnCapabilityPolicy.ReasonUnknown;
            _hasError = true;
            return;
        }

        var displayNameChanged =
            _displayName != _existing.DisplayName;
        var descriptionChanged =
            _description != _existing.Description;
        var requirementChanged =
            _requirement != _existing.RequirementLevel;
        var maxLengthChanged =
            _maxLength != (_existing.MaxLength ?? _maxLength);

        if( !displayNameChanged
            && !descriptionChanged
            && !requirementChanged
            && !maxLengthChanged )
        {
            _status = "No changes to save.";
            return;
        }

        if( displayNameChanged && !capability.CanEditDisplayName )
        {
            _status = capability.EditDisplayNameReason
                ?? ColumnCapabilityPolicy.ReasonNotRenameable;
            _hasError = true;
            return;
        }

        if( descriptionChanged && !capability.CanEditDescription )
        {
            _status = capability.EditDescriptionReason
                ?? ColumnCapabilityPolicy.ReasonNotCustomizableSettings;
            _hasError = true;
            return;
        }

        if( requirementChanged && !capability.CanEditRequirement )
        {
            _status = capability.EditRequirementReason
                ?? ColumnCapabilityPolicy.ReasonNotCustomizableSettings;
            _hasError = true;
            return;
        }

        if( maxLengthChanged && !capability.CanIncreaseLength )
        {
            _status = capability.IncreaseLengthReason
                ?? ColumnCapabilityPolicy.ReasonNotCustomizableSettings;
            _hasError = true;
            return;
        }

        var request = new UpdateColumnRequest
        {
            Context = _context,
            TableLogicalName = _tableLogicalName,
            ColumnLogicalName = _existing.LogicalName,
            ExpectedMetadataId = _existing.MetadataId,
            SetDisplayName = displayNameChanged,
            DisplayName = _displayName,
            SetDescription = descriptionChanged,
            Description = _description,
            NewMaxLength = maxLengthChanged ? _maxLength : null,
            SetRequirementLevel = requirementChanged,
            RequirementLevel = _requirement
        };

        await _service.UpdateColumnAsync(request, cancellationToken);
        _status = "Column saved.";
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
            new Text("Type", Style.Parse("cyan")),
            new Text(GetKindText(_kind))
        );
        if( _kind == ColumnKind.Text
            || _kind == ColumnKind.MultilineText )
        {
            table.AddRow(
                new Text("Max length", Style.Parse("cyan")),
                new Text(_maxLength.ToString())
            );
        }
        if( _kind == ColumnKind.Decimal )
        {
            table.AddRow(
                new Text("Precision", Style.Parse("cyan")),
                new Text(_precision.ToString())
            );
        }
        table.AddRow(
            new Text("Requirement", Style.Parse("cyan")),
            new Text(RequirementLevels.GetDisplayName(_requirement))
        );
        table.AddRow(
            new Text("Description", Style.Parse("cyan")),
            new Text(_description)
        );

        var panel = new Panel(table)
            .Header(_isEdit ? "Edit column" : "Create column")
            .RoundedBorder()
            .Expand();
        panel.Height = height - 4;

        var status = RenderStatus();
        var hint = new Text(
            _isEdit
                ? "  Enter: save  Esc: cancel"
                : "  Type display name, then Enter to create. Esc: back.",
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

    private int? GetMaxLengthForKind()
    {
        return _kind switch
        {
            ColumnKind.Text => _maxLength,
            ColumnKind.MultilineText => _maxLength,
            _ => null
        };
    }

    private string? ValidateCreate()
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

        if( _kind == ColumnKind.Text
            && ( _maxLength < 1 || _maxLength > 8000 ) )
        {
            return "Text length must be 1-8000.";
        }

        if( _kind == ColumnKind.MultilineText
            && ( _maxLength < 1 || _maxLength > 32000 ) )
        {
            return "Memo length must be 1-32000.";
        }

        if( _kind == ColumnKind.Decimal
            && ( _precision < 0 || _precision > 23 ) )
        {
            return "Precision must be 0-23.";
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
            return "field";
        }

        return char.ToLowerInvariant(suffix[0]) + suffix[1..];
    }

    private static string GetKindText(ColumnKind kind)
    {
        return kind switch
        {
            ColumnKind.Text => "Single line text",
            ColumnKind.MultilineText => "Multiple lines of text",
            ColumnKind.WholeNumber => "Whole number",
            ColumnKind.Decimal => "Decimal number",
            ColumnKind.YesNo => "Yes/No",
            _ => "Unknown"
        };
    }
}
