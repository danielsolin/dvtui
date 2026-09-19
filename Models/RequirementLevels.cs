namespace dvtui.Models;

public static class RequirementLevels
{
    public const string Optional = "1";
    public const string Recommended = "2";
    public const string Required = "3";

    public static string GetDisplayName(string level)
    {
        return level switch
        {
            Optional => "Optional",
            Recommended => "Business recommended",
            Required => "Business required",
            _ => "Optional"
        };
    }
}
