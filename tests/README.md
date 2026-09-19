# Terminal tests

Run from the repository root:

```sh
python3 tests/run_tui_tests.py
```

Requires .NET 10, Python 3, and Linux or WSL. No Python packages or Dataverse
credentials are needed. The runner builds the test host automatically.

The host references the application and supplies simulated metadata. The
runner drives the real TUI in a pseudo-terminal and checks:

- Customizable filtering and the 25/75 layout.
- Selection, paging, detail scrolling, and terminal resizing.
- Loading failures, retries, empty results, cancellation, and terminal reset.
- Long names: every selection stays visible while moving through 75 tables,
  and clipping follows the current sidebar width.
- Table columns screen: column listing with edit/delete capability flags,
  and the delete action.
- Create table screen: filling in the display name and submitting.
- Column editor screen: creating a new column and editing an existing one.

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

## Live test

The live test runs the full read-write lifecycle against a real Dataverse
environment. It is opt-in and requires credentials. Run it with:

```sh
dotnet run --project tests/dvtui.TerminalTests -- live-test \
    "https://<environment>.crm.dynamics.com" [solution-unique-name]
```

The live test creates a table and column, edits the column, publishes the
table, checks dependencies, deletes the column, and deletes the table.
It records a ledger in `live-test-ledger.txt` and cleans up in a `finally`
block. On WSL it sets `DE=wsl` so the OAuth browser opens on the Windows
host quickly.
