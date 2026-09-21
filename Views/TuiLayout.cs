namespace dvtui.Views;

internal static class TuiLayout
{
    public const int SidebarWidthDivisor = 4;
    public const int PanelHorizontalOverhead = 4;

    public static int GetSidebarWidth(int terminalWidth)
    {
        return terminalWidth / SidebarWidthDivisor;
    }

    public static string MinimumSizeHint(string action)
    {
        return "Enlarge the terminal ("
            + TuiConstants.MinimumWidth
            + " x "
            + TuiConstants.MinimumHeight
            + "). "
            + action;
    }
}
