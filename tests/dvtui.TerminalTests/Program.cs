using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Models;
using dvtui.Services;
using dvtui.Views;
using dvtui.TerminalTests;

var mode = args.FirstOrDefault() ?? "solution-selection";

if( mode == "live-test" )
{
    ConfigureWslBrowser();
    return LiveTest.Run(args.Skip(1).ToArray());
}

if( mode == "service-tests" )
{
    var failures = ServiceTests.Run();
    Console.WriteLine(failures == 0
        ? "All service tests passed."
        : $"{failures} service test(s) failed.");
    return failures == 0 ? 0 : 1;
}

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
    return 0;
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
    return 0;
}

if( mode == "table-columns" )
{
    var context = BuildWriteContext();
    var service = BuildFakeService(context);
    var result = RunTableColumnsScreen(service, context, entity);
    Console.WriteLine(result);
    return 0;
}

if( mode == "create-table" )
{
    var context = BuildWriteContext();
    var service = BuildFakeService(context);
    var result = RunCreateTableScreen(service, context);
    Console.WriteLine(result);
    return 0;
}

if( mode == "column-editor" )
{
    var context = BuildWriteContext();
    var service = BuildFakeService(context);
    var result = RunColumnEditorScreen(service, context, entity, null);
    Console.WriteLine(result);
    return 0;
}

if( mode == "column-editor-edit" )
{
    var context = BuildWriteContext();
    var service = BuildFakeService(context);
    var existing = service
        .GetColumnsAsync(
            entity.LogicalName,
            entity.MetadataId,
            CancellationToken.None
        )
        .GetAwaiter()
        .GetResult()
        .First(column => column.CanModifyAdditionalSettings == true);
    var result = RunColumnEditorScreen(
        service,
        context,
        entity,
        existing
    );
    Console.WriteLine(result);
    return 0;
}

return 0;

SolutionWriteContext BuildWriteContext()
{
    return new SolutionWriteContext
    {
        SolutionId = solution.Id,
        SolutionUniqueName = solution.UniqueName,
        IsManaged = false,
        PublisherId = Guid.NewGuid(),
        PublisherPrefix = "crtest",
        BaseLanguage = "en-US",
        EnvironmentUrl = "https://test.crm.dynamics.com"
    };
}

FakeDataverseService BuildFakeService(
    SolutionWriteContext context
)
{
    var columns = new Dictionary<Guid, List<DataverseColumn>>
    {
        [entity.MetadataId] =
        [
            new DataverseColumn
            {
                MetadataId = Guid.NewGuid(),
                LogicalName = "new_custom",
                SchemaName = "new_custom",
                DisplayName = "Custom",
                Description = "A custom column",
                Kind = ColumnKind.Text,
                MaxLength = 100,
                IsCustom = true,
                IsManaged = false,
                IsPrimaryId = false,
                IsPrimaryName = false,
                IsCustomizable = true,
                IsRenameable = true,
                CanModifyAdditionalSettings = true,
                RequirementLevel = RequirementLevels.Optional,
                AttributeTypeCode = "string"
            },
            new DataverseColumn
            {
                MetadataId = Guid.NewGuid(),
                LogicalName = "new_standard",
                SchemaName = "new_standard",
                DisplayName = "Standard",
                Description = "A managed column",
                Kind = ColumnKind.WholeNumber,
                IsCustom = false,
                IsManaged = true,
                IsPrimaryId = false,
                IsPrimaryName = false,
                IsCustomizable = false,
                IsRenameable = false,
                CanModifyAdditionalSettings = false,
                RequirementLevel = RequirementLevels.Required,
                AttributeTypeCode = "integer"
            }
        ]
    };
    var tableIds = new Dictionary<string, Guid>
    {
        [entity.LogicalName] = entity.MetadataId
    };
    return new FakeDataverseService(
        context.EnvironmentUrl,
        columns,
        tableIds
    );
}

