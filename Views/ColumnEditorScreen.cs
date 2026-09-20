using System.Globalization;
using dvtui.Models;
using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class ColumnEditorScreen : IFormScreen
{
    private readonly DataverseService _service;
    private readonly SolutionWriteContext _context;
    private readonly string _tableLogicalName;
    private readonly Guid _tableMetadataId;
    private readonly DataverseColumn? _existing;
    private readonly bool _isEdit;

    private string _displayName = string.Empty;
    private string _schemaSuffix = string.Empty;
    private string _description = string.Empty;
    private ColumnKind _kind = ColumnKind.Text;
    private string _maxLength = ColumnDefaults.TextDefaultLength.ToString(
        CultureInfo.InvariantCulture
    );
    private string _minValue = ColumnDefaults.WholeNumberDefaultMin.ToString(
        CultureInfo.InvariantCulture
    );
    private string _maxValue = ColumnDefaults.WholeNumberDefaultMax.ToString(
        CultureInfo.InvariantCulture
    );
    private string _precision = ColumnDefaults.DecimalDefaultPrecision.ToString(
        CultureInfo.InvariantCulture
    );
    private string _requirement = RequirementLevels.Optional;
    private bool _booleanDefault;
    private string _booleanTrueLabel = "Yes";
    private string _booleanFalseLabel = "No";
    private int _activeField;

    private bool _submitting;
    private string _status = string.Empty;
    private FormAction _pendingAction = FormAction.None;
    private bool _hasError;
    private bool _outcomeUnknown;
    private bool _mutationSucceeded;
    private int _revision;

    public ColumnEditorScreen(
        DataverseService service,
        SolutionWriteContext context,
        string tableLogicalName,
        Guid tableMetadataId,
        DataverseColumn? existing
    )
    {
        _service = service;
        _context = context;
        _tableLogicalName = tableLogicalName;
        _tableMetadataId = tableMetadataId;
        _existing = existing;
        _isEdit = existing != null;
        if( existing == null )
        {
            _status = "Fill in the column details.";
            return;
        }

        _displayName = existing.DisplayName;
        _schemaSuffix = existing.SchemaName;
        _description = existing.Description;
        _kind = existing.Kind;
        _maxLength = existing.MaxLength?.ToString(
            CultureInfo.InvariantCulture
        ) ?? string.Empty;
        _minValue = existing.MinValue?.ToString(
            CultureInfo.InvariantCulture
        ) ?? string.Empty;
        _maxValue = existing.MaxValue?.ToString(
            CultureInfo.InvariantCulture
        ) ?? string.Empty;
        _precision = existing.Precision?.ToString(
            CultureInfo.InvariantCulture
        ) ?? string.Empty;
        _requirement = existing.RequirementLevel
            ?? RequirementLevels.Optional;
        _booleanDefault = existing.BooleanDefaultValue ?? false;
        _booleanTrueLabel = existing.BooleanTrueLabel ?? "Yes";
        _booleanFalseLabel = existing.BooleanFalseLabel ?? "No";
        _status = "Edit column: " + existing.SchemaName;
    }

    public FormAction PendingAction => _pendingAction;
    public bool HasError => _hasError;
    public bool OutcomeUnknown => _outcomeUnknown;
    public string ProgressMessage => _isEdit
        ? "Saving column..."
        : "Creating column...";
    public string Status => _status;
    public bool MutationSucceeded => _mutationSucceeded;
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
        if( fields.Count == 0 )
        {
            return;
        }

        _activeField = Math.Clamp(_activeField, 0, fields.Count - 1);
        if( key.Key == ConsoleKey.Tab )
        {
            var direction = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                ? -1
                : 1;
            MoveField(fields.Count, direction);
            return;
        }

        var field = fields[_activeField];
        if( field.Choice )
        {
            if( key.Key == ConsoleKey.UpArrow
                || key.Key == ConsoleKey.LeftArrow
                || key.KeyChar == ' ' )
            {
                field.ChangeChoice?.Invoke(-1);
            }
            else if( key.Key == ConsoleKey.DownArrow
                || key.Key == ConsoleKey.RightArrow )
            {
                field.ChangeChoice?.Invoke(1);
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

        if( char.IsControl(key.KeyChar)
            || !ColumnEditorValidator.IsAllowedCharacter(
                field.InputKind,
                key.KeyChar
            ) )
        {
            return;
        }

        field.Write(field.Read() + key.KeyChar);
        Touch();
    }

    public async Task SubmitAsync(CancellationToken cancellationToken)
    {
        SessionLog.Info(
            "UI.ColumnEditor",
            "Submit started mode=" + (_isEdit ? "edit" : "create")
                + " table=" + _tableLogicalName
                + " column=" + (_existing?.LogicalName ?? "none")
                + " kind=" + _kind
        );
        _submitting = true;
        _hasError = false;
        _status = _isEdit ? "Saving column..." : "Creating column...";
        Touch();
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
        catch( OperationCanceledException )
            when( cancellationToken.IsCancellationRequested )
        {
            SessionLog.Warning(
                "UI.ColumnEditor",
                "Submit cancelled after dispatch mode="
                    + (_isEdit ? "edit" : "create")
            );
            MarkOutcomeUnknown(
                "Operation cancelled after dispatch; verify before retrying."
            );
            Touch();
        }
        catch( SchemaWriteOutcomeUnknownException ex )
        {
            SessionLog.Exception(
                "UI.ColumnEditor",
                ex,
                "Submit outcome unknown"
            );
            MarkOutcomeUnknown(ex.Message);
            Touch();
        }
        catch( Exception ex )
        {
            SessionLog.Exception("UI.ColumnEditor", ex, "Submit failed");
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

        var table = RenderForm();

        var panel = new Panel(table)
            .Header(_isEdit ? "Edit column" : "Create column")
            .RoundedBorder()
            .Expand();
        panel.Height = height - 4;

        var status = FormViewHelpers.RenderStatus(
            width,
            _status,
            _hasError,
            _submitting
        );
        var hint = new Text(
            "  Tab: next  Shift+Tab: previous  Ctrl+S: save  Esc: back"
        );

        return new Layout()
            .SplitRows(
                new Layout().Update(panel),
                new Layout().Size(1).Update(status),
                new Layout().Size(1).Update(hint)
            );
    }

    public IRenderable RenderForm()
    {
        var table = new Table()
            .AddColumn(new TableColumn("Field").NoWrap())
            .AddColumn("Value");
        AddRow(table, "Environment", _context.EnvironmentUrl);
        AddRow(table, "Solution", _context.SolutionUniqueName);
        AddRow(table, "Table", _tableLogicalName);
        if( !_isEdit )
        {
            AddRow(
                table,
                "Predicted logical name",
                FormViewHelpers.BuildPredictedName(
                    _context.PublisherPrefix,
                    _schemaSuffix
                )
            );
        }
        if( _isEdit && _existing != null )
        {
            AddRow(table, "Logical name", _existing.LogicalName);
            AddRow(table, "Schema name", _existing.SchemaName);
            AddRow(table, "Metadata ID", _existing.MetadataId.ToString());
            AddRow(table, "Type", GetKindText(_kind));
        }

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

        return table;
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
        var fields = new List<FormField>
        {
            new FormField(
                "Display name",
                () => _displayName,
                value => _displayName = value,
                inputKind: FormInputKind.Text
            )
        };
        if( !_isEdit )
        {
            fields.Add(new FormField(
                "Schema suffix",
                () => _schemaSuffix,
                value => _schemaSuffix = value,
                inputKind: FormInputKind.Text
            ));
        }

        fields.Add(new FormField(
            "Description",
            () => _description,
            value => _description = value,
            inputKind: FormInputKind.Text
        ));

        if( !_isEdit )
        {
            fields.Add(new FormField(
                "Type",
                () => GetKindText(_kind),
                _ => { },
                inputKind: FormInputKind.Choice,
                choice: true,
                changeChoice: ChangeKind
            ));
        }

        if( _kind == ColumnKind.Text || _kind == ColumnKind.MultilineText )
        {
            fields.Add(new FormField(
                "Maximum length",
                () => _maxLength,
                value => _maxLength = value,
                inputKind: FormInputKind.Integer
            ));
        }

        if( !_isEdit && _kind == ColumnKind.WholeNumber )
        {
            AddNumericFields(fields);
        }
        else if( !_isEdit && _kind == ColumnKind.Decimal )
        {
            AddNumericFields(fields);
            fields.Add(new FormField(
                "Precision",
                () => _precision,
                value => _precision = value,
                inputKind: FormInputKind.Integer
            ));
        }

        if( !_isEdit && _kind == ColumnKind.YesNo )
        {
            fields.Add(new FormField(
                "Default value",
                () => _booleanDefault ? "Yes" : "No",
                _ => { },
                inputKind: FormInputKind.Choice,
                choice: true,
                changeChoice: ChangeBooleanDefault
            ));
            fields.Add(new FormField(
                "Yes label",
                () => _booleanTrueLabel,
                value => _booleanTrueLabel = value,
                inputKind: FormInputKind.Text
            ));
            fields.Add(new FormField(
                "No label",
                () => _booleanFalseLabel,
                value => _booleanFalseLabel = value,
                inputKind: FormInputKind.Text
            ));
        }

        fields.Add(new FormField(
            "Requirement",
            () => RequirementLevels.GetDisplayName(_requirement),
            _ => { },
            inputKind: FormInputKind.Choice,
            choice: true,
            changeChoice: ChangeRequirement
        ));
        return fields;
    }

    private void AddNumericFields(List<FormField> fields)
    {
        fields.Add(new FormField(
            "Minimum value",
            () => _minValue,
            value => _minValue = value,
            inputKind: FormInputKind.Decimal
        ));
        fields.Add(new FormField(
            "Maximum value",
            () => _maxValue,
            value => _maxValue = value,
            inputKind: FormInputKind.Decimal
        ));
    }

    private void MoveField(int count, int direction)
    {
        _activeField = (_activeField + direction + count) % count;
        Touch();
    }

    private void ChangeKind(int direction)
    {
        var previousKind = _kind;
        _kind = CycleKind(_kind, direction);
        SetTypeDefaults(previousKind, _kind);
        Touch();
    }

    private void ChangeRequirement(int direction)
    {
        _requirement = CycleRequirement(_requirement, direction);
        Touch();
    }

    private void ChangeBooleanDefault(int direction)
    {
        _booleanDefault = !_booleanDefault;
        Touch();
    }

    private void SetTypeDefaults(ColumnKind previousKind, ColumnKind nextKind)
    {
        if( (previousKind == ColumnKind.Text
                || previousKind == ColumnKind.MultilineText)
            && (nextKind == ColumnKind.Text
                || nextKind == ColumnKind.MultilineText) )
        {
            var previousDefault = previousKind == ColumnKind.Text
                ? ColumnDefaults.TextDefaultLength
                : ColumnDefaults.MultilineDefaultLength;
            if( int.TryParse(
                _maxLength,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var currentLength
            ) && currentLength == previousDefault )
            {
                var nextDefault = nextKind == ColumnKind.Text
                    ? ColumnDefaults.TextDefaultLength
                    : ColumnDefaults.MultilineDefaultLength;
                _maxLength = nextDefault.ToString(
                    CultureInfo.InvariantCulture
                );
            }
        }

        if( nextKind == ColumnKind.WholeNumber )
        {
            _minValue = ColumnDefaults.WholeNumberDefaultMin.ToString(
                CultureInfo.InvariantCulture
            );
            _maxValue = ColumnDefaults.WholeNumberDefaultMax.ToString(
                CultureInfo.InvariantCulture
            );
        }
        else if( nextKind == ColumnKind.Decimal )
        {
            _minValue = ColumnDefaults.DecimalDefaultMin.ToString(
                CultureInfo.InvariantCulture
            );
            _maxValue = ColumnDefaults.DecimalDefaultMax.ToString(
                CultureInfo.InvariantCulture
            );
            _precision = ColumnDefaults.DecimalDefaultPrecision.ToString(
                CultureInfo.InvariantCulture
            );
        }
    }

    private static ColumnKind CycleKind(ColumnKind value, int direction)
    {
        var values = new[]
        {
            ColumnKind.Text,
            ColumnKind.MultilineText,
            ColumnKind.WholeNumber,
            ColumnKind.Decimal,
            ColumnKind.YesNo
        };
        var index = Array.IndexOf(values, value);
        index = (index + direction + values.Length) % values.Length;
        return values[index];
    }

    private static string CycleRequirement(string value, int direction)
    {
        var values = new[]
        {
            RequirementLevels.Optional,
            RequirementLevels.Recommended,
            RequirementLevels.Required
        };
        var index = Array.IndexOf(values, value);
        index = (index + direction + values.Length) % values.Length;
        return values[index];
    }

    private async Task SubmitCreateAsync(
        CancellationToken cancellationToken
    )
    {
        var schemaSuffix = string.IsNullOrWhiteSpace(_schemaSuffix)
            ? FormViewHelpers.ToSchemaSuffix(_displayName, "field")
            : _schemaSuffix;
        var values = new ColumnCreateValues(
            _displayName,
            schemaSuffix,
            _description,
            _kind,
            _maxLength,
            _minValue,
            _maxValue,
            _precision,
            _booleanTrueLabel,
            _booleanFalseLabel
        );
        var validationError = ColumnEditorValidator.ValidateCreate(
            values,
            out var maxLength,
            out var minimum,
            out var maximum,
            out var precision
        );
        if( validationError != null )
        {
            SessionLog.Warning(
                "UI.ColumnEditor",
                "Create validation failed message=" + validationError
            );
            _status = validationError;
            _hasError = true;
            return;
        }

        _schemaSuffix = schemaSuffix;
        var request = new CreateColumnRequest
        {
            Context = _context,
            TableLogicalName = _tableLogicalName,
            TableMetadataId = _tableMetadataId,
            DisplayName = _displayName,
            SchemaSuffix = schemaSuffix,
            Description = string.IsNullOrWhiteSpace(_description)
                ? null
                : _description,
            Kind = _kind,
            MaxLength = maxLength,
            MinValue = minimum,
            MaxValue = maximum,
            Precision = precision,
            BooleanDefaultValue = _booleanDefault,
            BooleanTrueLabel = _booleanTrueLabel,
            BooleanFalseLabel = _booleanFalseLabel,
            RequirementLevel = _requirement
        };

        await _service.CreateColumnAsync(request, cancellationToken);
        SessionLog.Info(
            "UI.ColumnEditor",
            "Create request completed table=" + _tableLogicalName
                + " schemaSuffix=" + _schemaSuffix
        );
        _mutationSucceeded = true;
        _status = "Column created. Publish the table separately if needed.";
    }

    private async Task SubmitEditAsync(
        CancellationToken cancellationToken
    )
    {
        if( _existing == null )
        {
            SessionLog.Warning(
                "UI.ColumnEditor",
                "Edit requested without an existing column"
            );
            return;
        }

        var capability = ColumnCapabilityPolicy.Evaluate(_existing);
        if( !capability.CanEdit )
        {
            SessionLog.Warning(
                "UI.ColumnEditor",
                "Edit blocked column=" + _existing.LogicalName
                    + " reason=" + capability.EditReason
            );
            _status = capability.EditReason
                ?? ColumnCapabilityPolicy.ReasonUnknown;
            _hasError = true;
            return;
        }

        int? requestedLength = null;
        if( _kind == ColumnKind.Text || _kind == ColumnKind.MultilineText )
        {
            if( string.IsNullOrWhiteSpace(_maxLength) )
            {
                requestedLength = _existing.MaxLength;
            }
            else if( !int.TryParse(
                _maxLength,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedLength
            ) )
            {
                _status = "Maximum length must be a whole number.";
                _hasError = true;
                return;
            }
            else
            {
                requestedLength = parsedLength;
            }
        }

        var displayNameChanged = _displayName != _existing.DisplayName;
        var descriptionChanged = _description != _existing.Description;
        var requirementChanged = _requirement != _existing.RequirementLevel;
        var maxLengthChanged = requestedLength != _existing.MaxLength;
        if( !displayNameChanged
            && !descriptionChanged
            && !requirementChanged
            && !maxLengthChanged )
        {
            SessionLog.Info(
                "UI.ColumnEditor",
                "Edit submitted with no changes column=" + _existing.LogicalName
            );
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
            TableMetadataId = _tableMetadataId,
            ColumnLogicalName = _existing.LogicalName,
            ExpectedMetadataId = _existing.MetadataId,
            SetDisplayName = displayNameChanged,
            DisplayName = _displayName,
            SetDescription = descriptionChanged,
            Description = _description,
            NewMaxLength = maxLengthChanged ? requestedLength : null,
            SetRequirementLevel = requirementChanged,
            RequirementLevel = _requirement
        };

        var result = await _service.UpdateColumnAsync(request, cancellationToken);
        SessionLog.Info(
            "UI.ColumnEditor",
            "Update request completed column=" + _existing.LogicalName
                + " changed=" + result.Changed
        );
        _mutationSucceeded = result.Changed;
        _status = result.Changed
            ? "Column saved. Publish the table separately."
            : "No changes to save.";
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
