using System.Globalization;
using dvtui.Models;
using dvtui.Services;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace dvtui.Views;

internal sealed class ColumnEditorScreen : IFormScreen
{
    private const int MaxDisplayLength = 125;
    private const int MaxSuffixLength = 80;
    private const int MaxDescriptionLength = 4000;

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
    private string _maxLength = ColumnDefaults.TextLength.ToString(
        CultureInfo.InvariantCulture
    );
    private string _minValue = ColumnDefaults.WholeNumberMin.ToString(
        CultureInfo.InvariantCulture
    );
    private string _maxValue = ColumnDefaults.WholeNumberMax.ToString(
        CultureInfo.InvariantCulture
    );
    private string _precision = ColumnDefaults.DecimalPrecision.ToString(
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
    public string Status => _status;
    public bool MutationSucceeded => _mutationSucceeded;
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
        if( field.Choice != EditorChoice.None )
        {
            if( key.Key == ConsoleKey.UpArrow
                || key.Key == ConsoleKey.LeftArrow
                || key.KeyChar == ' ' )
            {
                ChangeChoice(field.Choice, -1);
            }
            else if( key.Key == ConsoleKey.DownArrow
                || key.Key == ConsoleKey.RightArrow )
            {
                ChangeChoice(field.Choice, 1);
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
            || !IsAllowedCharacter(field.InputKind, key.KeyChar) )
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

        var table = RenderForm();

        var panel = new Panel(table)
            .Header(_isEdit ? "Edit column" : "Create column")
            .RoundedBorder()
            .Expand();
        panel.Height = height - 4;

        var status = RenderStatus();
        var hint = new Text(
            "  Tab: next  Shift+Tab: previous  Ctrl+S: save  Esc: back",
            Style.Parse("dim")
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
                BuildPredictedName(_schemaSuffix)
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
                ? Style.Parse("cyan")
                : Style.Plain;
            table.AddRow(
                new Text(marker + field.Label, style),
                new Text(DisplayValue(field.Read()))
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

    private List<EditorField> BuildFields()
    {
        var fields = new List<EditorField>
        {
            new(
                "Display name",
                EditorInputKind.Text,
                EditorChoice.None,
                () => _displayName,
                value => _displayName = value
            )
        };
        if( !_isEdit )
        {
            fields.Add(new EditorField(
                "Schema suffix",
                EditorInputKind.Text,
                EditorChoice.None,
                () => _schemaSuffix,
                value => _schemaSuffix = value
            ));
        }

        fields.Add(new EditorField(
            "Description",
            EditorInputKind.Text,
            EditorChoice.None,
            () => _description,
            value => _description = value
        ));

        if( !_isEdit )
        {
            fields.Add(new EditorField(
                "Type",
                EditorInputKind.Choice,
                EditorChoice.ColumnKind,
                () => GetKindText(_kind),
                _ => { }
            ));
        }

        if( _kind == ColumnKind.Text || _kind == ColumnKind.MultilineText )
        {
            fields.Add(new EditorField(
                "Maximum length",
                EditorInputKind.Integer,
                EditorChoice.None,
                () => _maxLength,
                value => _maxLength = value
            ));
        }

        if( !_isEdit && _kind == ColumnKind.WholeNumber )
        {
            AddNumericFields(fields);
        }
        else if( !_isEdit && _kind == ColumnKind.Decimal )
        {
            AddNumericFields(fields);
            fields.Add(new EditorField(
                "Precision",
                EditorInputKind.Integer,
                EditorChoice.None,
                () => _precision,
                value => _precision = value
            ));
        }

        if( !_isEdit && _kind == ColumnKind.YesNo )
        {
            fields.Add(new EditorField(
                "Default value",
                EditorInputKind.Choice,
                EditorChoice.BooleanDefault,
                () => _booleanDefault ? "Yes" : "No",
                _ => { }
            ));
            fields.Add(new EditorField(
                "Yes label",
                EditorInputKind.Text,
                EditorChoice.None,
                () => _booleanTrueLabel,
                value => _booleanTrueLabel = value
            ));
            fields.Add(new EditorField(
                "No label",
                EditorInputKind.Text,
                EditorChoice.None,
                () => _booleanFalseLabel,
                value => _booleanFalseLabel = value
            ));
        }

        fields.Add(new EditorField(
            "Requirement",
            EditorInputKind.Choice,
            EditorChoice.Requirement,
            () => RequirementLevels.GetDisplayName(_requirement),
            _ => { }
        ));
        return fields;
    }

    private void AddNumericFields(List<EditorField> fields)
    {
        fields.Add(new EditorField(
            "Minimum value",
            EditorInputKind.Decimal,
            EditorChoice.None,
            () => _minValue,
            value => _minValue = value
        ));
        fields.Add(new EditorField(
            "Maximum value",
            EditorInputKind.Decimal,
            EditorChoice.None,
            () => _maxValue,
            value => _maxValue = value
        ));
    }

    private void MoveField(int count, int direction)
    {
        _activeField = (_activeField + direction + count) % count;
        Touch();
    }

    private void ChangeChoice(EditorChoice choice, int direction)
    {
        switch( choice )
        {
            case EditorChoice.ColumnKind:
                var previousKind = _kind;
                _kind = CycleKind(_kind, direction);
                SetTypeDefaults(previousKind, _kind);
                break;
            case EditorChoice.Requirement:
                _requirement = CycleRequirement(_requirement, direction);
                break;
            case EditorChoice.BooleanDefault:
                _booleanDefault = !_booleanDefault;
                break;
        }

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
                ? ColumnDefaults.TextLength
                : ColumnDefaults.MultilineLength;
            if( int.TryParse(
                _maxLength,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var currentLength
            ) && currentLength == previousDefault )
            {
                var nextDefault = nextKind == ColumnKind.Text
                    ? ColumnDefaults.TextLength
                    : ColumnDefaults.MultilineLength;
                _maxLength = nextDefault.ToString(
                    CultureInfo.InvariantCulture
                );
            }
        }

        if( nextKind == ColumnKind.WholeNumber )
        {
            _minValue = ColumnDefaults.WholeNumberMin.ToString(
                CultureInfo.InvariantCulture
            );
            _maxValue = ColumnDefaults.WholeNumberMax.ToString(
                CultureInfo.InvariantCulture
            );
        }
        else if( nextKind == ColumnKind.Decimal )
        {
            _minValue = ColumnDefaults.DecimalMin.ToString(
                CultureInfo.InvariantCulture
            );
            _maxValue = ColumnDefaults.DecimalMax.ToString(
                CultureInfo.InvariantCulture
            );
            _precision = ColumnDefaults.DecimalPrecision.ToString(
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
        var validationError = ValidateCreate(
            out var maxLength,
            out var minimum,
            out var maximum,
            out var precision
        );
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
            TableMetadataId = _tableMetadataId,
            DisplayName = _displayName,
            SchemaSuffix = _schemaSuffix,
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
        _mutationSucceeded = true;
        _status = "Column created. Publish the table separately if needed.";
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
        _mutationSucceeded = result.Changed;
        _status = result.Changed
            ? "Column saved. Publish the table separately."
            : "No changes to save.";
    }

    private string? ValidateCreate(
        out int? maxLength,
        out decimal? minimum,
        out decimal? maximum,
        out int? precision
    )
    {
        maxLength = null;
        minimum = null;
        maximum = null;
        precision = null;
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

        if( _kind == ColumnKind.Text || _kind == ColumnKind.MultilineText )
        {
            var maximumLength = _kind == ColumnKind.Text
                ? ColumnDefaults.TextMaxLength
                : ColumnDefaults.MultilineMaxLength;
            if( !int.TryParse(
                _maxLength,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedLength
            )
                || parsedLength < 1
                || parsedLength > maximumLength )
            {
                return "Maximum length is outside the supported range.";
            }

            maxLength = parsedLength;
        }

        if( _kind == ColumnKind.WholeNumber
            || _kind == ColumnKind.Decimal )
        {
            var error = ParseDecimal(_minValue, "Minimum value", out minimum);
            if( error != null )
            {
                return error;
            }

            error = ParseDecimal(_maxValue, "Maximum value", out maximum);
            if( error != null )
            {
                return error;
            }

            if( minimum > maximum )
            {
                return "Minimum value must not exceed maximum value.";
            }

            var lowerBound = _kind == ColumnKind.WholeNumber
                ? ColumnDefaults.WholeNumberLowerBound
                : ColumnDefaults.DecimalLowerBound;
            var upperBound = _kind == ColumnKind.WholeNumber
                ? ColumnDefaults.WholeNumberUpperBound
                : ColumnDefaults.DecimalUpperBound;
            if( (minimum.HasValue && minimum.Value < lowerBound)
                || (maximum.HasValue && maximum.Value > upperBound) )
            {
                return "Numeric bounds are outside the supported range.";
            }

            if( _kind == ColumnKind.WholeNumber
                && ((minimum.HasValue
                    && minimum.Value != decimal.Truncate(minimum.Value))
                    || (maximum.HasValue
                        && maximum.Value != decimal.Truncate(maximum.Value))) )
            {
                return "Whole number bounds must be whole numbers.";
            }
        }

        if( _kind == ColumnKind.Decimal )
        {
            if( !int.TryParse(
                _precision,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedPrecision
            )
                || parsedPrecision < 0
                || parsedPrecision > ColumnDefaults.DecimalMaxPrecision )
            {
                return "Precision must be between 0 and 10.";
            }

            precision = parsedPrecision;
        }

        if( _kind == ColumnKind.YesNo )
        {
            if( string.IsNullOrWhiteSpace(_booleanTrueLabel)
                || string.IsNullOrWhiteSpace(_booleanFalseLabel) )
            {
                return "Yes and No labels are required.";
            }
        }

        return null;
    }

    private static string? ParseDecimal(
        string value,
        string field,
        out decimal? result
    )
    {
        if( string.IsNullOrWhiteSpace(value) )
        {
            result = null;
            return null;
        }

        if( decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed
        ) )
        {
            result = parsed;
            return null;
        }

        result = null;
        return field + " must use a valid number with '.' as decimal separator.";
    }

    private static bool IsAllowedCharacter(
        EditorInputKind kind,
        char value
    )
    {
        if( kind == EditorInputKind.Decimal )
        {
            return char.IsDigit(value) || value == '-' || value == '.';
        }

        if( kind == EditorInputKind.Integer )
        {
            return char.IsDigit(value) || value == '-';
        }

        return true;
    }

    private static string DisplayValue(string value)
    {
        return string.IsNullOrEmpty(value) ? "—" : value;
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

    private string BuildPredictedName(string suffix)
    {
        if( string.IsNullOrWhiteSpace(suffix) )
        {
            return "—";
        }

        return _context.PublisherPrefix + "_" + suffix;
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

    private enum EditorInputKind
    {
        Text,
        Integer,
        Decimal,
        Choice
    }

    private enum EditorChoice
    {
        None,
        ColumnKind,
        Requirement,
        BooleanDefault
    }

    private sealed class EditorField
    {
        public EditorField(
            string label,
            EditorInputKind inputKind,
            EditorChoice choice,
            Func<string> read,
            Action<string> write
        )
        {
            Label = label;
            InputKind = inputKind;
            Choice = choice;
            Read = read;
            Write = write;
        }

        public string Label { get; }
        public EditorInputKind InputKind { get; }
        public EditorChoice Choice { get; }
        public Func<string> Read { get; }
        public Action<string> Write { get; }
    }
}
