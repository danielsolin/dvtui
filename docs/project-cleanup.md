# dvtui Project Cleanup Report

Code review findings for dead code, duplication, and structure
improvements. Findings are ordered by priority.

---

## 1. Duplicated Validation Constants (High Priority)

The constants `MaxDisplayLength` (125), `MaxDescriptionLength` (4000),
and `MaxSchemaNameLength` / `MaxSuffixLength` (80) are each defined in
**three** files:

| File | Lines |
|---|---|
| `Services/DataverseSchemaService.cs` | 27–29 |
| `Views/CreateTableScreen.cs` | 12–14 |
| `Views/ColumnEditorScreen.cs` | 12–14 |

All three files already reference `Models/ColumnDefaults.cs` for other
limits (`TextLength`, `MultilineLength`, etc.), so that file is the
natural home.

**Recommendation:** Move the three constants to `ColumnDefaults.cs` as
`public const` fields. Suggested names:

- `DisplayLengthMax = 125`
- `DescriptionLengthMax = 4000`
- `SchemaNameLengthMax = 80`

Then remove the private copies from all three files and update
references.

---

## 2. Duplicated Helper Methods (High Priority)

`GetOptionValue` is defined in both:

- `Services/DataverseService.cs` (line 566)
- `Services/DataverseSchemaService.cs` (line 2461)

`GetGuidValue` is defined in:

- `Services/DataverseSchemaService.cs` (line 2473)

Both methods have identical logic.

**Recommendation:** Extract to an internal static class, e.g.
`Services/EntityExtensions.cs`:

```csharp
internal static class EntityExtensions
{
    public static int? GetOptionValue(this Entity entity, string attribute)
    {
        if( !entity.Contains(attribute) )
        {
            return null;
        }

        return entity.GetAttributeValue<OptionSetValue>(attribute)?.Value;
    }

    public static Guid? GetGuidValue(this Entity entity, string attribute)
    {
        if( !entity.Contains(attribute) )
        {
            return null;
        }

        return entity[attribute] switch
        {
            Guid value => value,
            EntityReference reference => reference.Id,
            _ => null
        };
    }
}
```

Remove the private copies from both service files.

---

## 3. Duplicated UI Timing Constants (Medium Priority)

These constants are repeated across multiple view files:

| Constant | Value | Defined in |
|---|---|---|
| `RefreshIntervalMilliseconds` | 80 | `Progress.cs`, `SolutionBrowserScreen.cs`, `SolutionSelectionScreen.cs`, `StartupScreen.cs` |
| `ShutdownTimeoutMilliseconds` | 2000 | `SolutionBrowserScreen.cs`, `SolutionSelectionScreen.cs` |
| `MinimumWidth` | 60 | `SolutionBrowserScreen.cs`, `SolutionSelectionScreen.cs` |
| `MinimumHeight` | 10 | `SolutionBrowserScreen.cs`, `SolutionSelectionScreen.cs` |

**Recommendation:** Create `Views/TuiConstants.cs` with `public const`
fields for the shared values. Keep file-specific constants (e.g.
`FormWidth` in `StartupScreen.cs`) where they are.

---

## 4. `Program.cs` Is Too Large (Medium Priority)

`Program.cs` is 1 340 lines and contains `Main`, `RunSolutionLoop`,
`RunCreateTable`, `RunTableColumns`, `RunColumnEditor`, `RunFormAsync`,
`RunSchemaMutation`, and other orchestration methods. UI orchestration
logic is mixed with startup routing.

**Recommendation:** Extract `RunFormAsync` and `RunSchemaMutation` into
a dedicated class, e.g. `Views/ScreenRunner.cs`. `Program.cs` should
retain only `Main` and top-level routing.

---

## 5. `DataverseSchemaService.cs` Is Too Large (Lower Priority)

The file is 2 720 lines and contains validation logic, Web API
fallback, schema operations, metadata mapping, and JSON parsing.

**Recommendation (larger refactor):** Split into:

- `Services/SchemaValidation.cs` — all `Validate*` methods
- `Services/WebApiClient.cs` — Web API fallback and JSON parsing
- `Services/DataverseSchemaService.cs` — remaining orchestration

This is a higher-risk change and should be done after the simpler
items above are complete.

---

## 6. `ColumnCapabilityPolicy` — Internal-Only Reason Constants (Observation)

Several `public const` reason strings in `ColumnCapabilityPolicy.cs`
are never referenced outside the file:

- `ReasonManagedColumn`
- `ReasonSystemColumn`
- `ReasonSpecialized`
- `ReasonUnsupportedKind`
- `ReasonNotCustomizable`
- `ReasonPrimaryId`
- `ReasonPrimaryName`

They are only used as `EditReason` / `DeleteReason` values inside
`ColumnCapability` objects. This is not a bug, but if these strings are
ever needed externally (e.g. in tests), they will need to stay `public`.
No action required.

---

## 7. `FakeDataverseService` Inherits from `DataverseService` (Observation)

`FakeDataverseService` in `tests/dvtui.TerminalTests/` subclasses
`DataverseService` and overrides virtual methods. This works but couples
the test to the concrete class. If a new non-virtual method is added to
`DataverseService`, the fake will not expose it.

An `IDataverseService` interface would decouple this, but it is a
larger refactor and may not be worth the effort at this stage.

---

## 8. `Ledger` Class in `LiveTest.cs` (Low Priority)

The `Ledger` class (line 580 of `LiveTest.cs`) is an internal helper
for recording identities during the live test. It is only used by
`LiveTest`.

**Recommendation:** If `LiveTest.cs` grows further, extract `Ledger`
into its own file: `tests/dvtui.TerminalTests/Ledger.cs`.

---

## Summary — Prioritized Action List

| # | Change | Risk | Benefit |
|---|---|---|---|
| 1 | Move `MaxDisplayLength`, `MaxDescriptionLength`, `MaxSchemaNameLength` to `ColumnDefaults.cs` | Low | High |
| 2 | Extract `GetOptionValue` / `GetGuidValue` to `EntityExtensions.cs` | Low | High |
| 3 | Move `RefreshIntervalMilliseconds` etc. to `TuiConstants.cs` | Low | Medium |
| 4 | Split `Program.cs` (extract `RunFormAsync`, `RunSchemaMutation`) | Medium | Medium |
| 5 | Split `DataverseSchemaService.cs` | High | High |
| 6 | Extract `Ledger` to its own file | Low | Low |
