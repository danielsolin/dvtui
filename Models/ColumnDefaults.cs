namespace dvtui.Models;

public static class ColumnDefaults
{
    public const int TextLength = 100;
    public const int MultilineLength = 2000;
    public const int TextMaxLength = 4000;
    public const int MultilineMaxLength = 1048576;
    public const int WholeNumberLowerBound = int.MinValue;
    public const int WholeNumberUpperBound = int.MaxValue;
    public const int WholeNumberMin = 0;
    public const int WholeNumberMax = int.MaxValue;
    public const decimal DecimalLowerBound = -100000000000m;
    public const decimal DecimalUpperBound = 100000000000m;
    public const decimal DecimalMin = 0;
    public const decimal DecimalMax = 100000000000m;
    public const int DecimalPrecision = 2;
    public const int DecimalMaxPrecision = 10;
    public const int PrimaryNameLength = 100;
    public const int DisplayLengthMax = 125;
    public const int DescriptionLengthMax = 4000;
    public const int SchemaNameLengthMax = 80;
}