static string RunTableColumnsScreen(
    DataverseService service,
    SolutionWriteContext context,
    DataverseEntity entity
)
{
    var screen = new TableColumnsScreen(service, entity, context);
    if( Console.IsInputRedirected || Console.IsOutputRedirected )
    {
        return "redirected";
    }

    var previousControlCMode = Console.TreatControlCAsInput;
    string result = "close";
    AnsiConsole.AlternateScreen(() =>
    {
        Console.TreatControlCAsInput = true;
        AnsiConsole.Clear();
        try
        {
            var action = new TableColumnsAction[1];
            var column = new DataverseColumn?[1];
            AnsiConsole.Live(screen.Render())
                .StartAsync(async ctx =>
                {
                    using var cancellation = new CancellationTokenSource();
                    var loadTask = screen.LoadAsync(cancellation.Token);
                    while( true )
                    {
                        if( loadTask != null
                            && loadTask.IsCompleted )
                        {
                            loadTask = null;
                        }

                        while( Console.KeyAvailable )
                        {
                            var key = Console.ReadKey(intercept: true);
                            if( key.Key == ConsoleKey.R )
                            {
                                loadTask = screen.LoadAsync(
                                    cancellation.Token
                                );
                            }
                            else
                            {
                                screen.HandleKey(key);
                            }

                            if( screen.PendingAction !=
                                TableColumnsAction.None )
                            {
                                action[0] = screen.PendingAction;
                                column[0] = screen.PendingColumn;
                                return;
                            }
                        }

                        ctx.UpdateTarget(screen.Render());
                        await Task.Delay(50);
                    }
                })
                .GetAwaiter()
                .GetResult();

            result = action[0].ToString().ToLowerInvariant();
            var pendingColumn = column[0];
            if( pendingColumn != null )
            {
                result += ":" + pendingColumn.SchemaName;
            }
        }
        finally
        {
            AnsiConsole.Cursor.Show();
            Console.TreatControlCAsInput = previousControlCMode;
        }
    });
    return result;
}

static string RunCreateTableScreen(
    DataverseService service,
    SolutionWriteContext context
)
{
    var screen = new CreateTableScreen(service, context);
    if( Console.IsInputRedirected || Console.IsOutputRedirected )
    {
        return "redirected";
    }

    var previousControlCMode = Console.TreatControlCAsInput;
    string result = "close";
    AnsiConsole.AlternateScreen(() =>
    {
        Console.TreatControlCAsInput = true;
        AnsiConsole.Clear();
        try
        {
            AnsiConsole.Live(screen.Render())
                .StartAsync(ctx => RunFormScreen(
                    screen,
                    ctx,
                    token => screen.SubmitAsync(token)
                ))
                .GetAwaiter()
                .GetResult();

            result = screen.PendingAction == FormAction.Submit
                ? (screen.HasError ? "submit-error" : "submit")
                : "close";
        }
        finally
        {
            AnsiConsole.Cursor.Show();
            Console.TreatControlCAsInput = previousControlCMode;
        }
    });
    return result;
}

static string RunColumnEditorScreen(
    DataverseService service,
    SolutionWriteContext context,
    DataverseEntity entity,
    DataverseColumn? existing
)
{
    var screen = new ColumnEditorScreen(
        service,
        context,
        entity.LogicalName,
        existing
    );
    if( Console.IsInputRedirected || Console.IsOutputRedirected )
    {
        return "redirected";
    }

    var previousControlCMode = Console.TreatControlCAsInput;
    string result = "close";
    AnsiConsole.AlternateScreen(() =>
    {
        Console.TreatControlCAsInput = true;
        AnsiConsole.Clear();
        try
        {
            AnsiConsole.Live(screen.Render())
                .StartAsync(ctx => RunFormScreen(
                    screen,
                    ctx,
                    token => screen.SubmitAsync(token)
                ))
                .GetAwaiter()
                .GetResult();

            result = screen.PendingAction == FormAction.Submit
                ? (screen.HasError ? "submit-error" : "submit")
                : "close";
        }
        finally
        {
            AnsiConsole.Cursor.Show();
            Console.TreatControlCAsInput = previousControlCMode;
        }
    });
    return result;
}

static async Task RunFormScreen<TScreen>(
    TScreen screen,
    LiveDisplayContext context,
    Func<CancellationToken, Task> submit
)
    where TScreen : IFormScreen
{
    using var cancellation = new CancellationTokenSource();
    while( screen.PendingAction == FormAction.None )
    {
        if( Console.KeyAvailable )
        {
            var key = Console.ReadKey(intercept: true);
            screen.HandleKey(key);
        }

        context.UpdateTarget(screen.Render());
        await Task.Delay(50);
    }

    if( screen.PendingAction == FormAction.Submit )
    {
        context.UpdateTarget(screen.Render());
        await submit(cancellation.Token);
        context.UpdateTarget(screen.Render());
        await Task.Delay(500);
    }
}

static void ConfigureWslBrowser()
{
    if( !OperatingSystem.IsLinux() )
    {
        return;
    }

    var wslDistribution =
        Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");

    if( string.IsNullOrWhiteSpace(wslDistribution) )
    {
        return;
    }

    if( string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("DE")) )
    {
        Environment.SetEnvironmentVariable("DE", "wsl");
    }
}
