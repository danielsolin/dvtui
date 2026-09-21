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

**Why:** This widens the shipped service interface for an opt-in live-test cleanup
fallback. The fallback handles the case where retrieval by metadata ID is temporarily
unavailable after table creation.

**Recommended action:** Keep this method for now. If it is removed later, first
introduce a test-owned retrieval path and verify the live cleanup behavior before
deleting the fallback.

**Uncertainty/risk:** Medium. LiveTest is an opt-in live test, and the fallback
exists for an edge case where the just-created table is not yet visible by
metadata ID.

## 4. Unused production API: `DeleteTableAsync`

**Finding:** `IDataverseSchemaService.DeleteTableAsync`
(`Services/IDataverseSchemaService.cs:50`) is implemented in
`Services/DataverseSchemaService.cs:1053` but no UI flow deletes tables. The
only caller is `tests/dvtui.TerminalTests/LiveTest.cs:504`.

**Why:** This is a test-only service surface with an unsafe signature: it accepts
only a logical name and skips the write prechecks used by the other write methods.

**Recommended action:** Keep it for now so the live test retains one cleanup path.
Do not move a raw delete request into the test project just to remove this method.
If it is redesigned, require table identity and write context, then preserve
post-delete verification.

**Uncertainty/risk:** Medium. LiveTest cleanup relies on this, and the current
signature makes accidental deletion easier than the other write operations.

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

**Finding:** `Services/MsalTokenProvider.cs:30,48` - the `EnvironmentUrl`
property is assigned in the constructor and never read anywhere. The constructor
parameter itself is still used to build the OAuth scope at line 32.

**Why:** Dead code.

**Recommended action:** Delete the property and its assignment. Keep the
`environmentUrl` constructor parameter because it is required for `_scopes`.

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
`_client is not IDataverseWebExecutor` makes the fallback a no-op and logs a
message referring to a "test executor". The current `FakeExecutor` already
implements `IDataverseWebExecutor`; the condition still matters for any custom
executor that supports SDK requests but not Web API requests.

**Why:** The fallback contract is implicit, and a non-Web-API executor can cause
the requirement update to be skipped without an error.

**Recommended action:** If the fallback is required, inject
`IDataverseWebExecutor` and fail clearly when it is unavailable. If it is
intentionally optional, keep the guard but document and test the no-op behavior.

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

## Follow-up (2026-09-21)

The current cleanup pass implemented findings 2, 5, 7, 8, 9, and 13. Findings
1, 3, 4, 6, 10, 11, and 12 remain deferred because they need broader design or
live-test decisions.
