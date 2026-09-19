# Terminal and schema tests

Run from the repository root:

```sh
python3 tests/run_tui_tests.py
```

Requires .NET 10, Python 3, and Linux or WSL. No Python packages or Dataverse
credentials are needed. The runner builds the test host automatically.

The host references the application and supplies simulated metadata. The runner
builds that host, drives the real TUI in a pseudo-terminal, and checks:

- Customizable filtering and the shared 25/75 layout.
- Selection, paging, detail scrolling, and terminal resizing.
- Loading failures, retries, empty results, cancellation, and terminal reset.
- Long names: every selection stays visible while moving through 75 tables,
  and clipping follows the current sidebar width.
- Table columns screen: structured details, inline editing, capability flags,
  read-only reasons, and the delete action.
- Confirmation dialogs stay inside the alternate-screen TUI lifecycle.
- Create table screen: filling in the display name and submitting with Ctrl+S.
- Column editor screen: creating and editing with Ctrl+S.
- Managed-solution browsing with write actions disabled.

This is an integration test runner, invoked with Python rather than
`dotnet test`. It does not connect to a real Dataverse environment.
The runner disables persistent .NET build servers and cleans up each PTY
process group when a test finishes or the runner exits.

## Service tests

The C# service tests exercise `DataverseSchemaService` against a fake
executor. Run them with:

```sh
dotnet run --project tests/dvtui.TerminalTests -- service-tests
```

They cover typed metadata requests for the five supported column kinds,
solution scope, capability checks, identity conflicts, label preservation,
dependency-gated deletion, deletion readback, and table-scoped publication.

## Live test

The live test runs an opt-in read-write lifecycle against a disposable target in
a real Dataverse environment. It requires an interactive login and an exact
unmanaged solution with permission to create and delete custom metadata. Do not
point it at a production solution unless temporary objects are acceptable.
Run it with:

```sh
dotnet run --project tests/dvtui.TerminalTests -- live-test \
    "https://<environment>.crm.dynamics.com" [solution-unique-name]
```

The live test creates a uniquely named table and text column, reads them back,
edits the column, publishes only the table, checks dependencies, deletes the
column, and deletes the table. It records exact identities in an OS temporary
ledger and attempts bounded, identity-checked cleanup in a `finally` block,
including after an intermediate failure. Remaining objects or cleanup failures
are reported in that ledger; inspect it before rerunning.
On WSL it sets `DE=wsl` so the OAuth browser opens on the Windows host quickly.

The live test currently exercises the text-column lifecycle. The other four
supported creation types are covered by offline service assertions; live
validation remains an explicit follow-up for an authorized disposable target.
