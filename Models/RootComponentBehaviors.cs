namespace dvtui.Models;

public static class RootComponentBehaviors
{
    public const int IncludeSubcomponents = 0;
    public const int DoNotIncludeSubcomponents = 1;
    public const int IncludeAsShellOnly = 2;

    public static string GetDisplayName(int? behavior)
    {
        return behavior switch
        {
            IncludeSubcomponents => "Include subcomponents",
            DoNotIncludeSubcomponents => "Do not include subcomponents",
            IncludeAsShellOnly => "Include as shell only",
            null => "Unknown",
            _ => $"Unknown ({behavior.Value})"
        };
    }
}
