# Repository Guidelines

## General

The name of this project is 'dvtui'. That is, in lowercase letters only.

## Project Structure

dvtui is a small .NET console application built around a terminal user
interface. It's goal is to let a user administrate a Dataverse environment from
the terminal.

- `Program.cs` contains application startup and lifecycle orchestration.
- `Services/` contains Dataverse connection and metadata access code.
- `Models/` contains data objects used by the TUI.
- `dvtui.csproj` defines the .NET target and NuGet dependencies.
- `tests/` contains the terminal test host and Python integration runner.

Keep presentation, Dataverse communication, and data models separate. Add
new functionality to the narrowest appropriate area.

## Build, Run, and Development Commands

Restore dependencies and build the application with:

```text
dotnet restore
dotnet build
```

Start interactively and enter the environment URL when prompted:

```text
dotnet run
```

The URL may also be supplied as the only argument:

```text
dotnet run -- "https://example.crm.dynamics.com"
```

Run `dotnet build` after changes. Run terminal integration tests with:

```text
python3 tests/run_tui_tests.py
```

See `tests/README.md` for requirements and coverage.

## Authentication and token cache

The application signs in with Microsoft's shared public Power Platform
client (MSAL). It uses `AuthenticationType.ExternalTokenManagement` so dvtui
owns the MSAL `IPublicClientApplication` directly. The provider lives in
`Services/MsalTokenProvider.cs`.

- Client ID: `51f81489-12ee-4a9e-aaae-a2591f45987d` (public, no secret)
- Authority: `AzureAdMultipleOrgs`
- Redirect URI: `http://localhost`
- Scope: `<environment-url>/user_impersonation`

The MSAL user token cache is serialized (`SerializeMsalV3`) and stored in a
plain file, one per environment host, under the user's application data
folder (on Linux, `~/.config/dvtui/`):

```text
~/.config/dvtui/token-cache-<host>.dat
```

File persistence is used because the built-in OAuth cache store depends on a
system keyring (Secret Service) on Linux, which is not reliably available.
Delete the file for a host to sign out of that environment.

## Testing Guidelines

Keep tests under `tests/`. Terminal integration tests use a separate host
that references the application and supplies simulated metadata. No unit
testing framework or coverage threshold is configured yet. Name new tests
after the production type, for example `DataverseServiceTests`.

PTY reads return only bytes written during the read window (incremental
redraws), and the app stops repainting once the screen is stable. Pace each
key press until the expected content is visible before pressing the next key;
do not assert on fixed read windows.

### Live tests against a real environment

The test host has a `live-test` mode that runs the real connection manager
against a live environment and exercises the full table lifecycle (create
table, add column, read back, edit, publish, delete):

```text
dotnet exec tests/dvtui.TerminalTests/bin/Debug/net10.0/dvtui.TerminalTests.dll live-test <url>
```

The test environment is `https://dmrnd.crm22.dynamics.com` (solution
`Crd815b`, publisher prefix `cr84e`). It creates a throwaway table and
deletes it when done.

When running from WSL, the interactive login window opens on the Windows
host; the operator must click OK before the test can continue. The test host
calls `ConfigureWslBrowser()` for `live-test` so the browser launches on the
host side.

To verify token persistence, run `live-test` twice: the first run logs
"Interactive login required", the second logs "Silent token acquired" and
connects without a browser. Both runs may log a trailing "Table cleanup
failed: The operation was canceled" after "Lifecycle complete"; that is a
post-completion teardown request, not an authentication failure.
