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
- `Views/` contains classed for TUI views.
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

See `docs/TESTING.md` for coverage and the live test.

## Authentication

Authentication and token caching are documented in `docs/ARCHITECTURE.md`.

## Testing

Keep tests under `tests/` and name them after the production type, for
example `DataverseServiceTests`. See `docs/TESTING.md` for coverage, PTY
pacing rules, and the live test.
