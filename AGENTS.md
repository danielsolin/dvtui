# Repository Guidelines

## Project Structure

DVTUI is a small .NET console application built around a terminal user
interface.

- `Program.cs` contains startup, input handling, and screen rendering.
- `Services/` contains Dataverse connection and metadata access code.
- `Models/` contains data objects used by the TUI.
- `dvtui.csproj` defines the .NET target and NuGet dependencies.
- There is currently no test project or checked-in asset directory.

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

Run `dotnet build` after changes. Use `dotnet test` if a test project is
added; the repository currently has no automated tests.

## Coding Style and Naming

Use four spaces for indentation and keep every line at or below 80
characters. All source, comments, and documentation must be in English.
Prefer straightforward C# over clever abstractions. Use PascalCase for
types and public members, camelCase for locals and parameters, and `_camelCase`
for private fields.

When a method call spans lines, put each argument on its own line. When a
method chain spans lines, put the dot at the start of the next line.

## Testing Guidelines

No testing framework or coverage threshold is configured yet. New tests
should be placed in a separate test project and named after the production
type, for example `DataverseServiceTests`.

## Commits and Pull Requests

The current history contains only the initial `Init` commit, so no established
convention exists. Use short, imperative commit subjects such as
`Show connection status`. Pull requests should describe the behavior change,
include validation commands and results, and include terminal screenshots
when the TUI output changes.

## Security and Configuration

Do not commit access tokens, credentials, browser caches, or environment URLs
that are not intended for public use. Authentication should remain in the
interactive browser flow or approved local configuration.
