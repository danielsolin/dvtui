# Code Cleanup Review

Date: 2026-09-20
Scope: read-only review of `dvtui` (app + tests) for structure, cleanliness, and
maintainability. No feature behavior, performance, or architecture redesign.

## 1. Near-duplicate master-detail screens

**Finding:** `SolutionSelectionScreen` and `SolutionBrowserScreen` implement the
same master-detail pattern with nearly identical internal code: pending load
handling via `Progress.Show`, two-pane layout with focus toggling, page-based
list rendering, shared key handling (arrows, PgUp/PgDn, Home/End, Tab, R
reload, Esc/Q), `GetPageSize()`, selection-restore-on-reload, error pane
rendering, and minimum-size guards.

- `Views/SolutionSelectionScreen.cs` (401 lines)
- `Views/SolutionBrowserScreen.cs` (662 lines)

**Why:** Two copies of the same navigation/paging/refresh loop will drift;
every change to list interaction must be made twice. This is the largest block
of near-duplicate code in the app.

**Recommended action:** Extract the shared master-detail behavior (list paging,
selection, pane focus, key handling, refresh loop, minimum-size guard) into one
shared screen component that each screen feeds with a row source, row label,
and detail renderer. Keep the details-pane async loading in the browser screen
only, since that is where it is genuinely different.

**Uncertainty/risk:** Medium. These are interactive screens; the shared part
must be validated with the PTY tests (`tests/run_tui_tests.py`) because the
details loading differs (synchronous rows vs deferred `EntityDetails` load).

## 2. Repeated small view helpers

**Finding:** Several identical helpers are copy-pasted across view files:

- `AddRow(Table, label, value)` in `Views/ComponentDetailsView.cs:57`,
  `Views/EntityDetailsView.cs:136`, `Views/SolutionSelectionScreen.cs:347`
- `FormatBoolean(bool?)` in `Views/EntityDetailsView.cs:144` and
  `Views/SolutionSelectionScreen.cs:355`
- The "Enlarge the terminal (60 x 10)..." message plus the size guard appears
  in five screens (see finding 9).

**Why:** Same code in three places; display formatting (e.g. the "—" for empty
values) is duplicated.

**Recommended action:** Move `AddRow` and `FormatBoolean` into the existing
`Views/FormViewHelpers.cs` and delete the local copies.

**Uncertainty/risk:** Low. Pure presentation code; the PTY tests cover the
affected screens.

## 3. Unused production API: `GetEntityByLogicalNameAsync`

**Finding:** `IDataverseQueryService.GetEntityByLogicalNameAsync`
(`Services/IDataverseQueryService.cs:22`) is implemented in
`Services/DataverseQueryService.cs:159` but has no production caller. Its only
caller is `tests/dvtui.TerminalTests/LiveTest.cs:487`.

**Why:** Dead code in the shipped application surface.

**Recommended action:** Delete the interface member and implementation, and
update `LiveTest.cs:487` to retrieve by logical name through the remaining
mechanism (or delete that fallback branch if the by-ID retrieve is sufficient
for the live cleanup path).

**Uncertainty/risk:** Low. LiveTest is an opt-in live test; the fallback
exists for an edge case where the just-created table is not yet visible by
metadata ID. Confirm with the live test if it is run in CI.

## 4. Unused production API: `DeleteTableAsync`

**Finding:** `IDataverseSchemaService.DeleteTableAsync`
(`Services/IDataverseSchemaService.cs:50`) is implemented in
`Services/DataverseSchemaService.cs:1053` but no UI flow deletes tables. The
only caller is `tests/dvtui.TerminalTests/LiveTest.cs:504`.

**Why:** Dead feature in the production surface; it also skips the write
prechecks every other write in this service performs.

**Recommended action:** Delete the interface member and implementation, and
move the live-test table deletion to a direct executor call inside the test
project (the test already owns a service stack it can wire a raw request
through).

**Uncertainty/risk:** Medium-low. LiveTest cleanup relies on this; the test
must be adjusted first. If table deletion is planned as a future UI feature,
keep it and note that intentionally.

## 5. Duplicated "observe faulted task" code

**Finding:** `ScreenRunner.ObserveLateTask` (`Views/ScreenRunner.cs:288`) and
`StartupScreen.ObserveFaults` (`Views/StartupScreen.cs:212`) are the same
six-line `ContinueWith` block.

**Why:** Exact duplicate of a subtle helper; should exist once.

**Recommended action:** Delete `StartupScreen.ObserveFaults` and call
`ScreenRunner.ObserveLateTask` from `StartupScreen.StopConnection`
(`Views/StartupScreen.cs:195`).

**Uncertainty/risk:** None.

## 6. Validation rules duplicated between UI and service layers

**Finding:** The same validation rules exist in two layers with two message
styles:

- UI: `CreateTableScreen.Validate()` (`Views/CreateTableScreen.cs`),
  `ColumnEditorValidator.ValidateCreate` (`Views/ColumnEditorValidator.cs`)
- Service: `SchemaValidation.ValidateTableCreation` /
  `ValidateColumnCreation` / `ValidateColumnUpdate`
  (`Services/SchemaValidation.cs`)

Both enforce the same display-name length, schema-suffix rules, description
length, text length, numeric bounds, whole-number check, and precision 0-10.
Limits are shared via `Models/ColumnDefaults.cs`, but the rules themselves
(parse, range check, min<=max, truncation check) are written twice.

**Why:** The two copies can drift; a change to an allowed range or rule has to
be made in two places with different wording.

**Recommended action:** Keep the UI pre-validation (it gives immediate
feedback) but make it share the rule logic with `SchemaValidation` so each rule
lives once. If a full merge is not justified, at minimum move the parse+range
checks for length/precision/numeric values into one shared validator used by
both layers.

