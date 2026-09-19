using System.Text;
using dvtui.Models;
using dvtui.Services;

namespace dvtui.TerminalTests;

internal static class LiveTest
{
    private const string LedgerPath = "live-test-ledger.txt";

    public static int Run(string[] args)
    {
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
        var ledger = new Ledger(LedgerPath, runMarker);
        Console.WriteLine("Run marker: " + runMarker);
        Console.WriteLine("Ledger: " + LedgerPath);

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
            Console.Error.WriteLine("Live test failed: " + ex.Message);
            ledger.Add("error", ex.Message);
            return 1;
        }
        finally
        {
            service.Dispose();
            ledger.Flush();
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
                retrieveAsIfPublished: false,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            Console.WriteLine(
                $"Read back column: {fresh.SchemaName} "
                + $"len={fresh.MaxLength}"
            );

            var updateResult = schema.UpdateColumnAsync(
                new UpdateColumnRequest
                {
                    Context = context,
                    TableLogicalName = tableLogicalName,
                    ColumnLogicalName = columnLogicalName,
                    ExpectedMetadataId = fresh.MetadataId,
                    SetDisplayName = true,
                    DisplayName = "Live Text Edited",
                    SetDescription = false,
                    Description = fresh.Description,
                    NewMaxLength = null,
                    SetRequirementLevel = false,
                    RequirementLevel = fresh.RequirementLevel
                        ?? RequirementLevels.Optional
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
            Console.WriteLine(
                $"Updated column: changed={updateResult.Changed}"
            );

            schema.PublishTableAsync(
                tableLogicalName,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            Console.WriteLine("Published table.");

            var published = schema.LoadColumnDefinitionAsync(
                tableLogicalName,
                columnLogicalName,
                retrieveAsIfPublished: true,
                CancellationToken.None
            ).GetAwaiter().GetResult();
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
                CancellationToken.None
            ).GetAwaiter().GetResult();
            Console.WriteLine("Deleted column.");
            ledger.Remove("column");

            Console.WriteLine("Lifecycle complete. Cleaning up table...");
            CleanupTable(
                service,
                tableLogicalName,
                tableId,
                ledger
            );
            return 0;
        }
        catch( Exception ex )
        {
            Console.Error.WriteLine(
                "Lifecycle failed: " + ex.Message
            );
            ledger.Add("error", ex.Message);
            return 1;
        }
    }

    private static void CleanupTable(
        DataverseService service,
        string tableLogicalName,
        Guid tableId,
        Ledger ledger
    )
    {
        try
        {
            service.DeleteTableAsync(
                tableLogicalName,
                tableId,
                CancellationToken.None
            ).GetAwaiter().GetResult();
            ledger.Remove("table");
            Console.WriteLine("Deleted table: " + tableLogicalName);
        }
        catch( Exception ex )
        {
            Console.Error.WriteLine(
                "Table cleanup failed: " + ex.Message
            );
            ledger.Add(
                "cleanup-error",
                $"table={tableLogicalName} {ex.Message}"
            );
        }
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

internal sealed class Ledger
{
    private readonly string _path;
    private readonly string _marker;
    private readonly List<string> _lines = [];

    public Ledger(string path, string marker)
    {
        _path = path;
        _marker = marker;
    }

    public void Add(string key, string value)
    {
        _lines.Add($"[{_marker}] {key}: {value}");
        Flush();
    }

    public void Remove(string key)
    {
        _lines.RemoveAll(line => line.Contains(key + ":"));
        Flush();
    }

    public void Flush()
    {
        File.WriteAllLines(_path, _lines);
    }
}
