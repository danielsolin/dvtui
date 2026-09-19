using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Models;
using dvtui.Services;
using dvtui.Views;
using dvtui.TerminalTests;
using Progress = dvtui.Views.Progress;

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
    Description = "Customer accounts",
    PrimaryIdAttribute = "accountid",
    PrimaryNameAttribute = "name",
    IsCustomizable = true,
    CanCreateAttributes = true
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
    new()
    {
        LogicalName = "accountid",
        SchemaName = "AccountId",
        DisplayName = "Account ID",
        Type = "accountid"
    },
    new()
    {
        LogicalName = "name",
        SchemaName = "Name",
        DisplayName = "Name",
        Type = "string"
    },
    new()
    {
        LogicalName = "new_note",
        SchemaName = "New_Note",
        DisplayName = "Note",
        Type = "memo"
    },
    new()
    {
        LogicalName = "parentid",
        SchemaName = "ParentId",
        DisplayName = "Parent",
        Type = "lookup"
    }
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

if( mode == "solution-browser" || mode == "solution-browser-readonly" )
{
    var browserCanWrite = mode == "solution-browser";
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
        },
        browserCanWrite,
        browserCanWrite ? null : ColumnCapabilityPolicy.ReasonManagedSolution
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

if( mode == "create-table-slow" )
{
    var context = BuildWriteContext();
    var service = BuildFakeService(context, TimeSpan.FromSeconds(10));
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

if( mode == "confirmation" )
{
    var screen = new ConfirmationScreen(
        "Delete column",
        [
            ("Environment", "https://test.crm.dynamics.com"),
            ("Solution", "contoso"),
            ("Table", "account"),
            ("Column", "new_custom")
        ],
        ["This deletes the column and its stored data."],
        "Type the full column logical name to confirm.",
        value => value == "new_custom"
    );
    AnsiConsole.AlternateScreen(() =>
    {
        var confirmed = screen.Show();
        Console.WriteLine(confirmed ? "confirmed" : "cancelled");
    });
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
        BaseLanguage = "1033",
        EnvironmentUrl = "https://test.crm.dynamics.com"
    };
}

FakeDataverseService BuildFakeService(
    SolutionWriteContext context,
    TimeSpan? operationDelay = null
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
                IsLogical = false,
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
                IsLogical = false,
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
        tableIds,
        operationDelay
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
                    using var progressHost = Progress.Attach();
                    using var cancellation = new CancellationTokenSource();
                    try
                    {
                        var loadTask = screen.LoadAsync(
                            cancellation.Token
                        );
                        var lastRevision = -1;
                        var lastSize = (Width: 0, Height: 0);
                        while( true )
                        {
                            var refresh = false;
                            if( loadTask != null
                                && loadTask.IsCompleted )
                            {
                                loadTask = null;
                                refresh = true;
                            }

                            var inputLocked = Progress.DiscardPendingInput(
                                "TableColumnsScreen"
                            );
                            while( !inputLocked && Console.KeyAvailable )
                            {
                                if( Progress.DiscardPendingInput(
                                    "TableColumnsScreen"
                                ) )
                                {
                                    inputLocked = true;
                                    break;
                                }

                                var key = Console.ReadKey(
                                    intercept: true
                                );
                                if( key.Key == ConsoleKey.R
                                    && loadTask == null )
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

                            var size = (
                                AnsiConsole.Profile.Width,
                                AnsiConsole.Profile.Height
                            );
                            var revision = screen.Revision;
                            if( refresh
                                || revision != lastRevision
                                || Progress.IsActive
                                || size != lastSize )
                            {
                                ctx.UpdateTarget(screen.Render());
                                lastRevision = revision;
                                lastSize = size;
                            }
                            await Task.Delay(100);
                        }
                    }
                    finally
                    {
                        cancellation.Cancel();
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
        entity.MetadataId,
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
    using var progressHost = Progress.Attach();
    using var cancellation = new CancellationTokenSource();
    while( true )
    {
        var lastRevision = -1;
        var lastSize = (Width: 0, Height: 0);
        while( screen.PendingAction == FormAction.None )
        {
            var inputLocked = Progress.DiscardPendingInput(
                typeof(TScreen).Name
            );
            if( !inputLocked && Console.KeyAvailable )
            {
                var key = Console.ReadKey(intercept: true);
                screen.HandleKey(key);
            }

            var size = (
                AnsiConsole.Profile.Width,
                AnsiConsole.Profile.Height
            );
            var revision = screen.Revision;
            if( revision != lastRevision
                || size != lastSize )
            {
                context.UpdateTarget(screen.Render());
                lastRevision = revision;
                lastSize = size;
            }
            await Task.Delay(100);
        }

        if( screen.PendingAction == FormAction.Close )
        {
            return;
        }

        var submitTask = Progress.Show(
            screen.ProgressMessage,
            cancellation.Token,
            submit
        );
        lastRevision = -1;
        lastSize = (Width: 0, Height: 0);
        while( !submitTask.IsCompleted )
        {
            Progress.DiscardPendingInput(typeof(TScreen).Name);

            var size = (
                AnsiConsole.Profile.Width,
                AnsiConsole.Profile.Height
            );
            var revision = screen.Revision;
            if( revision != lastRevision
                || Progress.IsActive
                || size != lastSize )
            {
                context.UpdateTarget(screen.Render());
                lastRevision = revision;
                lastSize = size;
            }
            await Task.Delay(100);
        }

        await submitTask;
        context.UpdateTarget(screen.Render());
        if( screen.OutcomeUnknown )
        {
            return;
        }

        if( screen.HasError )
        {
            screen.ResetForRetry();
            continue;
        }

        await Task.Delay(500);
        return;
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
