# Step 1: Select a solution and browse its contents

## 1. Purpose and implementation contract

Implement this startup flow in dvtui:

```text
Existing login -> Required solution selection -> Read-only solution browser
                              ^                            |
                              +--------- Esc --------------+
```

You are implementing step 1 of dvtui. Follow this plan, complete the tasks below in order, and
report the verification results. Do not expand the scope or implement later administration features.

The required outcome is:

1. Keep the existing Dataverse login.
2. Require the user to select an existing solution after login, even if only one is available.
3. Present that solution's component inventory in the TUI.
4. Preserve useful table properties and column inspection for tables in that inventory.
5. Make no Dataverse writes.

All project artifacts and UI text must be in English. Use lowercase `dvtui` for the application
name. Keep every changed or added file line within 100 characters. Follow `AGENTS.md`, including
argument layout and method chaining. Do not upgrade packages, change authentication, introduce
a UI framework, commit, or push as part of this task.

## 2. Scope decisions

### Included

- A required, keyboard-operated solution picker with loading, retry, and empty states.
- Visible solutions returned to the current user, both managed and unmanaged.
- A solution header containing its friendly name, unique name, version, and management type.
- All component rows returned for the selected solution, including unfamiliar component types.
- Resolved table names and the existing rich table details and column display.
- A basic type-and-identifier detail view for other components.
- Returning to the picker without signing in again.
- Cancellation, bounded shutdown, terminal restoration, and automated terminal coverage.

### Explicitly excluded

- Creating, updating, deleting, importing, exporting, or publishing anything in Dataverse.
- Adding components to a solution or removing them from one.
- Setting a preferred solution, remembering the last selection, or selecting one automatically.
- Editing records, table definitions, forms, views, apps, flows, or security roles.
- Full designers or specialized detail readers for every component type.
- Reconstructing solution layers, dependencies, or an exported solution package.
- Claiming that every column in a table belongs to the selected solution.
- An environment-wide browser as an alternative route around solution selection.
- Search, persisted settings, and new command-line switches in this step.

For this first step, "solution contents" means a complete list of the component records the API
returns for that solution. Tables receive rich details. Other component types remain visible with
their type and identifiers; resolving all their friendly names is deferred. This is deliberately
not a promise to reproduce every grouping or count in the maker portal.

## 3. Current code: verified starting point

The repository was inspected when this plan was written. Recheck it before implementation because
the operator may have made additional changes.

| File | Current responsibility and relevant behavior |
| --- | --- |
| `Program.cs` | Runs login, then opens `EntityBrowserScreen`; owns service disposal. |
| `Services/DataverseService.cs` | Owns `ServiceClient`; reads table and column metadata. |
| `Views/StartupScreen.cs` | URL entry, login progress, connection errors, and cancellation. |
| `Views/EntityBrowserScreen.cs` | Table list, async details, navigation, retry, and shutdown. |
| `Views/EntityDetailsView.cs` | Renders table properties and columns. |
| `Views/ScrollableContent.cs` | Scrolls rendered detail content. |
| `Models/DataverseEntity.cs` | Table properties; currently does not retain `MetadataId`. |
| `Models/DataverseEntityDetails.cs` | A table and its columns. |
| `Models/DataverseField.cs` | Column labels, type, and description. |
| `tests/dvtui.TerminalTests/Program.cs` | Test host with simulated data and failures. |
| `tests/run_tui_tests.py` | PTY integration runner; not a conventional unit test project. |

The project targets .NET 10 and currently references Dataverse.Client 1.2.27 and
Spectre.Console 0.57.2. Use these installed dependencies.

Current behavior that must change:

- `GetEntitiesAsync` reads tables across the environment.
- `EntityBrowserScreen` filters those tables using `IsCustomizable == true`.
- Both metadata reads use `RetrieveAsIfPublished = false`.
- There is no selected solution or solution component model.

Current behavior to preserve:

