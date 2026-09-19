# Step 2: Create tables and manage custom columns

## 1. Your task

You are implementing step 2 of dvtui. Follow this plan in order, verify the resulting behavior,
and report what was tested. Build on the working solution selection and browser from step 1.

Implement these operations in the terminal UI:

1. Create a standard custom Dataverse table in the selected unmanaged solution.
2. Create supported custom columns on a table in that solution.
3. Edit a defined subset of properties on supported custom columns.
4. Delete eligible custom columns after checking dependencies and confirming the target.
5. Explicitly publish the selected table's pending customizations.

The public UI must not offer table deletion. Controlled cleanup of a table created by a live test
is a separate test-only operation described below.

Read the current `AGENTS.md` and code first. Keep artifacts and UI text in English, use lowercase
`dvtui`, and limit every line in added or changed files to 100 characters. Follow the repository's
argument layout and method chaining style. Preserve unrelated and already staged changes.
Do not upgrade packages, redesign authentication, commit, or push as part of this task.

The scope choices below are intentional. Do not silently replace them with a generic metadata
editor or implement additional administration features.

## 2. Scope and behavior decisions

### Supported operations

| Operation | Scope |
| --- | --- |
| Create table | Standard custom table, organization-owned or user/team-owned. |
| Create column | Plain text, multiline text, whole number, decimal, or yes/no. |
| Edit column | Display name, description, requirement level, and text-length increases. |
| Delete column | Eligible unmanaged custom columns of the five supported kinds. |
| Publish table | Explicit publication of the selected table, including pending changes. |

Support new columns on existing standard or custom tables when their metadata permits it. A table
does not have to be custom to allow custom columns. A managed table may also allow new unmanaged
columns; do not confuse the table's management state with the column's management state.

For step 2, require the selected solution to be explicitly unmanaged. Keep managed solutions fully
browsable, with write actions disabled and a readable explanation. This is a dvtui scope policy,
not a claim that Dataverse forbids all customization of managed components.

For editing and deleting existing columns, require an unmanaged custom column of a supported,
ordinary kind. Keep system, managed, primary ID, primary name, derived, logical, calculated,
rollup, formula, and autonumber columns read-only in this step. Preserve browsing of other types.

### Out of scope

- Date/time, money, floating-point, choices, lookups, relationships, files, and images.
- Calculated/formula/rollup columns and conversions between column kinds.
- Changing logical/schema names, existing numeric bounds/precision, or existing boolean options.
- Reducing a text column's maximum length.
- Editing or deleting tables, including ownership changes after creation.
- Record data editing, data migration, bulk writes, or undo/rollback promises.
- Automatically removing dependencies, solution membership, forms, or views to enable deletion.
- Import/export, solution creation, publisher creation, or changes to the preferred solution.
- Adding every table subcomponent to a solution as a shortcut.
- Publishing all customizations in the environment.
- Changes to permissions, authentication, or security roles.

## 3. Current implementation and the lesson from step 1

Recheck these observations before editing:

| File | Relevant current behavior |
| --- | --- |
| `Program.cs` | Owns one service and loops between solution selection and browsing. |
| `Services/DataverseService.cs` | Reads solutions, membership, table summaries, and details. |
| `Views/SolutionBrowserScreen.cs` | Shows components and loads table details asynchronously. |
| `Views/EntityDetailsView.cs` | Renders columns as a table inside scrollable detail content. |
| `Models/DataverseField.cs` | Has labels and a type string, but no stable column identity. |
| `Models/DataverseSolution.cs` | Has solution identity but no publisher context. |
| `tests/dvtui.TerminalTests/Program.cs` | Supplies fake solution and browser data. |
| `tests/run_tui_tests.py` | Drives a prebuilt test host through a PTY. |

The current detail method is:

```text
GetEntityAsync(string logicalName, Guid metadataId, CancellationToken)
```

It sets both `LogicalName` and `MetadataId` on `RetrieveEntityRequest`, with
`RetrieveAsIfPublished = true`. Table summary enrichment also has a metadata-ID fallback.

