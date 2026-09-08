using dvtui.Models;
using dvtui.Views;

var mode = args.FirstOrDefault() ?? "normal";
var attempts = 0;
EntityBrowserScreen.Show(async cancellationToken =>
{
    attempts++;
    await Task.Delay(mode == "cancel" ? 10000 : 350, cancellationToken);
    if (mode == "retry" && attempts == 1)
    {
        throw new Exception("Simulated [metadata] error");
    }

    var entities = new List<DataverseEntity>();
    if (mode == "empty")
    {
        return entities;
    }

    for (var index = 0; index < 75; index++)
    {
        entities.Add(new DataverseEntity
        {
            LogicalName = mode == "long"
                ? $"table_{index:000}_with_a_very_long_logical_name"
                : $"table_{index:000}",
            DisplayName = $"Display [name] {index:000}",
            SchemaName = $"Table{index:000}",
            IsCustomizable = true,
            IsCustom = index % 2 == 0,
            PrimaryIdAttribute = $"table_{index:000}id",
            Description = string.Join(
                " ",
                Enumerable.Repeat("Long description for scrolling.", 50)
            ) + " DESCRIPTION_END"
        });
    }

    entities.Add(new DataverseEntity
    {
        LogicalName = "excluded_false",
        IsCustomizable = false
    });
    entities.Add(new DataverseEntity
    {
        LogicalName = "excluded_unknown"
    });
    return entities;
}, (logicalName, _) =>
{
    var fields = new List<DataverseField>();
    fields.Add(new DataverseField
    {
        SchemaName = logicalName + "id",
        DisplayName = "Id",
        Type = "UniqueIdentifier",
        Description = "Primary key for " + logicalName + "."
    });
    for (var index = 0; index < 39; index++)
    {
        fields.Add(new DataverseField
        {
            SchemaName = $"field_{index:000}",
            DisplayName = $"Field {index:000}",
            Type = index % 3 == 0 ? "Lookup" : "String",
            Description = $"Description for field {index:000}."
        });
    }

    return Task.FromResult(new DataverseEntityDetails
    {
        Entity = new DataverseEntity { LogicalName = logicalName },
        Fields = fields
    });
});
Console.WriteLine($"Browser exited; attempts: {attempts}");