- URL precedence: command-line argument, then `DVTUI_DEFAULT_ENV`, then interactive entry.
- Existing OAuth/browser handling, including the WSL setup.
- List/detail focus, scrolling, resizing, clipping, and rendering untrusted labels as plain text.
- No repainting once an idle screen is stable.
- Cancellation and bounded cleanup when an operation ignores cancellation.
- PTY process-group cleanup and builds with `--disable-build-servers` in the test runner.

## 4. Data semantics: implement these before polishing the UI

### 4.1 Solutions are records; tables and columns are metadata

Use the existing SDK connection. Query `solution` and `solutioncomponent` using `QueryExpression`
and `ServiceClient.RetrieveMultipleAsync`. Continue using metadata requests through
`ServiceClient.ExecuteAsync` for table definitions. Do not introduce direct HTTP requests.

A solution ID is not a table metadata ID. A solution component row ID is not its component object's
ID. Keep these separate in both models and method parameters.

### 4.2 Solution selection query

Query `solution` with an explicit column set:

```text
solutionid, friendlyname, uniquename, version, ismanaged, description
```

Filter on `isvisible = true`. Do not filter out managed solutions. Do not maintain a hard-coded
list of solution names or IDs to include or exclude. If a default or platform solution satisfies
the query, it is selectable under this policy. The picker must describe the list as visible
solutions, rather than claiming to include hidden internal solutions.

Order by `friendlyname`, then `uniquename`, then `solutionid` for deterministic paging. Use the
solution ID as selection identity; friendly names can be duplicated. Fall back to the unique name
or ID when display values are missing. Read missing nullable booleans as unknown, not false.

Use paging from the beginning. Read until `MoreRecords` is false, increment `PageNumber`, and pass
the returned `PagingCookie` into the next request. Use a named page-size constant, initially 5000.
Do not combine `TopCount` with this full-list query. Pass cancellation to every call and check it
between pages. Do not expose the first page as if it were the complete list. See [paging].

### 4.3 Solution component query

Query `solutioncomponent` with `solutionid` equal to the selected solution ID. Request:

```text
solutioncomponentid, solutionid, componenttype, objectid,
rootsolutioncomponentid, rootcomponentbehavior
```

Use deterministic ordering ending in `solutioncomponentid` and the same paging rules. A failure
on a later page is a failed load, not a successful but truncated inventory.

Keep the component type's integer value even when the application does not recognize it.
Use a small, centralized set of documented type names, with `Type <number>` as the fallback.
At minimum recognize table/entity (1), column/attribute (2), choice/option set (9), view/saved query
(26), workflow (29), system form (60), and web resource (61). Verify these values against
[components] before implementing. Do not assume a workflow is necessarily a cloud flow.

The raw rows are authoritative for this inventory. Preserve each distinct `solutioncomponentid`.
Do not collapse different membership rows merely because their object IDs match. If the same row
is encountered twice during paging, deduplicate by its row ID. Counts describe component rows,
not distinct tables, objects, or fully expanded subcomponents.

### 4.4 Table inclusion and column context

Preserve the root component ID and the root inclusion behavior. The documented behaviors are:

| Value | Meaning |
| --- | --- |
| 0 | Include subcomponents |
| 1 | Do not include subcomponents |
| 2 | Include as shell only |
| Missing or other | Unknown; preserve the original value when present |

Zero is a valid value. Do not let a missing SDK attribute silently become zero.

Do not assume every included child has a separate component row. Conversely, reading a table's
full metadata does not prove that all its columns are included in this solution. Display the root
behavior and any explicit parent reference as information, without implementing an expansion or
membership inference engine in this step. See [components] and [solution-sdk].

The table column section must say:

```text
Environment columns (not a solution membership list)
```

The component list shows solution membership records. The table details show the current
environment definition as useful context. Keep that distinction visible even for a shell-only
table or a solution containing selected columns.

### 4.5 Metadata lookup and publication state

Retain `EntityMetadata.MetadataId` in `DataverseEntity`. Match table components' `objectid` values
to this ID. Never match by display name, publisher prefix, `ObjectTypeCode`, or row ID.

For this step, reuse the existing bulk table metadata approach with `EntityFilters.Entity` to
build an ID-to-table index once per content load. Only perform this read if there are table
components. Do not request attributes for every environment table.