The user reports that step 1 required live requests to resolve metadata lookup behavior. Preserve
the working path unless new evidence demonstrates a necessary change. Do not revert it to an
ID-only lookup just because the original plan or a documentation example used that form.

Keep table logical names, table metadata IDs, column metadata IDs, and solution component row IDs
distinct. There is no reason to assume a request accepts an arbitrary parameter named `Id`.
Inspect installed SDK types and actual request parameters. Verify returned identity before using
a metadata result to construct a write.

Current test documentation and the runner differ: the runner expects its binary to exist and does
not build it automatically. Build the test host explicitly during baseline verification. Do not
treat the old documentation or an existing binary as proof of the current test behavior.

## 4. Start with API verification, before building all forms

### 4.1 Offline preparation

Inspect the installed SDK and official references in section 13. Compile small request-building
checks in the existing test host or a narrowly scoped test helper. Do not invent API properties.

Verify these request families:

- `CreateEntityRequest` and its returned table and primary-column IDs.
- `CreateAttributeRequest`, `RetrieveAttributeRequest`, and `UpdateAttributeRequest`.
- `DeleteAttributeRequest` and `RetrieveDependenciesForDeleteRequest`.
- `PublishXmlRequest` for one table.
- Solution, publisher, and organization reads needed for prefix and label language.

Use typed SDK messages and the existing `ServiceClient`. Do not add a second HTTP client or
reimplement the Dataverse protocol. See [create-table], [columns], and [solution-sdk].

### 4.2 Controlled live probe

If the user has already authorized a disposable test environment and target unmanaged solution,
use that authorization. Otherwise obtain the missing exact environment and solution before live
writes. Do not infer a test target from the production login default or a URL in an old document.
Continue offline implementation and tests while live access is unavailable.

Create an explicit, opt-in live test mode, separate from the ordinary terminal test command. It
must use the same service operations as production, except for test-only table cleanup. Never run
it automatically on build, app startup, or normal test execution.

Probe one complete lifecycle first:

1. Read and record environment identity, solution ID, unique name, publisher prefix, and language.
2. Choose a unique schema suffix and verify that the target table does not already exist.
3. Create one temporary custom table with a unique run marker in its description.
4. Read it back and verify the response ID, logical name, primary column, and solution context.
5. Create a text column, read it back, edit it, and read the unpublished metadata again.
6. Publish only this temporary table and verify the edited published values.
7. Check deletion dependencies, delete the test column, and verify its absence.
8. Clean up the temporary table using its exact recorded identity and verify its absence.

Repeat the column lifecycle for the other supported types during final validation. Exercise both
ownership choices if live access permits. Do not create dependencies or record data merely to make
negative tests possible; those cases can use controlled test doubles.

Record request class, identifying parameters, SDK version, returned IDs, relevant fault codes,
publication state, and cleanup outcome. Do not record tokens, credentials, or unrelated data.
Capture the successful request shape in regression tests so the next agent does not rediscover it.

### 4.3 Cleanup rules

Keep a run-specific ledger of exact created objects, response IDs, schema names, and run marker.
Write it incrementally to a temporary report so an interrupted test still has a cleanup record.
Never delete pre-existing objects or use a broad name-prefix query as a deletion target.

Attempt cleanup in `finally`, using a fresh bounded cancellation token rather than an already
canceled test token. Before deletion, re-read the object and verify identity against the ledger.
If creation timed out without a response ID, reconcile by the unique name and marker before
claiming ownership or attempting cleanup. If ownership remains uncertain, leave it and report it.

A cleanup failure must report the environment, exact remaining IDs/names, and failure reason.
Do not delete an entire solution, remove unrelated dependencies, or report clean success while
temporary objects remain. The test-only table deletion must not become an application command.

## 5. Solution context, identities, and capabilities

### 5.1 Write context

Load a write context for the selected solution containing:

- Environment identity and URL for display and result reporting.
- Solution ID, unique name, and explicit managed/unmanaged state.
- Publisher ID and its `customizationprefix`.
- Organization base language code for label creation and editing.

