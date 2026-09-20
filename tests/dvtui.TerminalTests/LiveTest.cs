using System.Text;
using dvtui.Models;
using dvtui.Services;

namespace dvtui.TerminalTests;

internal static class LiveTest
{
    public static int Run(string[] args)
    {
        SessionLog.Start(args);
        Console.WriteLine("Session log: " + (SessionLog.FilePath ?? "unavailable"));
        var url = args.Length > 0 ? args[0] : null;
        if( string.IsNullOrWhiteSpace(url) )
        {
            Console.Error.WriteLine(
                "Usage: live-test <environment-url> [solution-unique-name]"
            );
            return 2;
        }

        var solutionName = args.Length > 1 ? args[1] : null;
        var runMarker = "dvtui-" + DateTime.UtcNow.ToString(
            "yyyyMMdd-HHmmss"
        );
        var ledgerPath = Path.Combine(
            Path.GetTempPath(),
            "dvtui-live-test-" + runMarker + ".ledger"
        );
        var ledger = new Ledger(ledgerPath, runMarker);
        Console.WriteLine("Run marker: " + runMarker);
        Console.WriteLine("Ledger: " + ledgerPath);

        var connection = new DataverseConnectionManager(url);
        try
        {
            Console.WriteLine("Connecting...");
            connection.Connect();
            Console.WriteLine("Connected.");

            var solution = PickSolution(connection.QueryService, solutionName);
            if( solution == null )
            {
                Console.Error.WriteLine(
                    "No suitable unmanaged solution found."
                );
                return 1;
            }

            Console.WriteLine(
                $"Solution: {solution.FriendlyName} "
                + $"({solution.UniqueName})"
            );

            var schema = connection.SchemaService;
            var context = schema.LoadWriteContextAsync(
                solution,
                CancellationToken.None
            ).GetAwaiter().GetResult();

            Console.WriteLine(
                $"Publisher prefix: {context.PublisherPrefix}"
            );
            Console.WriteLine($"Base language: {context.BaseLanguage}");
            ledger.Add(
                "context",
                $"solution={context.SolutionId} "
                + $"prefix={context.PublisherPrefix} "
                + $"lang={context.BaseLanguage}"
            );

            return RunLifecycle(
                connection.QueryService,
                schema,
                context,
                runMarker,
                ledger
            );
        }
        catch( Exception ex )
        {
            Console.Error.WriteLine("Live test failed: " + ex);
            ledger.Add("error", ex.ToString());
            return 1;
        }
        finally
        {
            connection.Dispose();
            ledger.Flush();
            SessionLog.Stop();
        }
    }

    private static DataverseSolution? PickSolution(
        IDataverseQueryService queryService,
        string? name
    )
    {
        var solutions = queryService.GetSolutionsAsync(
            CancellationToken.None
        ).GetAwaiter().GetResult();
        var unmanaged = solutions
            .Where(s => s.IsManaged != true)
            .ToList();

        if( unmanaged.Count == 0 )
        {
            return null;
        }

        if( string.IsNullOrWhiteSpace(name) )
        {
            return unmanaged[0];
        }

        return unmanaged.FirstOrDefault(s =>
            s.UniqueName == name || s.FriendlyName == name
        );
    }

