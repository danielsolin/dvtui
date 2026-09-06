# Terminal tests

Run from the repository root:

```sh
python3 tests/test_entity_browser.py
```

Requires .NET 10, Python 3, and Linux or WSL. No Python packages or Dataverse
credentials are needed. The runner builds the test host automatically.

The host references the application and supplies simulated metadata. The
runner drives the real browser in a pseudo-terminal and checks:

- Customizable filtering and the 25/75 layout.
- Selection, paging, detail scrolling, and terminal resizing.
- Loading failures, retries, empty results, cancellation, and terminal reset.
- Long names: every selection stays visible while moving through 75 tables,
  and clipping follows the current sidebar width.

This is an integration test runner, invoked with Python rather than
`dotnet test`. It does not connect to a real Dataverse environment.