Use the index only to enrich the already selected solution's rows. Never append the other tables
from the index to the component list. This is one metadata read, not one read per table name.

Fetch a selected table's columns lazily using `RetrieveEntityRequest`, its `MetadataId`, and
`EntityFilters.Entity | EntityFilters.Attributes`. The SDK supports metadata-ID lookup;
verify the request properties against [retrieve-entity].

Use `RetrieveAsIfPublished = true` consistently for the index and table details. This allows the
read-only administrative view to include unpublished metadata. It does not publish anything.
Label details as current environment metadata, including unpublished changes. It is not the
original definition from one managed solution layer. See [publishing].

If the metadata index or an individual lookup fails, keep the component inventory available.
Unresolved tables remain listed using their type and object ID, with an explanatory warning.
Do not substitute an empty solution or drop inaccessible components. Retrying the content load
should retry metadata resolution as well.

## 5. Proposed code boundaries

Use the existing three-way split: models, Dataverse access, and terminal views. Keep the existing
delegate-based view construction so the test host can supply deterministic data without login.
No new dependency injection container, repository framework, or generic screen framework is needed.

### Models

Add `Models/DataverseSolution.cs` with these properties:

- `Guid Id`
- `string FriendlyName`
- `string UniqueName`
- `string Version`
- `bool? IsManaged`
- `string Description`

Add `Models/DataverseSolutionComponent.cs` with:

- Component row ID and nullable object ID.
- Integer component type, preserving unknown values.
- Nullable root component ID and nullable root inclusion behavior.
- Optional resolved `DataverseEntity` for table components.
- Optional resolution error text when enrichment fails.

Keep SDK entities and Spectre renderables out of these models. Store original IDs and values;
derive display text in the presentation layer. Use named constants or enums for recognized types
and root behavior, while retaining a fallback for future values.

Add the table metadata ID to `DataverseEntity`. Keep the existing entity details and field models
unless a concrete display requirement needs an additional property.

### Service

Extend `DataverseService` with these operations, using the existing async conventions:

```text
GetSolutionsAsync(CancellationToken)
    -> List<DataverseSolution>

GetSolutionComponentsAsync(Guid solutionId, CancellationToken)
    -> List<DataverseSolutionComponent>

GetEntityAsync(Guid metadataId, CancellationToken)
    -> DataverseEntityDetails
```

The component method queries membership, resolves table summaries, and returns the inventory.
Reject an empty solution ID before sending a request. Keep solution filtering inside the service,
not just in the view. Do not add a nullable solution parameter that enables a global fallback.

Turn the existing environment-wide table loader into a private metadata-index helper if that is
its only remaining caller. Replace the logical-name detail method if no caller needs it after the
change. Do not leave obsolete public operations or unused classes for a hypothetical later step.

Share paging between the two record queries with one small private helper. Keep component mapping
as a simple pure helper if needed for focused fixture checks. Do not mix rendering into it.

### Views

Add `Views/SolutionSelectionScreen.cs` for loading and choosing solutions. Its `Show` method
accepts a solution-loading delegate and returns a selected solution or null when the user quits.

Evolve and rename `Views/EntityBrowserScreen.cs` to `Views/SolutionBrowserScreen.cs`. Update its
callers and remove the old name rather than keeping two overlapping browsers. It accepts:

- The selected solution model.
- A component-loading delegate already bound to that solution ID.
- A table detail delegate accepting a metadata ID and cancellation token.

Its result distinguishes `BackToSolutions` from `Quit`. Use a small explicit result enum in the
view layer. Do not make `Q` and `Esc` indistinguishable to the caller.

Reuse `EntityDetailsView` and `ScrollableContent`. Add a small component details renderer for
identifiers, type, parent, and inclusion behavior. It should work for unknown component types.

### Program

Keep login and service ownership in `Program`. After successful login, run a simple loop:

1. Open the picker using the connected service.
2. Exit if no solution is selected.
3. Capture the selected solution in the content-loading delegate.
4. Open the solution browser.
5. Return to the picker on `BackToSolutions`; exit on `Quit`.

