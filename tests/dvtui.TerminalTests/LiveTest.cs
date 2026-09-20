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

        var service = new DataverseService(url);
        try
        {
            Console.WriteLine("Connecting...");
            service.Connect();
            Console.WriteLine("Connected.");

            var solution = PickSolution(service, solutionName);
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

            var schema = service.CreateSchemaService();
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

            return RunLifecycle(service, schema, context, runMarker,
                ledger);
        }
        catch( Exception ex )
        {
            Console.Error.WriteLine("Live test failed: " + ex);
            ledger.Add("error", ex.ToString());
            return 1;
        }
        finally
        {
            service.Dispose();
            ledger.Flush();
            SessionLog.Stop();
        }
    }

    private static DataverseSolution? PickSolution(
        DataverseService service,
        string? name
    )
    {
        var solutions = service.GetSolutionsAsync(
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
        DataverseService service,
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
                    PrimaryNameMaxLength = ColumnDefaults.PrimaryNameLength
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
                    MaxLength = ColumnDefaults.TextLength,
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

            var fresh = schema.LoadColumnDefinitionAsync(
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
                || fresh.MaxLength != ColumnDefaults.TextLength )
            {
                throw new InvalidOperationException(
                    "Column readback did not match the created identity."
                );
            }
            Console.WriteLine(
                $"Read back column: {fresh.SchemaName} "
                + $"len={fresh.MaxLength}"
            );
            var listed = service.GetColumnsAsync(
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

            var unpublishedRequirement = schema.LoadColumnDefinitionAsync(
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
                tableLogicalName,
                CancellationToken.None,
                context,
                tableId
            ).GetAwaiter().GetResult();
            Console.WriteLine("Published table.");

            var published = schema.LoadColumnDefinitionAsync(
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
                tableLogicalName,
                columnLogicalName,
                columnId,
                CancellationToken.None,
                tableId,
                context
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
                service,
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
                tableLogicalName,
                columnLogicalName,
                columnId,
                cancellation.Token,
                tableId,
                context
            ).GetAwaiter().GetResult();
            ledger.Remove("column");
            Console.WriteLine("Cleaned up column: " + columnLogicalName);
        }
        catch( Exception ex ) when( IsMetadataNotFound(ex) )
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
        DataverseService service,
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
                entity = service.GetEntityAsync(
                    tableLogicalName,
                    tableId,
                    cancellation.Token
                ).GetAwaiter().GetResult();
            }
            catch( Exception ex ) when( IsMetadataNotFound(ex) )
            {
                entity = service.GetEntityByLogicalNameAsync(
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

            service.DeleteTableAsync(
                tableLogicalName,
                tableId,
                cancellation.Token
            ).GetAwaiter().GetResult();
            VerifyTableAbsent(service, tableLogicalName, tableId, cancellation.Token);
            ledger.Remove("table");
            Console.WriteLine("Deleted table: " + tableLogicalName);
        }
        catch( Exception ex )
        {
            if( IsMetadataNotFound(ex) )
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
        DataverseService service,
        string tableLogicalName,
        Guid tableId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            service.GetEntityAsync(
                tableLogicalName,
                tableId,
                cancellationToken
            ).GetAwaiter().GetResult();
        }
        catch( Exception ex ) when( IsMetadataNotFound(ex) )
        {
            return;
        }

        throw new InvalidOperationException(
            "Table cleanup was sent, but the table is still present."
        );
    }

    private static bool IsMetadataNotFound(Exception exception)
    {
        for( var current = exception; current != null; current = current.InnerException )
        {
            if( current.Message.Contains(
                "could not find",
                StringComparison.OrdinalIgnoreCase
            )
                || current.Message.Contains(
                "not found",
                StringComparison.OrdinalIgnoreCase
            )
                || current.Message.Contains(
                    "does not exist",
                    StringComparison.OrdinalIgnoreCase
                )
                || current.Message.Contains(
                    "cannot be found",
                    StringComparison.OrdinalIgnoreCase
                ) )
            {
                return true;
            }
        }

        return false;
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
