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

        var supported = IsSupportedKind(column);
        var isCustom = column.IsCustom == true;
        var isManaged = column.IsManaged == true;
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
        var canEditRequirement = canModifySettings;
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
        if( isCustomizable != true || canCreateAttributes != true )
        {
            var reason = isCustomizable != true
                ? ReasonTableNotCustomizable
                : ReasonUnknownTable;
            return new ColumnCapability
            {
                CanCreateColumn = false,
                CreateColumnReason = reason
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
}
