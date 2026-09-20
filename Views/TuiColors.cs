using Spectre.Console;

namespace dvtui.Views;

internal static class TuiColors
{
    public static readonly Style SecondaryText = new(Color.LightSlateGrey);
    public static readonly Style EditableColumn = new(Color.SpringGreen1);
    public static readonly Style ActiveField = Style.Parse("black on cyan1");
}
