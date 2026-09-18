namespace dvtui.Models;

public static class SolutionComponentTypes
{
    public const int Entity = 1;
    public const int Attribute = 2;
    public const int OptionSet = 9;
    public const int SavedQuery = 26;
    public const int Workflow = 29;
    public const int SystemForm = 60;
    public const int WebResource = 61;

    public static string GetDisplayName(int componentType)
    {
        return componentType switch
        {
            Entity => "Table",
            Attribute => "Column",
            OptionSet => "Option set",
            SavedQuery => "View",
            Workflow => "Workflow",
            SystemForm => "System form",
            WebResource => "Web resource",
            _ => $"Type {componentType}"
        };
    }
}
