using dvtui.Models;
using dvtui.Views;

var mode = args.FirstOrDefault() ?? "solution-selection";
var entity = new DataverseEntity
{
    MetadataId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
    LogicalName = "account",
    DisplayName = "Accounts",
    Description = "Customer accounts"
};
var contact = new DataverseEntity
{
    MetadataId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
    LogicalName = "contact",
    DisplayName = "Contacts",
    Description = "Customer contacts"
};
var product = new DataverseEntity
{
    MetadataId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
    LogicalName = "product",
    DisplayName = "Products",
    Description = "Catalog products"
};
var entities = new List<DataverseEntity>
{
    entity,
    contact,
    product
};
var solution = new DataverseSolution
{
    Id = Guid.Parse("00000000-0000-0000-0000-00000000000a"),
    FriendlyName = "Contoso",
    UniqueName = "contoso",
    Version = "1.0.0.0",
    IsManaged = false,
    Description = "Contoso baseline solution"
};
var components = new List<DataverseSolutionComponent>
{
    new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-00000000000b"),
        ObjectId = entity.MetadataId,
        ComponentType = SolutionComponentTypes.Entity,
        RootComponentId = entity.MetadataId,
        RootComponentBehavior = RootComponentBehaviors.IncludeSubcomponents,
        Entity = entity
    },
    new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-00000000000c"),
        ObjectId = contact.MetadataId,
        ComponentType = SolutionComponentTypes.Entity,
        RootComponentId = contact.MetadataId,
        RootComponentBehavior = RootComponentBehaviors.IncludeSubcomponents,
        Entity = contact
    },
    new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-00000000000d"),
        ObjectId = product.MetadataId,
        ComponentType = SolutionComponentTypes.Entity,
        RootComponentId = product.MetadataId,
        RootComponentBehavior = RootComponentBehaviors.IncludeSubcomponents,
        Entity = product
    }
};
var fields = new List<DataverseField>
{
    new() { SchemaName = "accountid", DisplayName = "Account ID", Type = "accountid" },
    new() { SchemaName = "name", DisplayName = "Name", Type = "string" },
    new() { SchemaName = "new_note", DisplayName = "Note", Type = "memo" },
    new() { SchemaName = "parentid", DisplayName = "Parent", Type = "lookup" }
};

if( mode == "solution-selection" )
{
    SolutionSelectionScreen.Show(
        async token =>
        {
            await Task.Delay(50, token);
            return [
                solution,
                new DataverseSolution
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-00000000000e"),
                    FriendlyName = "Contoso managed",
                    UniqueName = "contoso_managed",
                    Version = "1.0.0.0",
                    IsManaged = true,
                    Description = "Managed copy"
                }
            ];
        }
    );
    return;
}

if( mode == "solution-browser" )
{
    var result = SolutionBrowserScreen.Show(
        solution,
        (id, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(components);
        },
        (logicalName, metadataId, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(
                new DataverseEntityDetails
                {
                    Entity = entities.First(item => item.MetadataId == metadataId),
                    Fields = fields
                }
            );
        }
    );
    Console.WriteLine(result);
    return;
}
