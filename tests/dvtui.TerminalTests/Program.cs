using Spectre.Console;
using Spectre.Console.Rendering;
using dvtui.Models;
using dvtui.Services;
using dvtui.Views;
using dvtui.TerminalTests;

var mode = args.FirstOrDefault() ?? "solution-selection";

if( mode == "live-test" )
{
    TerminalSession.ConfigureWslBrowser();
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
    var services = BuildFakeServices(context);
    var result = RunTableColumnsScreen(
        services.Query,
        services.Schema,
        context,
        entity
    );
    Console.WriteLine(result);
    return 0;
}

if( mode == "table-columns-mutation" )
{
    var context = BuildWriteContext();
    var services = BuildFakeServices(
        context,
        TimeSpan.FromSeconds(2)
    );
    var result = RunTableColumnsMutationScreen(
        services.Query,
        services.Schema,
        context,
        entity
    );
    Console.WriteLine(result);
    return 0;
}

if( mode == "create-table" )
{
    var context = BuildWriteContext();
    var services = BuildFakeServices(context);
    var result = RunCreateTableScreen(services.Schema, context);
    Console.WriteLine(result);
    return 0;
}

if( mode == "create-table-slow" )
{
    var context = BuildWriteContext();
    var services = BuildFakeServices(context, TimeSpan.FromSeconds(10));
    var result = RunCreateTableScreen(services.Schema, context);
    Console.WriteLine(result);
    return 0;
}

if( mode == "column-editor" )
{
    var context = BuildWriteContext();
    var services = BuildFakeServices(context);
    var result = RunColumnEditorScreen(
        services.Schema,
        context,
        entity,
        null
    );
    Console.WriteLine(result);
    return 0;
}

if( mode == "column-editor-edit" )
{
    var context = BuildWriteContext();
    var services = BuildFakeServices(context);
    var existing = services.Query
        .GetColumnsAsync(
            entity.LogicalName,
            entity.MetadataId,
            CancellationToken.None
        )
        .GetAwaiter()
        .GetResult()
        .First(column => column.CanModifyAdditionalSettings == true);
    var result = RunColumnEditorScreen(
        services.Schema,
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

FakeDataverseServices BuildFakeServices(
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
    return new FakeDataverseServices(
        context,
        columns,
        tableIds,
        operationDelay
    );
}

static string RunTableColumnsScreen(
    IDataverseQueryService queryService,
    IDataverseSchemaService schemaService,
    SolutionWriteContext context,
    DataverseEntity entity
)
{
    var screen = new TableColumnsScreen(
        queryService,
        schemaService,
        entity,
        context
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
                .StartAsync(ctx => ScreenRunner.RunTableColumnsAsync(
                    screen,
                    ctx,
                    loadColumns: true
                ))
                .GetAwaiter()
                .GetResult();

            result = screen.PendingAction.ToString().ToLowerInvariant();
            var pendingColumn = screen.PendingColumn;
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

static string RunTableColumnsMutationScreen(
    IDataverseQueryService queryService,
    IDataverseSchemaService schemaService,
    SolutionWriteContext context,
    DataverseEntity entity
)
{
    var screen = new TableColumnsScreen(
        queryService,
        schemaService,
        entity,
        context
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
        var loadColumns = true;
        SchemaMutation? mutation = null;
        try
        {
            while( true )
            {
                AnsiConsole.Clear();
                AnsiConsole.Live(screen.Render())
                    .StartAsync(ctx => ScreenRunner.RunTableColumnsAsync(
                        screen,
                        ctx,
                        loadColumns,
                        null,
                        mutation
                    ))
                    .GetAwaiter()
                    .GetResult();
                loadColumns = false;
                mutation = null;
                var action = screen.PendingAction;
                var pendingColumn = screen.PendingColumn;
                if( action == TableColumnsAction.SaveColumn
                    && screen.Editor != null )
                {
                    var saved = RunEmbeddedColumnEditor(
                        screen,
                        screen.Editor
                    );
                    screen.CompleteEdit(saved);
                    if( saved )
                    {
                        loadColumns = true;
                    }
                    continue;
                }
                if( action == TableColumnsAction.DeleteColumn
                    && pendingColumn != null )
                {
                    screen.ResetAction();
                    mutation = PrepareDeleteMutation(
                        schemaService,
                        context,
                        entity,
                        pendingColumn
                    );
                    continue;
                }
                if( action == TableColumnsAction.Publish )
                {
                    screen.ResetAction();
                    mutation = PreparePublishMutation(
                        schemaService,
                        context,
                        entity
                    );
                    continue;
                }
                result = action.ToString().ToLowerInvariant();
                if( pendingColumn != null )
                {
                    result += ":" + pendingColumn.SchemaName;
                }
                break;
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

static bool RunEmbeddedColumnEditor(
    TableColumnsScreen host,
    ColumnEditorScreen editor
)
{
    AnsiConsole.Clear();
    AnsiConsole.Live(host.Render())
        .StartAsync(ctx => ScreenRunner.RunFormAsync(
            editor,
            ctx,
            token => editor.SubmitAsync(token),
            host.Render,
            retryAfterError: false
        ))
        .GetAwaiter()
        .GetResult();
    return editor.MutationSucceeded;
}

static SchemaMutation PrepareDeleteMutation(
    IDataverseSchemaService schemaService,
    SolutionWriteContext context,
    DataverseEntity entity,
    DataverseColumn column
)
{
    return new SchemaMutation(
        SchemaMutationKind.DeleteColumn,
        "Deleting column " + column.LogicalName
            + " from table " + entity.LogicalName,
        token => schemaService.DeleteColumnAsync(
            new DeleteColumnRequest
            {
                TableLogicalName = entity.LogicalName,
                ColumnLogicalName = column.LogicalName,
                ExpectedMetadataId = column.MetadataId,
                TableMetadataId = entity.MetadataId,
                Context = context
            },
            token
        )
    );
}

static SchemaMutation PreparePublishMutation(
    IDataverseSchemaService schemaService,
    SolutionWriteContext context,
    DataverseEntity entity
)
{
    return new SchemaMutation(
        SchemaMutationKind.PublishTable,
        "Publishing table " + entity.LogicalName,
        token => schemaService.PublishTableAsync(
            new PublishTableRequest
            {
                TableLogicalName = entity.LogicalName,
                TableMetadataId = entity.MetadataId,
                Context = context
            },
            token
        )
    );
}

static string RunCreateTableScreen(
    IDataverseSchemaService schemaService,
    SolutionWriteContext context
)
{
    var screen = new CreateTableScreen(schemaService, context);
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
                .StartAsync(ctx => ScreenRunner.RunFormAsync(
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
    IDataverseSchemaService schemaService,
    SolutionWriteContext context,
    DataverseEntity entity,
    DataverseColumn? existing
)
{
    var screen = new ColumnEditorScreen(
        schemaService,
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
                .StartAsync(ctx => ScreenRunner.RunFormAsync(
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