Read the selected solution's `publisherid`, then the publisher record. Do not infer the prefix from
the solution name or from a table's current prefix. Read the environment's base language; do not
hard-code English LCID 1033 because the UI is English. See [publisher] and [organization].

For this step, edit labels in the base language only and show that language in the form. Preserve
all other translations. The current `GetLabel` fallback is suitable for browsing, but must not be
used to copy a user's localized label into a different language entry.

Pass `SolutionUniqueName` for supported create/update requests. Use the solution's unique name,
not its friendly name or GUID string. Verify the selected solution still exists and is unmanaged
before a write. Missing context disables writing; it must not select a default solution silently.

### 5.2 Table and column identity

Capture the selected solution and table identity when opening an editor. Confirm the table is
still part of the selected solution before dispatch. Do not let changing the browser selection
retarget an open form or a pending operation.

Columns require at least table logical name, column logical name, and metadata ID. Use names in
requests that require names, and metadata IDs where the SDK requires metadata IDs. Before edit or
delete, read fresh unpublished column metadata and compare its ID and parent table with the
captured target. If a column was deleted and recreated under the same name, require a new review.

The environment column list is not a solution membership list. Saving a custom column through an
unmanaged solution may associate that column with the solution. Explain this in the save summary.
The change affects the shared environment, not a private copy of the table inside that solution.
Use `SolutionUniqueName` rather than a blanket add-all-components operation. See [solution-sdk].

### 5.3 Capability checks

Check capabilities in the service as well as the UI. Disabled buttons alone are insufficient.
Use typed metadata and documented managed properties, including the following where applicable:

- Table `IsCustomizable` and `CanCreateAttributes` for creating columns.
- Column `IsCustomAttribute`, `IsManaged`, `IsPrimaryId`, and `IsPrimaryName`.
- Column `AttributeOf`, `IsLogical`, `SourceType`, and `AutoNumberFormat` for special kinds.
- Column `IsCustomizable`, `IsRenameable`, and `CanModifyAdditionalSettings`.
- `RequiredLevel.Value` and its changeability metadata.

Verify actual property types and meanings against [attribute-metadata] and [entity-metadata].
Preserve unknown values and fetch fresh metadata when required; unknown does not mean permitted.
Do not invent an attribute-level `CanBeDeleted` property or use `IsValidForUpdate` as permission
to edit the schema: the latter concerns values on data records.

Return a reason per disabled action/property. Examples include `Managed solution: read-only`,
`Primary column: editing is not supported in step 2`, and `This column type is read-only`.
Do not hide unsupported columns from browsing.

Metadata capabilities do not prove the caller has all required privileges. Let the server enforce
privileges and show its failure clearly. Do not require a particular role name or create a new
privilege-management subsystem. Recheck the relevant metadata immediately before dispatch.

## 6. Forms and request semantics

### 6.1 Create table

Collect these inputs:

- Display name and plural display name.
- Schema suffix, with the publisher prefix shown separately and applied exactly once.
- Optional description.
- Ownership: organization or user/team, mapped to the documented SDK enum.
- Primary name column display name, schema suffix, and maximum text length.

Show the final schema name, predicted logical name, primary name column, solution, and environment
before submission. Let the user correct the suffix before saving. Validate documented naming and
length rules, and check for a collision without treating that check as a lock against other users.

Create a standard table with `CreateEntityRequest` and a `StringAttributeMetadata` primary name
column. Do not enable activities, notes, virtual/elastic behavior, or other capabilities implicitly.
Use a plain-text primary name column; a centralized default length of 100 is sufficient.
Do not confuse the primary name column with the platform-generated primary ID column.

Read back the result and refresh solution contents. Select the created table when possible.
If creation succeeded but refresh failed, say `Table created; refresh failed` and retain the
returned identity. Do not offer to create it again as though nothing happened.

### 6.2 Create column

Common inputs are display name, schema suffix, description, type, and requirement level. Use the
selected solution's publisher prefix, even when the parent table has a different prefix.

