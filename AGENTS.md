# Repository Guidelines

## General

This project is for fun and learning. Only produce code or edit files after
first discussing it with the operator. Do not change anything that the operator
did not ask for. If further changes are required to finish a task, stop and ask
how to proceed.

## Project Structure

DVTUI is a small .NET console application built around a terminal user
interface. It's goal is to let a user administrate a Dataverse environment from
the terminal.

- `Program.cs` contains startup, input handling, and screen rendering.
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
python3 tests/test_entity_browser.py
```

See `tests/README.md` for requirements and coverage.

## Testing Guidelines

Keep tests under `tests/`. Terminal integration tests use a separate host
that references the application and supplies simulated metadata. No unit
testing framework or coverage threshold is configured yet. Name new tests
after the production type, for example `DataverseServiceTests`.