**Uncertainty/risk:** Medium. Message wording differs on purpose (UI vs
service); a shared rules core with layer-specific messages is the
sensible shape. Needs the service tests in
`tests/dvtui.TerminalTests/ServiceTests.cs` to stay green.

## 7. Redundant indirection: `ComponentDetailsView.CreateOverview`

**Finding:** `Views/ComponentDetailsView.cs:50` -
`CreateOverview(component)` does nothing but call
`CreateMembership(component)`.

**Why:** Unnecessary indirection.

**Recommended action:** Delete `CreateOverview` and call `CreateMembership`
directly from `Create`.

**Uncertainty/risk:** None.

## 8. Dead property: `MsalTokenProvider.EnvironmentUrl`

**Finding:** `Services/MsalTokenProvider.cs:30,48` - `EnvironmentUrl` is
assigned in the constructor and never read anywhere.

**Why:** Dead code.

**Recommended action:** Delete the property and drop the unused constructor
parameter (keep `cachePath`).

**Uncertainty/risk:** None.

## 9. Hardcoded terminal minimums bypassing `TuiConstants`

**Finding:** `TuiConstants.MinimumWidth` / `MinimumHeight` exist, but only two
screens use them. `ColumnEditorScreen.cs:268`, `CreateTableScreen.cs:236`, and
`TableColumnsScreen.cs:234` hardcode `width < 60 || height < 10`, and the
"Enlarge the terminal (60 x 10)..." text is written out in five screens
(`SolutionSelectionScreen.cs:270`, `SolutionBrowserScreen.cs:527`,
`CreateTableScreen.cs:238`, `TableColumnsScreen.cs:237`,
`ColumnEditorScreen.cs:270`).

**Why:** Magic numbers duplicated next to the constants that already exist for
them.

**Recommended action:** Use `TuiConstants.MinimumWidth` / `MinimumHeight` in
the three screens, and build the hint text in one place (e.g. a helper on
`TuiLayout` or `FormViewHelpers`) that interpolates the constants, so the text
and the check cannot disagree.

**Uncertainty/risk:** None.

## 10. `DataverseSchemaService` is too large for one class

**Finding:** `Services/DataverseSchemaService.cs` is 1574 lines and mixes:
write-context loading, table create/delete, column create/update/delete,
dependency queries, scope verification, and post-write readback verification.
`UpdateColumnAsync` (line 530 to ~750) alone does capability checks,
per-field change detection, the write, the Web API fallback, and readback
verification.

**Why:** One class owns too many distinct responsibilities; the update path in
particular is hard to follow.

**Recommended action:** Split the post-write verification helpers
(`VerifyCreatedTableAsync`, `VerifyCreatedColumnAsync`,
`VerifyUpdatedColumnAsync`, `VerifyColumnDeletedAsync`,
`LoadAttributeMetadataByIdAsync`, `Ensure*NameAvailableAsync`) into a separate
`SchemaWriteVerification` (or similar) class, and move the update change
detection block out of `UpdateColumnAsync` into a small helper. This is the
lowest-risk split; keep orchestration in the service.

**Uncertainty/risk:** Medium. This is the most behavior-sensitive code in the
app; `ServiceTests.cs` covers it well, but the split should happen after the
findings below that touch the same files.

## 11. Duplicated entity identity checks between services

**Finding:** `DataverseQueryService.ValidateEntityIdentityInput` (line 275) and
`VerifyEntityIdentity` (line 469) duplicate the argument validation and the
post-retrieve identity check that `DataverseSchemaService.LoadEntityMetadataAsync`
(performs in its body). Both also build identical `RetrieveEntityRequest`s.

**Why:** The same identity rules (non-empty logical name, non-empty metadata
ID, match after retrieve, `ColumnConflictException` on mismatch) are written
twice.

**Recommended action:** Fold both services onto one shared helper for the
identity validation/verification (it can live next to
`MetadataUtilities` or become part of finding 10's split).

**Uncertainty/risk:** Low-medium. Behavior stays identical; tests exist in
`ServiceTests.cs` for the conflict paths.

## 12. Defensive type check in `WebApiClient`

**Finding:** `Services/WebApiClient.cs:31` -
`_client is not IDataverseWebExecutor` skips the fallback with a log message
referring to a "test executor". In production the executor always implements
`IDataverseWebExecutor`; only the test fakes do not.

**Why:** The app layer quietly degrades behavior based on test doubles.

**Recommended action:** Either make `UpdateRequirementLevelAsync` take the web
executor as a required dependency (constructor injection of
`IDataverseWebExecutor`) or fail clearly when it is missing, instead of
silently returning. The test fakes should implement the interface or the
fallback should be made optional at the test level.

**Uncertainty/risk:** Low. `ServiceTests.cs:933`
(`TestUpdateRequirementUsesWebApiFallback`) exercises this path and would need
a matching update.

## 13. Minor: `GetTokenCachePath` side effect used for logging

**Finding:** `Services/DataverseConnectionManager.cs:17,31` -
`GetTokenCachePath` creates the cache directory as a side effect and is called
twice in the constructor: once to pass the path to `MsalTokenProvider`, once
purely to log the path.

**Why:** An impure helper called for its return value in a log line; the
directory is created twice.

**Recommended action:** Compute the path once into a local variable and reuse
it for both the provider and the log line.

**Uncertainty/risk:** None.

## Summary (suggested order)

1. Zero-risk deletions first: findings 5, 7, 8, 9, 13.
2. Shared view helpers: findings 1, 2 (validated by PTY tests).
3. Unused API removal: findings 3, 4 (adjust `LiveTest.cs` first).
4. Validation dedup: finding 6 (service tests must stay green).
5. Larger split: findings 10, 11, 12 last, after the above land.
