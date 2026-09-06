# DVTUI (Dataverse Text User Interface)

A lightweight TUI for interacting with Microsoft Dataverse via its Web API.

## Goal
Provide high-efficiency, low-distraction exploration of Dataverse metadata
and data via terminal.

## Hard Rules

- **80-Char Limit:** No line in any file may exceed 80 characters. This applies
  to code, comments, and documentation.
- **Language:** All project artifacts, including source code, comments, and
  documentation, must be written in English.
- **No Clever Code:** Prioritize readability and simplicity over complex
  abstractions or "clever" one-liners.
- **Clean Architecture:** Strict separation of concerns. No misplaced classes,
  no dead code, and no "just in case" code.
- **TUI Focus:** The primary interaction model is a Text User Interface.
- **Minimalist Start:** Start with no arguments or at most one (the URL).
- **Direct API Access:** Use the Web API directly.
- **Code Style (Arguments):** If a method call cannot fit on a single line,
  all arguments must be placed on individual new lines.
  Example:
  ```csharp
  object.Method(
      arg1,
      arg2
  );
  ```
- **Code Style (Chaining):** When chaining methods, the dot (.) must be placed
  at the beginning of the new line.
  Example:
  ```csharp
  object.Method()
    .AnotherMethod();
  ```
- **No Unannounced Changes:** Do not make structural changes, or take any major decisions, without asking the operator first.
- **Build Often:** Regularly build the project to ensure it continues to
  compile.

## Technical Stack

- **Runtime:** .NET
- **UI Library:** Spectre.Console
- **Communication:** HttpClient (Web API)