| UI type | SDK metadata | Type-specific creation settings |
| --- | --- | --- |
| Text | `StringAttributeMetadata` | Plain text; maximum length. |
| Multiline text | `MemoAttributeMetadata` | Plain text; maximum length. |
| Whole number | `IntegerAttributeMetadata` | Ordinary number; minimum and maximum. |
| Decimal | `DecimalAttributeMetadata` | Minimum, maximum, and decimal places. |
| Yes/No | `BooleanAttributeMetadata` | Default value; base-language Yes/No labels. |

Do not expose specialized formats such as duration, time zone, rich text, or autonumber. Use one
central location for default lengths, numeric bounds, precision, and other creation defaults.
Choose conservative defaults and verify their limits against the installed SDK and live probe.
Suggested application defaults are text length 100, multiline length 2000, and decimal precision 2.

Parse numeric inputs consistently and document the accepted decimal separator in the form. Reject
invalid values locally without discarding input. Check minimum <= maximum, integer bounds, text
length limits, and supported decimal precision. Do not round invalid input silently.

Offer Optional, Business recommended, and Business required, mapped to the corresponding SDK
requirement levels. Do not offer System required. Business required is application-level behavior,
not a new database NOT NULL guarantee. See [column-properties].

Create through `CreateAttributeRequest` with `SolutionUniqueName`. Record the returned column
metadata ID, verify the column, and refresh the column list and solution inventory as needed.
Do not add every column on the table to the solution to make the new column visible.

### 6.3 Edit column

Load a fresh, type-specific unpublished definition when opening the editor. Keep its identity,
current values, base-language labels, and relevant capabilities as the editing snapshot.

| Property | Step 2 policy |
| --- | --- |
| Display name | Editable when the column's rename capability permits it. |
| Description | Editable when applicable customization settings permit it. |
| Requirement level | Editable only when metadata permits the change. |
| Text/multiline maximum length | May increase; may never decrease through this UI. |
| Schema/logical name, type | Always read-only. |
| Numeric bounds/precision, boolean options/default | Read-only after creation. |

Do not enable edits for a specialized column just because its underlying type is text or decimal.
No-change Save should close or report `No changes`; it must not issue a write or add membership.

Before saving, re-read the definition. Compare editable properties and capabilities with the
snapshot. If they changed elsewhere, show a conflict and require reload/review. This narrows the
overwrite risk but is not an atomic concurrency guarantee; do not claim metadata ETag support
without verifying that the chosen SDK operation supports it.

Build the update from fresh metadata of the correct subtype, changing only the allowed values.
Preserve unrelated settings, numeric properties, boolean labels, and other language labels.
Use `MergeLabels = true` and verify both replacement and clearing of the base-language description.
An intentionally cleared description differs from an omitted property. See [update-column].

Send `UpdateAttributeRequest` with the exact table logical name and solution unique name.
Read back unpublished metadata to verify the requested changes. Show that publishing is a
separate operation; do not publish automatically as part of Save.

### 6.4 Delete column

Only offer deletion for the ordinary, supported custom columns described in section 2. Re-read
the target and verify identity and eligibility before displaying the final confirmation.

Call `RetrieveDependenciesForDeleteRequest` with component type Attribute and the column metadata
ID. Do not use the solution component row ID. If dependencies exist, show the blocking components
with available names or type/ID fallbacks and do not dispatch deletion. If the check fails, do not
treat that as an empty dependency list. See [solution-sdk] and [delete-column].

The confirmation shows environment, solution, table logical name, and column logical name, plus:

```text
This deletes the column and its stored data from the environment.
It does not only remove the column from this solution.
```

Require typing the full column logical name and activating Delete. Default to Cancel. Avoid a
single unconfirmed Delete-key action. Recheck dependencies immediately before dispatch if time
has passed since the initial check; the server remains authoritative if a dependency changes.

Execute `DeleteAttributeRequest`, then verify absence through a fresh metadata read. Distinguish
an actual not-found result from permission, transport, and parsing failures. Do not regard a
generic caught exception as evidence that deletion succeeded.