Reload the visible solution list when returning to the picker. Do not reconnect or create another
`ServiceClient` on every selection. Dispose the service once when the application exits.

## 6. TUI behavior

### 6.1 Solution picker

Use the existing fullscreen Spectre.Console style with a scrollable list and a details pane.
List friendly names; show unique name, version, managed/unmanaged/unknown, and description in the
details pane. Duplicate friendly names must remain distinguishable through their unique names.
Use a reasonable picker split, such as 40/60, so solution names have useful space.

| Key | Action |
| --- | --- |
| Up / Down | Move the current selection. |
| PageUp / PageDown | Move by the visible page size. |
| Home / End | Move to the first or last entry. |
| Tab | Switch list/detail focus. |
| Enter | Open the selected solution when a successful load has completed. |
| R | Reload or retry the solution list. |
| Esc / Q / Ctrl-C | Quit the application. |

While details have focus, movement keys scroll details; Enter still opens the selected solution.
Do not open anything while loading, after a failed load, or when the list is empty. During reload,
disable opening stale selections. `R` must not start concurrent list requests.

Required states and example text:

- Loading: `Loading solutions...`
- Ready: `<count> visible solutions | Enter: open`
- Empty: `No visible solutions found. R: reload | Q: quit`
- Error: `Could not load solutions. R: retry | Q: quit`, with readable error details.

Always require Enter, including for a single available solution. Do not turn a permission error
into an empty-result message. For redirected input or output, report that solution selection needs
an interactive terminal and exit without opening a solution or falling back to the global list.

### 6.2 Solution browser

Keep the current browser's 25/75 list/detail split. Change the list title to `Components` and the
detail title to `Component details`. Sort by type and then resolved name or object ID, using the
component row ID as a final tie-breaker. Every component row must remain reachable by navigation.

The header identifies the selected solution and the read-only mode. Show complete solution
identity in a compact overview when needed; clipped header text must not be the only place it is
available. Include version and managed/unmanaged/unknown state in that overview.

List examples:

```text
Table: account
Table: example_project
Column: <object ID>
System form: <object ID>
Type 12345: <object ID>
```

For a table, show component membership information followed by the existing table properties and
the clearly labeled environment columns. For another type, show its type code, object ID,
component row ID, parent reference, and root behavior when present. State that detailed inspection
of that type is not available in step 1. Do not show a misleading empty table or column panel.

Remove the `IsCustomizable == true` filter. Non-customizable tables in the selected solution are
valid read-only content. Continue displaying customization and management properties as facts.

Preserve movement, paging, Tab, and scrolling behavior. Add `Esc: solutions`. Keep `Q` and Ctrl-C
as application exit. `R` reloads the selected solution's component inventory and its table index.
It never changes the selected solution. Clear stale details during reload and cancel old loads.

Show component-row and table-row counts with those explicit meanings. An empty inventory is
`No components found in this solution`, not `No customizable tables found`. A solution with only
non-table components is not empty. Missing metadata produces a warning, not a smaller inventory.

On a component query failure, show the selected solution, the failure, retry, and back actions.
Never fall back to all environment tables. If a selected table disappears or becomes unreadable,
keep the list usable and show an error in its details pane.

### 6.3 Rendering and task lifetime

- Render remote strings using `Text` or explicit markup escaping.
- Support the existing minimum terminal size of 60 by 10 and the small-terminal message.
- Recompute visible rows from the actual space left after headers and footers.
- Clip long list labels and headers without wrapping over unrelated UI regions.
- Keep the selected row visible while navigating and resizing.
- Restore the alternate screen, cursor, and Ctrl-C mode on every exit and failure path.
- Refresh on data completion, user input, or resize; do not repaint continuously when idle.
- Allow quit and back while loads are pending; do not await network work inside key handlers.
- Cancel outstanding work on reload, selection change, back, and quit as applicable.
- Use the existing two-second cleanup budget as a total screen shutdown bound, not per task.
- Observe faults from superseded or timed-out tasks and dispose their token sources safely.
- Tag detail requests with component identity and a generation so late results cannot replace
  details for another component, a reloaded inventory, or a newly selected solution.
