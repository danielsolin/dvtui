# Code Cleanup Review Report - 20260920

## 1. Monolithic Service: `DataverseSchemaService`

- **Finding:** The `DataverseSchemaService` has grown too large (1,574 lines).
- **Location:** `Services/DataverseSchemaService.cs`
- **Why:** It violates the Single Responsibility Principle by handling multiple distinct domains:
  managing solution write contexts, column CRUD operations (including Web API fallbacks),
  table creation, and dependency checking.
- **Recommended Action:** Refactor this service into smaller, specialized services (e.g.,
  `ITableService`, `IColumnService`, and `ISolutionContextService`).

## 2. Performance Risk: Full Environment Metadata Fetching

- **Finding:** `GetTableIndexAsync` retrieves all entities in the environment at once.
- **Location:** `Services/DataverseQueryService.cs`
- **Why:** In large Dataverse organizations, fetching every entity metadata object simultaneously
  will lead to significant latency, high memory usage, and potential timeouts.
- **Recommended Action:** Implement a more targeted approach for fetching entity metadata,
  potentially using caching or lazy loading of metadata for specific entities only when needed.

## 3. Code Duplication: JSON Parsing Helpers

- **Finding:** Duplicate logic for case-insensitive JSON node traversal.
- **Location:** `Services/WebApiClient.cs`
- **Why:** Methods like `GetNode`, `GetObject`, and `GetString` are implemented locally to safely
  navigate `JsonObject` nodes. This logic is highly likely to be required in other services
  interacting with the Web API.
- **Recommended Action:** Move these helper methods to a centralized utility class, such as
  `MetadataUtilities.cs`.

## 4. Model Redundancy: `DataverseField` vs `DataverseColumn`

- **Finding:** Potential unnecessary abstraction in the data model.
- **Location:** `Models/DataverseField.cs` and `Models/DataverseColumn.cs`
- **Why:** `DataverseField` is a very limited version of `DataverseColumn`. Its current usage
  appears mostly restricted to lightweight display in `EntityDetailsView`.
- **Recommended Action:** Evaluate whether `DataverseField` can be eliminated in favor of
  `DataverseColumn` or a shared interface/base class to simplify the domain model.

## Uncertainties and Risks

- Refactoring `DataverseSchemaService` carries a moderate risk of introducing regressions in
  complex update workflows (like requirement level updates). Thorough unit testing of the new
  services will be required.
- Implementing targeted metadata fetching (finding #2) may require changes to the TUI's
  navigation flow to accommodate asynchronous loading states.
