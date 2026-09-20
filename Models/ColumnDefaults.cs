namespace dvtui.Models;

public static class ColumnDefaults
{
    public const int TextDefaultLength = 100;
    public const int MultilineDefaultLength = 2000;
    public const int TextAllowedMaxLength = 4000;
    public const int MultilineAllowedMaxLength = 1048576;
    public const int WholeNumberAllowedMin = int.MinValue;
    public const int WholeNumberAllowedMax = int.MaxValue;
    public const int WholeNumberDefaultMin = 0;
    public const int WholeNumberDefaultMax = int.MaxValue;
    public const decimal DecimalAllowedMin = -100000000000m;
    public const decimal DecimalAllowedMax = 100000000000m;
    public const decimal DecimalDefaultMin = 0;
    public const decimal DecimalDefaultMax = 100000000000m;
    public const int DecimalDefaultPrecision = 2;
    public const int DecimalAllowedMaxPrecision = 10;
    public const int PrimaryNameDefaultLength = 100;
    public const int DisplayNameAllowedMaxLength = 125;
    public const int DescriptionAllowedMaxLength = 4000;
    public const int SchemaNameAllowedMaxLength = 80;
}