- Apply identity checks to failure results as well as successful results.
- Do not mutate UI state from background tasks after a screen has exited.

Use explicit per-screen state and straightforward methods. Do not build a generic navigation or
task orchestration framework to implement two screens.

## 7. Implementation sequence and checkpoints

### Task A: Establish the baseline

1. Read `AGENTS.md`, this plan, and the listed production/test files.
2. Check `git status --short`; preserve unrelated changes.
3. Run `dotnet build --disable-build-servers` and `python3 tests/run_tui_tests.py`.
4. Record pre-existing failures separately from failures caused by this work.
5. Verify the SDK symbols and schema against the official references below.

Do not contact a real environment merely to establish the baseline.

### Task B: Implement read-only data access

1. Add the solution and component models and preserve table metadata IDs.
2. Implement paged solution and component reads with explicit columns and cancellation.
3. Add table summary enrichment by metadata ID, keeping unresolved rows visible.
4. Implement lazy table details by metadata ID and the agreed publication-state policy.
5. Check that no create, update, delete, publish, import, or export calls were introduced.
6. Build before proceeding.

### Task C: Add the required solution picker

1. Implement the picker with simulated data available through its loading delegate.
2. Cover loading, explicit selection, empty results, errors, retry, and cancellation.
3. Preserve terminal cleanup and responsive input during slow requests.
4. Add focused picker PTY scenarios and run them.

### Task D: Adapt the browser to solution contents

1. Rename and evolve the current browser; keep useful layout and scrolling behavior.
2. Replace global table state with selected-solution component state.
3. Add solution context, generic component details, and explicit column-context labeling.
4. Remove customizable-only filtering and adjust the related fixtures and assertions.
5. Implement back versus quit results and cancellation of stale details.
6. Update existing browser tests and run the terminal suite.

### Task E: Wire the complete flow

1. Replace the direct login-to-table-browser transition in `Program.cs`.
2. Add the picker/browser loop without changing authentication or service ownership.
3. Extend the test host with a simulated successful login and at least two different solutions.
4. Verify solution switching, empty content, failures, and exit behavior across screen boundaries.
5. Remove obsolete call sites and update `tests/README.md` to describe the new coverage.

### Task F: Review and hand off

1. Run the verification in section 8.
2. Inspect the complete diff for scope, correctness, English text, and line length.
3. Report implemented behavior, checks run, failures, and any unverified live behavior.
4. Leave the changes uncommitted unless the operator separately requests a commit.

## 8. Verification

### 8.1 Automated fixtures and terminal scenarios

Use the existing C# test host and Python PTY runner. Keep credentials out of tests. Do not add a
test framework just to reproduce assertions that fit the current host.

The minimum fixture set contains two solutions with overlapping table names but different IDs or
memberships, one shared table with distinct membership rows, a managed solution, an empty solution,
and a solution containing only non-table components. Include duplicate friendly names, brackets,
long names, unknown component types, and unresolved table metadata.

Required scenarios:

1. Successful simulated login opens the picker, not a component browser.
2. A single solution still waits for Enter; quit does not invoke a content loader.
3. Selecting solution A loads A's ID and never displays B-only or environment-only tables.
4. Esc returns to the picker; selecting B changes both header and inventory without another login.
5. Managed solutions and non-customizable member tables remain visible.
6. A non-table-only solution displays its components; unknown types retain their identifiers.
7. Empty solutions and empty solution lists have different, accurate messages.
8. Solution and component load failures support retry without pretending there are no records.
9. Failed name resolution leaves the component listed and shows the warning.
10. Table details show the column-context label and root behavior for a shell or segmented table.
11. Delayed success and delayed failure from a previous selection never replace current details.
12. Reload cancels old details and does not permit opening stale picker rows.
13. Paging, Home/End, focus, detail scrolling, long labels, and resizing continue to work.
14. Idle screens stop repainting; bracket-containing names do not trigger markup errors.
15. Q and Ctrl-C work during picker loading, content loading, and table detail loading.
16. Esc works during content loading; uncooperative tasks do not block bounded screen exit.
17. Terminal state is restored after quit and after returning between screens.
18. Redirected terminal handling does not automatically select a solution.