A successful deletion is not undoable in dvtui. Never offer automatic dependency removal or
recreation as rollback. Refresh the column list and invalidate old selections and detail tasks.

### 6.5 Publish table

Provide a separate `Publish table` action with the selected table's logical name and environment.
Explain that it publishes pending changes for that table, potentially including other users'
changes. Do not label it `Publish my changes` or imply solution-only isolation.

Use `PublishXmlRequest` with the documented XML structure for the selected table. Build XML safely;
do not interpolate arbitrary UI strings into XML. Do not call `PublishAllXmlRequest`.

Create/delete operations may already publish their changes. Treat the save and publication states
separately and verify behavior in the live probe; do not label every write as an unpublished draft.
Updates require attention to publication. See [publishing] and [publish-all].

Track pending changes made in this session for useful UI feedback, without claiming to discover
every pending change in the environment. Clear a session pending indicator only after a successful
publish result; when verifying edits, also compare a published metadata read with expected values.
If publication fails, keep the saved edit and explain that publication failed. Do not undo it.

## 7. Write lifecycle and uncertain outcomes

Read cancellation from step 1 cannot be copied unchanged to write operations. Canceling a client
wait does not establish that Dataverse canceled the server operation.

Use explicit states in a small operation model or controller:

```text
Editing -> Validating -> Sending -> Verifying -> Completed
                |          |           |
                v          +-----------+----> Outcome unknown -> Recheck
          Validation error
```

Also distinguish a definite server rejection from a successful write followed by a failed
refresh. Preserve response IDs and fault details whenever available.

Required behavior:

- Allow one mutation at a time per connected app session.
- Disable repeat submission, reload, and solution switching while a mutation is being resolved.
- Before dispatch, canceling a form performs no write.
- After dispatch, Escape must not claim to undo or cancel the server change.
- Keep the UI responsive while waiting, with status updates and an explicit exit path.
- On exit during a write, explain that the result may be unknown and retain target information.
- Bound cleanup time and observe late task faults; never let a timed-out task alter a new screen.
- Do not add an automatic retry loop around schema writes.
- Understand existing SDK transport retries; do not promise exactly-once delivery.

For timeout, lost connection, or client cancellation after dispatch, perform bounded readback
with a fresh token. Reconcile creation by response ID when available and otherwise by the unique
schema/logical name and intended definition. Reconcile edits using requested property values and
the original metadata ID. Reconcile deletion using a verified absence of that exact target.

Metadata visibility can lag. An immediate missing result is not proof a timed-out create did not
happen. Keep the outcome unknown if bounded checks cannot resolve it. Offer Recheck; do not silently
resend. A same-name object with different identity or properties is a conflict, not success.

At minimum preserve the operation summary until resolved or until the user explicitly exits.
Print unresolved target information after leaving the alternate screen, so it is not lost with
the TUI. A full persistent operation journal is outside this step; the live test ledger is separate.

## 8. Code boundaries and testability

Keep presentation, metadata access, and plain application models separate. Do not turn
`SolutionBrowserScreen` into a large SDK request builder.

### Models

- Extend `DataverseField` or add a column definition model with stable identity, typed kind,
  supported property values, base-language labels, and the capabilities needed by the editor.
- Add a solution write-context model for publisher and language information.
- Use small request models for table creation, column creation, and column editing.
- Add operation outcome and dependency summary models only where they are used.
- Preserve nullable metadata values; do not coerce absent flags into permission to write.

Use a typed column-kind enum for behavior. Keep strings for display, not business decisions.
Do not expose SDK metadata instances or Spectre renderables as UI editing state.

### Services

Keep `DataverseService` as the connection owner and existing browser-facing entry point. Put the
new schema operations in `Services/DataverseSchemaService.cs`, sharing that same connected SDK
client. Add a small capability/validation helper if it makes policy independently testable.

Use a narrow SDK execution seam for service tests, such as the documented async service interface
or injected async request delegates. Verify the installed signatures before choosing it. Keep
client ownership and disposal unambiguous; do not connect once per form or create another client
just to perform writes. The test seam must not require real authentication.