    private static int RunLifecycle(
        IDataverseQueryService queryService,
        DataverseSchemaService schema,
        SolutionWriteContext context,
        string runMarker,
        Ledger ledger
    )
    {
        var suffix = "lv" + Guid.NewGuid().ToString("N")[..8];
        var tableLogicalName = BuildName(context.PublisherPrefix, suffix);
        Console.WriteLine("Table logical name: " + tableLogicalName);

        var tableId = Guid.Empty;
        var columnLogicalName = string.Empty;
        var columnId = Guid.Empty;
        try
        {
            var tableIdResult = schema.CreateTableAsync(
                new CreateTableRequest
                {
                    Context = context,
                    DisplayName = "Live Test " + runMarker,
                    PluralDisplayName = "Live Tests " + runMarker,
                    SchemaSuffix = suffix,
                    Description = "dvtui live test " + runMarker,
                    IsUserOwned = false,
                    PrimaryNameDisplayName = "Name",
                    PrimaryNameSchemaSuffix = "name",
                    PrimaryNameMaxLength = ColumnDefaults.PrimaryNameDefaultLength
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
            tableId = tableIdResult;
            ledger.Add("table", $"{tableLogicalName} id={tableId}");
            Console.WriteLine("Created table: " + tableId);

            var columnSuffix = "txt" + Guid.NewGuid().ToString("N")[..6];
            columnLogicalName = BuildName(
                context.PublisherPrefix,
                columnSuffix
            );
            var columnIdResult = schema.CreateColumnAsync(
                new CreateColumnRequest
                {
                    Context = context,
                    TableLogicalName = tableLogicalName,
                    TableMetadataId = tableId,
                    DisplayName = "Live Text",
                    SchemaSuffix = columnSuffix,
                    Description = "dvtui live text " + runMarker,
                    Kind = ColumnKind.Text,
                    MaxLength = ColumnDefaults.TextDefaultLength,
                    MinValue = null,
                    MaxValue = null,
                    Precision = null,
                    RequirementLevel = RequirementLevels.Optional
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
            columnId = columnIdResult;
            ledger.Add(
                "column",
                $"{columnLogicalName} id={columnId}"
            );
            Console.WriteLine("Created column: " + columnId);

            var fresh = schema.GetColumnDefinitionAsync(
                tableLogicalName,
                columnLogicalName,
                retrieveAsIfPublished: true,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            if( fresh.MetadataId != columnId
                || !string.Equals(
                    fresh.LogicalName,
                    columnLogicalName,
                    StringComparison.OrdinalIgnoreCase
                )
                || fresh.MaxLength != ColumnDefaults.TextDefaultLength )
            {
                throw new InvalidOperationException(
                    "Column readback did not match the created identity."
                );
            }
            Console.WriteLine(
                $"Read back column: {fresh.SchemaName} "
                + $"len={fresh.MaxLength}"
            );
            var listed = queryService.GetColumnsAsync(
                tableLogicalName,
                tableId,
                CancellationToken.None
            ).GetAwaiter().GetResult().First(column =>
                string.Equals(
                    column.LogicalName,
                    columnLogicalName,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            var listedCapability = ColumnCapabilityPolicy.Evaluate(listed);
            if( listed.Kind != ColumnKind.Text
                || listed.AttributeFormat != "Text"
                || !listedCapability.CanEdit )
            {
                throw new InvalidOperationException(
                    "Column list readback did not identify the text column "
                    + "as editable."
                );
            }
            Console.WriteLine("Listed column is editable text.");

            var requirementResult = schema.UpdateColumnAsync(
                new UpdateColumnRequest
                {
                    Context = context,
                    TableLogicalName = tableLogicalName,
                    TableMetadataId = tableId,
                    ColumnLogicalName = columnLogicalName,
                    ExpectedMetadataId = fresh.MetadataId,
                    SetRequirementLevel = true,
                    RequirementLevel = RequirementLevels.Recommended
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
            if( !requirementResult.Changed )
            {
                throw new InvalidOperationException(
                    "Requirement level update reported no change."
                );
            }

            var unpublishedRequirement = schema.GetColumnDefinitionAsync(
                tableLogicalName,
                columnLogicalName,
                retrieveAsIfPublished: true,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            if( unpublishedRequirement.RequirementLevel
                != RequirementLevels.Recommended )
            {
                throw new InvalidOperationException(
                    "Unpublished requirement readback did not match the edit."
                );
            }
            Console.WriteLine("Updated unpublished requirement level.");

            var updateResult = schema.UpdateColumnAsync(
                new UpdateColumnRequest
                {
                    Context = context,
                    TableLogicalName = tableLogicalName,
                    TableMetadataId = tableId,
                    ColumnLogicalName = columnLogicalName,
                    ExpectedMetadataId = unpublishedRequirement.MetadataId,
                    SetDisplayName = true,
                    DisplayName = "Live Text Edited",
                    SetDescription = false,
                    Description = unpublishedRequirement.Description,
                    NewMaxLength = null,
                    SetRequirementLevel = false,
                    RequirementLevel = unpublishedRequirement.RequirementLevel
                        ?? RequirementLevels.Optional
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
            Console.WriteLine(
                $"Updated column: changed={updateResult.Changed}"
            );

            schema.PublishTableAsync(
                new PublishTableRequest
                {
                    TableLogicalName = tableLogicalName,
                    TableMetadataId = tableId,
                    Context = context
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
            Console.WriteLine("Published table.");

            var published = schema.GetColumnDefinitionAsync(
                tableLogicalName,
                columnLogicalName,
                retrieveAsIfPublished: true,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            if( published.MetadataId != columnId
                || published.DisplayName != "Live Text Edited"
                || published.RequirementLevel != RequirementLevels.Recommended )
            {
                throw new InvalidOperationException(
                    "Published column readback did not match the edit."
                );
            }
            Console.WriteLine(
                $"Published display name: {published.DisplayName}"
            );

            var dependencies = schema
                .GetColumnDeleteDependenciesAsync(
                    columnId,
                    CancellationToken.None
                )
                .GetAwaiter().GetResult();
            Console.WriteLine(
                $"Dependencies: {dependencies.Count}"
            );

            schema.DeleteColumnAsync(
                new DeleteColumnRequest
                {
                    TableLogicalName = tableLogicalName,
                    ColumnLogicalName = columnLogicalName,
                    ExpectedMetadataId = columnId,
                    TableMetadataId = tableId,
                    Context = context
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
            Console.WriteLine("Deleted column.");
            ledger.Remove("column");
            columnId = Guid.Empty;

            Console.WriteLine("Lifecycle complete. Cleaning up table...");
            return 0;
        }
        catch( SchemaWriteOutcomeUnknownException ex )
        {
            if( ex.MetadataId.HasValue )
            {
                if( tableId == Guid.Empty && string.IsNullOrWhiteSpace(
                    columnLogicalName
                ) )
                {
                    tableId = ex.MetadataId.Value;
                    ledger.Add(
                        "table",
                        $"{tableLogicalName} id={tableId} outcome=unknown"
                    );
                }
                else if( columnId == Guid.Empty
                    && !string.IsNullOrWhiteSpace(columnLogicalName) )
                {
                    columnId = ex.MetadataId.Value;
                    ledger.Add(
                        "column",
                        $"{columnLogicalName} id={columnId} outcome=unknown"
                    );
                }
            }

            Console.Error.WriteLine(
                "Lifecycle outcome is unknown: " + ex.Message
            );
            ledger.Add("error", ex.Message);
            return 1;
        }
        catch( Exception ex )
        {
            Console.Error.WriteLine(
                "Lifecycle failed: " + ex
            );
            ledger.Add("error", ex.ToString());
            return 1;
        }
        finally
        {
            CleanupColumn(
                schema,
                context,
                tableLogicalName,
                columnLogicalName,
                tableId,
                columnId,
                ledger
            );
            CleanupTable(
                queryService,
                schema,
                tableLogicalName,
                tableId,
                ledger
            );
        }
    }

    private static void CleanupColumn(
        DataverseSchemaService schema,
        SolutionWriteContext context,
        string tableLogicalName,
        string columnLogicalName,
        Guid tableId,
        Guid columnId,
        Ledger ledger
    )
    {
        if( tableId == Guid.Empty || columnId == Guid.Empty )
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(30)
            );
            schema.DeleteColumnAsync(
                new DeleteColumnRequest
                {
                    TableLogicalName = tableLogicalName,
                    ColumnLogicalName = columnLogicalName,
                    ExpectedMetadataId = columnId,
                    TableMetadataId = tableId,
                    Context = context
                },
                cancellation.Token
            ).GetAwaiter().GetResult();
            ledger.Remove("column");
            Console.WriteLine("Cleaned up column: " + columnLogicalName);
        }
        catch( Exception ex ) when( MetadataUtilities.IsMetadataNotFound(ex) )
        {
            ledger.Remove("column");
            Console.WriteLine("Column already absent: " + columnLogicalName);
        }
        catch( Exception ex )
        {
            Console.Error.WriteLine(
                "Column cleanup failed: " + ex.Message
            );
            ledger.Add(
                "cleanup-error",
                $"column={columnLogicalName} id={columnId} {ex.Message}"
            );
        }
    }

    private static void CleanupTable(
        IDataverseQueryService queryService,
        DataverseSchemaService schema,
        string tableLogicalName,
        Guid tableId,
        Ledger ledger
    )
    {
        if( tableId == Guid.Empty )
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(30)
            );
            DataverseEntityDetails entity;
            try
            {
                entity = queryService.GetEntityAsync(
                    tableLogicalName,
                    tableId,
                    cancellation.Token
                ).GetAwaiter().GetResult();
            }
            catch( Exception ex ) when( MetadataUtilities.IsMetadataNotFound(ex) )
            {
                entity = queryService.GetEntityByLogicalNameAsync(
                    tableLogicalName,
                    cancellation.Token
                ).GetAwaiter().GetResult();
            }
            if( entity.Entity.MetadataId != tableId
                || !string.Equals(
                    entity.Entity.LogicalName,
                    tableLogicalName,
                    StringComparison.OrdinalIgnoreCase
                ) )
            {
                throw new InvalidOperationException(
                    "Cleanup identity verification failed."
                );
            }

            schema.DeleteTableAsync(
                tableLogicalName,
                cancellation.Token
            ).GetAwaiter().GetResult();
            VerifyTableAbsent(
                queryService,
                tableLogicalName,
                tableId,
                cancellation.Token
            );
            ledger.Remove("table");
            Console.WriteLine("Deleted table: " + tableLogicalName);
        }
        catch( Exception ex )
        {
            if( MetadataUtilities.IsMetadataNotFound(ex) )
            {
                ledger.Remove("table");
                Console.WriteLine("Table already absent: " + tableLogicalName);
                return;
            }

            Console.Error.WriteLine(
                "Table cleanup failed: " + ex.Message
            );
            ledger.Add(
                "cleanup-error",
                $"table={tableLogicalName} {ex.Message}"
            );
        }
    }

    private static void VerifyTableAbsent(
        IDataverseQueryService queryService,
        string tableLogicalName,
        Guid tableId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            queryService.GetEntityAsync(
                tableLogicalName,
                tableId,
                cancellationToken
            ).GetAwaiter().GetResult();
        }
        catch( Exception ex ) when( MetadataUtilities.IsMetadataNotFound(ex) )
        {
            return;
        }

        throw new InvalidOperationException(
            "Table cleanup was sent, but the table is still present."
        );
    }

    private static string BuildName(string prefix, string suffix)
    {
        if( string.IsNullOrWhiteSpace(prefix) )
        {
            return suffix;
        }

        return prefix + "_" + suffix;
    }
}
