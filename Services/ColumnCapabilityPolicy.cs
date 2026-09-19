using dvtui.Models;

namespace dvtui.Services;

public static class ColumnCapabilityPolicy
{
    public const string ReasonManagedSolution = "Managed solution: read-only";
    public const string ReasonPrimaryId =
        "Primary column: editing is not supported in step 2";
    public const string ReasonPrimaryName =
        "Primary name column: editing is not supported in step 2";
    public const string ReasonManagedColumn = "Managed column: read-only";
    public const string ReasonSystemColumn = "System column: read-only";
    public const string ReasonSpecialized =
        "This column type is read-only in step 2";
    public const string ReasonUnsupportedKind =
        "This column type is read-only in step 2";
    public const string ReasonNotCustomizable =
        "Column is not customizable";
    public const string ReasonNotRenameable =
        "Column is not renameable";
    public const string ReasonNotCustomizableSettings =
        "Column settings are not customizable";
    public const string ReasonUnknown =
        "Column capability is unknown; no write is allowed";
    public const string ReasonTableNotCustomizable =
        "Table does not allow new columns";
    public const string ReasonTableCannotCreateAttributes =
        "Table does not allow new columns";
    public const string ReasonUnknownTable =
        "Table capability is unknown; no write is allowed";

    public static ColumnCapability Evaluate(DataverseColumn column)
    {
        if( column.IsPrimaryId == true )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonPrimaryId,
                DeleteReason = ReasonPrimaryId
            };
        }

        if( column.IsPrimaryName == true )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonPrimaryName,
                DeleteReason = ReasonPrimaryName
            };
        }

        if( !string.IsNullOrWhiteSpace(column.AttributeOf)
            || column.IsLogical == true
            || column.SourceType > 0
            || !string.IsNullOrWhiteSpace(column.AutoNumberFormat)
            || IsSpecializedFormat(column) )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonSpecialized,
                DeleteReason = ReasonSpecialized
            };
        }

        var supported = IsSupportedKind(column);
        if( column.IsCustom == null || column.IsManaged == null )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonUnknown,
                DeleteReason = ReasonUnknown
            };
        }

        var isCustom = column.IsCustom.Value;
        var isManaged = column.IsManaged.Value;
        var renameable = column.IsRenameable == true;
        var customizable = column.IsCustomizable == true;
        var canModifySettings = column.CanModifyAdditionalSettings == true;

        if( !supported )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonUnsupportedKind,
                DeleteReason = ReasonUnsupportedKind
            };
        }

        if( isManaged )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonManagedColumn,
                DeleteReason = ReasonManagedColumn
            };
        }

        if( !isCustom )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonSystemColumn,
                DeleteReason = ReasonSystemColumn
            };
        }

        if( column.IsCustomizable == null )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonUnknown,
                DeleteReason = ReasonUnknown
            };
        }

        if( !customizable )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CanEdit = false,
                CanDelete = false,
                EditReason = ReasonNotCustomizable,
                DeleteReason = ReasonNotCustomizable
            };
        }

        var canEditDisplayName = renameable;
        var canEditDescription = canModifySettings;
        var canEditRequirement = canModifySettings
            && column.CanChangeRequirement == true;
        var canIncreaseLength = canModifySettings
            && (column.Kind == ColumnKind.Text
                || column.Kind == ColumnKind.MultilineText);
        var canEdit = canEditDisplayName
            || canEditDescription
            || canEditRequirement
            || canIncreaseLength;

        return new ColumnCapability
        {
            CanCreateColumn = true,
            CanEdit = canEdit,
            CanDelete = true,
            CanEditDisplayName = canEditDisplayName,
            CanEditDescription = canEditDescription,
            CanEditRequirement = canEditRequirement,
            CanIncreaseLength = canIncreaseLength,
            EditDisplayNameReason = canEditDisplayName
                ? null
                : ReasonNotRenameable,
            EditDescriptionReason = canEditDescription
                ? null
                : ReasonNotCustomizableSettings,
            EditRequirementReason = canEditRequirement
                ? null
                : ReasonNotCustomizableSettings,
            IncreaseLengthReason = canIncreaseLength
                ? null
                : ReasonNotCustomizableSettings,
            EditReason = canEdit
                ? null
                : ReasonNotCustomizableSettings
        };
    }

    public static ColumnCapability EvaluateTable(
        bool? isCustomizable,
        bool? canCreateAttributes
    )
    {
        if( isCustomizable == false )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CreateColumnReason = ReasonTableNotCustomizable
            };
        }

        if( canCreateAttributes == false )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CreateColumnReason = ReasonTableCannotCreateAttributes
            };
        }

        if( isCustomizable != true || canCreateAttributes != true )
        {
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CreateColumnReason = ReasonUnknownTable
            };
        }

        return new ColumnCapability
        {
            CanCreateColumn = true
        };
    }

    private static bool IsSupportedKind(DataverseColumn column)
    {
        if( column.Kind == ColumnKind.Text
            || column.Kind == ColumnKind.MultilineText
            || column.Kind == ColumnKind.WholeNumber
            || column.Kind == ColumnKind.Decimal
            || column.Kind == ColumnKind.YesNo )
        {
            return true;
        }

        return false;
    }

    private static bool IsSpecializedFormat(DataverseColumn column)
    {
        if( string.IsNullOrWhiteSpace(column.AttributeFormat) )
        {
            return false;
        }

        return column.Kind switch
        {
            ColumnKind.Text => !string.Equals(
                column.AttributeFormat,
                "Text",
                StringComparison.OrdinalIgnoreCase
            ),
            ColumnKind.MultilineText => !string.Equals(
                    column.AttributeFormat,
                    "Text",
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.Equals(
                    column.AttributeFormat,
                    "TextArea",
                    StringComparison.OrdinalIgnoreCase
                ),
            ColumnKind.WholeNumber => !string.Equals(
                column.AttributeFormat,
                "None",
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false
        };
    }
}