Suggested operations, with cancellation and explicit context/identity parameters:

```text
LoadWriteContextAsync
LoadColumnDefinitionAsync
CreateTableAsync
CreateColumnAsync
UpdateColumnAsync
GetColumnDeleteDependenciesAsync
DeleteColumnAsync
PublishTableAsync
```

The exact signatures should follow existing async conventions. Each write validates scope and
fresh capabilities internally, constructs the request, and returns enough information for
readback and outcome handling. Do not let the UI directly call an unrestricted Execute delegate.

Keep request construction and reconciliation testable. Do not add a generic repository, automatic
retry framework, command bus, or universal metadata-form generator.

### Views

Add a dedicated table column browser with selectable rows. The existing rendered column table
inside `EntityDetailsView` is insufficient for safely targeting a column action.

Suggested focused files:

- `Views/TableColumnsScreen.cs`
- `Views/CreateTableScreen.cs`
- `Views/ColumnEditorScreen.cs`, shared by create/edit where practical.
- A small reusable confirmation renderer if both delete and publish need it.

Keep plain text/numeric editing reusable only where the forms actually need it. Do not repurpose
the URL-specific input control into an entire form framework.

### Program and navigation

Wire schema capabilities and services through the existing solution loop. Preserve required
solution selection, solution switching, and one connection per session. Do not change URL/login
behavior. Preserve selected component/column identity when returning from forms or refreshing.

## 9. TUI interaction contract

### Solution browser

- `N`: create table, when the selected solution is unmanaged and write context is available.
- `Enter` on a resolved table: open its column browser.
- Keep current scrolling, Tab, reload, Esc to solutions, and Q/Ctrl-C behavior when idle.
- Managed solutions and unresolved components remain browsable with disabled-action explanations.

### Column browser

Show environment and solution context, table name, and a scrollable column list. Include logical
name, display name, type, requirement level, and read-only/action availability. Details show the
selected column's full identity and relevant settings. Preserve the environment-column distinction
from step 1; this is not a list of columns exclusively owned by one solution.

| Key | Action when available |
| --- | --- |
| Up/Down, PageUp/PageDown, Home/End | Navigate rows. |
| Tab | Switch between the list and detail pane. |
| N | New column. |
| E | Edit selected column. |
| D | Begin dependency check and deletion confirmation. |
| P | Begin publish-table confirmation. |
| R | Reload metadata when no mutation is active. |
| Esc | Return to the solution browser. |
| Q / Ctrl-C | Exit through the appropriate idle/in-progress handling. |

### Forms

Use Tab/Shift-Tab to move focus and standard editing keys within inputs. Use an explicit Save or
Create control, plus Ctrl-S as an optional shortcut; Enter must not submit an incomplete form by
accident. Show validation beside the relevant input and retain draft values after rejection.

Q, R, N, D, and P are ordinary characters while a text input has focus. Do not let global shortcuts
consume typed names or descriptions. Escape cancels an unsent form; confirm discarding changes
when appropriate. Keep immutable fields visible but non-editable.

Display a concise final summary before a create/update is sent, showing the actual target and
changed values. Deletion uses the stronger typed-name confirmation in section 6.4. Publishing has
its own explicit confirmation because it may include other pending changes on the table.

Use one active console input/render loop at a time. Do not start competing Live displays or nested
alternate-screen handlers that lose the parent state. Handle small terminals with scrolling or a
clear resize message, escape remote text, and restore cursor and terminal modes on every exit.

## 10. Implementation checkpoints

Complete one checkpoint before moving to the next. Run focused verification after each code change;
do not postpone all validation until every screen has been built.

### A. Baseline and request verification

1. Inspect the current worktree and preserve staged step-1 changes.
2. Build the app and test host, then run the existing terminal suite.
3. Record pre-existing failures without relabeling them as new regressions.
4. Verify SDK symbols, identity handling, and request construction offline.
5. Run the initial controlled live lifecycle when its target is authorized and available.