For pure component-mapping checks, use synthetic SDK records if the mapping is extracted for
testing. Cover metadata-ID matching, missing versus zero root behavior, unknown types, and keeping
two different membership row IDs for the same object. These checks may run as a dedicated host
mode that fails with a nonzero exit code; no Python reimplementation of C# business logic.

Read PTY output until an expected marker or a bounded deadline, then send the next key. Output is
incremental, so a later read is not a full screenshot. Do not use fixed sleeps to prove readiness.
Extend the current wait helper where needed. Keep process-group teardown in `finally`.

UI fixtures do not verify real SDK queries. Inspect the query code for solution-ID filtering,
explicit columns, paging cookies, page increments, stable ordering, and cancellation. If paging
is not exercised against a fake SDK boundary or a suitable live solution, report that limitation;
do not claim that a 75-row UI fixture proves server paging.

### 8.2 Required commands

Run from the repository root:

```sh
dotnet build --disable-build-servers
python3 tests/run_tui_tests.py
git diff --check
git status --short
```

Also check every added or changed text file for lines longer than 100 characters. Include untracked
files: `git diff --check` alone will not inspect them. Inspect the final diff and confirm that no
old global-browser route remains and no SDK write calls were introduced.

Do not call `dotnet test` a substitute for the Python runner. The current terminal host is not a
test-framework project.

### 8.3 Optional live, read-only smoke test

Use an environment and login supplied by the operator. Do not invent credentials, create fixture
data, or change permissions to make verification pass. If access is unavailable, finish the local
checks and explicitly report that live verification was not performed.

When access is available:

1. Sign in and verify that the picker is mandatory.
2. Choose an existing solution and compare its identity with the maker portal.
3. Inspect known table and non-table components and compare membership with the same solution.
4. Verify that an unrelated environment table is absent from the component list.
5. Inspect existing table columns and confirm the environment-context label is visible.
6. Check a managed solution and a segmented or shell-only table if available without changes.
7. Switch solutions and verify isolation and reuse of the existing login.
8. Quit and verify that the application exits and the terminal is restored.

Compare like-for-like objects, not a raw component-row count with a maker portal top-level count.
Do not export or publish a solution as a verification shortcut. Do not create or delete test data.

## 9. Definition of done

- The real startup path cannot browse content before an explicit solution selection.
- The selected solution's ID constrains the component query.
- All returned component rows remain inspectable, including unsupported and unresolved types.
- Tables have useful names, properties, and lazily loaded column context.
- Managed and non-customizable content is not hidden by an editing-oriented filter.
- Shell/segmented membership is not misrepresented as the table's entire column definition.
- Back, retry, cancellation, stale-result protection, and terminal cleanup work as specified.
- No Dataverse mutation has been introduced or performed for testing.
- Build and terminal tests pass, or pre-existing/environment failures are precisely identified.
- New behavior and known verification limits are documented in the implementation handoff.

## 10. Official references

Consult these for API details. Do not infer SDK property names or component semantics from the
maker portal URL. The plan's UI layout and scope choices are project decisions, not SDK guarantees.

- [Solution record schema][solutions]
- [Solution component schema and component type values][components]
- [Working with solutions using the Dataverse SDK][solution-sdk]
- [QueryExpression overview; follow its "Page results" link][paging]
- [RetrieveEntityRequest and metadata-ID lookup][retrieve-entity]
- [Publishing and retrieving unpublished metadata][publishing]

[solutions]:
  https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/solution
[components]:
https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/solutioncomponent
[solution-sdk]:
  https://learn.microsoft.com/power-platform/alm/solution-api
[paging]:
https://learn.microsoft.com/power-apps/developer/data-platform/org-service/queryexpression/overview
[retrieve-entity]:
  https://learn.microsoft.com/dotnet/api/microsoft.xrm.sdk.messages.retrieveentityrequest
[publishing]:
  https://learn.microsoft.com/power-apps/developer/model-driven-apps/publish-customizations