### B. Context, metadata, and policies

1. Load publisher/base-language context for the selected unmanaged solution.
2. Extend column identity and type-specific metadata mapping.
3. Implement capability decisions and validation with focused C# tests.
4. Preserve existing read-only behavior for managed and unsupported components.

### C. Create table end to end

1. Add request construction, response identity capture, and readback.
2. Add the create-table form and solution browser action.
3. Verify invalid input, duplicates, denied access, uncertain outcome, and successful refresh.

### D. Column browser and create column

1. Add selectable column rows and safe identity-based navigation.
2. Implement the five supported creation types and their validation.
3. Add deterministic request tests and PTY form/navigation tests.
4. Verify that a new column is associated with the selected solution without broad inclusion.

### E. Edit and publication

1. Add snapshots, fresh pre-save reads, conflict handling, and selective property updates.
2. Preserve translations and settings not edited by the user.
3. Implement text-length increases and reject decreases.
4. Add separate publication with accurate saved/published/unknown outcomes.

### F. Delete column

1. Implement eligibility checks and dependency inspection.
2. Add typed-name confirmation and one-shot dispatch.
3. Verify deletion by metadata absence, distinguishing lookup errors from not-found results.
4. Cover failed checks, dependencies, changed identity, timeout, and successful deletion.

### G. Final verification and handoff

1. Run the automated checks and the authorized live lifecycle for all supported kinds.
2. Verify cleanup and report any remaining test objects by exact identity.
3. Update `tests/README.md` with commands, coverage, and opt-in live test prerequisites.
4. Review the diff for scope, English text, line lengths, and mutation paths.
5. Report completed behavior and precise verification limits. Do not claim live success from mocks.

If live access is unavailable, finish offline work and mark live validation outstanding. Do not
invent observed results or block unrelated implementation on a missing test login.

## 11. Automated verification

Use C# tests against a fake SDK execution boundary for request and outcome semantics. The existing
terminal host may expose a non-interactive assertion mode that returns a nonzero exit code on
failure; a new test framework is not required. Keep business logic tests in C#, not Python copies.

The fake must record every outbound request so tests can prove that forbidden or canceled actions
sent no write. Simulate response success, definite rejection, lost response after commit, delayed
visibility, stale identity, and readback failure. Test real request construction, not just a second
implementation that returns the expected text.

### Service and policy cases

- Managed or unknown solution state blocks writes in the service.
- Publisher prefix and base language come from the selected context.
- Wrong solution/table/column IDs or a changed metadata ID stop dispatch.
- Supported types map to the correct metadata subtype and creation settings.
- Invalid names, duplicate targets, numeric bounds, precision, and lengths are rejected.
- Business required maps correctly; System required is never offered.
- Protected, specialized, system, managed, and unsupported columns remain read-only.
- Per-property managed flags control individual edits; data-update flags are not reused.
- Editing one base-language label preserves other translations and unrelated settings.
- Clearing a description works; a no-change edit sends no write.
- Text length can increase but cannot decrease; existing numeric settings remain intact.
- A concurrent change produces a conflict rather than silent overwrite.
- Dependencies or a failed dependency check prevent deletion.
- Delete uses the column metadata ID for dependency checks and proper names for deletion.
- Cancellation before submission sends nothing; cancellation after dispatch is reconciled.
- A lost successful response is not followed by a second create/update/delete request.
- Failed verification/refresh does not erase knowledge of a successful response.
- A permission or transport failure during lookup is not interpreted as absence.
- Publish targets one table and never issues PublishAllXml.
- Publication failure leaves a saved update intact and accurately reported.

### Terminal cases

- Login and explicit solution selection still precede every mutation route.
- Managed solutions show browsing plus clear disabled-action reasons.
- Column selection targets the correct logical name/ID after sorting and refresh.
- Typing shortcut letters into inputs does not trigger browser commands.
- Input validation preserves values; Cancel before submission performs no write.
- Create/edit summaries display the selected environment, solution, and target.
- Delete needs the exact logical name; Cancel remains the default.
- Dependency errors and unknown outcomes remain visible with appropriate next actions.
- Double submission does not send two operations.
- Save, readback, and publication success/failure appear as distinct outcomes.
- Small windows, resizing, long labels, bracket characters, focus, and scrolling work.
- Old async results cannot change a new editor or a different selected solution.
- Exit restores the terminal and reports unresolved operations outside the alternate screen.

Extend the PTY runner using condition-based output waits and explicit exit assertions. Do not
count force-killing a child as proof that the application quit correctly. Always stop each process
group and close its descriptors in `finally`, including when an assertion fails. Keep waits bounded
and avoid adding fixed sleeps as readiness checks. Repair only the harness behavior necessary to
make these tests reliable; do not spend this step on unrelated test cleanup.

## 12. Commands, acceptance, and reporting

Run from the repository root:

```sh
dotnet build --disable-build-servers
dotnet build tests/dvtui.TerminalTests/dvtui.TerminalTests.csproj --disable-build-servers
python3 tests/run_tui_tests.py
git diff --check
git status --short
```

Also run the new C# service assertion mode and document its exact command. Ordinary tests must
not need credentials or write to Dataverse. `dotnet test` alone does not run the current PTY host.

Check added/untracked files as well as tracked diffs for whitespace and the 100-character limit.
Review the staged baseline separately from changes made during this task; do not alter the user's
staging choices. Existing uncommitted work is not permission to reformat unrelated files.

The implementation is complete when:

- Users can create a table and manage the five supported custom column kinds through the TUI.
- Solution context, publisher prefix, label language, and stable identities are correct.
- Unsupported or disallowed operations are explained and rejected before dispatch.
- Edits preserve unrelated metadata and detect changed editing snapshots.
- Deletion checks dependencies and requires an explicit target confirmation.
- Publication is explicit, table-scoped, and distinct from saving.
- Ambiguous write outcomes are reconciled without blind retries or false failure/success claims.
- Automated tests pass and no application/test processes are left running.
- Authorized live verification has recorded results and cleanup, or is explicitly outstanding.

Your final handoff should state implemented features, tests and exact commands, live observations,
remaining limitations, and any test objects requiring cleanup. Explain any observed SDK behavior
that differs from the documentation, especially metadata identity and publication semantics.
Do not claim that code review or a simulated SDK test proves behavior in a live environment.

## 13. Official references

These references support SDK behavior. Scope restrictions and UI choices above are dvtui decisions.
Use live evidence to settle ambiguous behavior in the actual environment and installed SDK version.

- [CreateEntityRequest][create-table]
- [CreateAttributeRequest and supported column metadata][columns]
- [UpdateAttributeRequest and label merging][update-column]
- [DeleteAttributeRequest][delete-column]
- [Solutions, component association, and deletion dependencies][solution-sdk]
- [AttributeMetadata capabilities and identity][attribute-metadata]
- [EntityMetadata capabilities][entity-metadata]
- [Publisher schema and customization prefix][publisher]
- [Organization schema and base language][organization]
- [Column properties, requirement levels, and deletion behavior][column-properties]
- [Publishing and unpublished metadata][publishing]
- [Publication notes for create/delete versus update][publish-all]

[create-table]:
https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.messages.createentityrequest
[columns]:
https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.messages.createattributerequest
[update-column]:
https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.messages.updateattributerequest
[delete-column]:
https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.messages.deleteattributerequest
[solution-sdk]:
https://learn.microsoft.com/power-platform/alm/solution-api
[attribute-metadata]:
https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.metadata.attributemetadata
[entity-metadata]:
https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.metadata.entitymetadata
[publisher]:
https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/publisher
[organization]:
https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/organization
[column-properties]:
https://learn.microsoft.com/power-apps/maker/data-platform/create-edit-field-solution-explorer
[publishing]:
https://learn.microsoft.com/power-apps/developer/model-driven-apps/publish-customizations
[publish-all]:
https://learn.microsoft.com/dotnet/api/microsoft.crm.sdk.messages.publishallxmlrequest
