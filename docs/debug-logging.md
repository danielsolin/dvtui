# Session debug logging

Each interactive dvtui process creates one session log under `tmp/logs`.
The default file name is `YYYYMMDD-HHMMSS.log`. If two processes start in the
same second, a numeric suffix is added to keep both sessions intact.

The log is created before the terminal UI starts and is closed during normal
shutdown. It is also flushed after each line, so a log remains useful when the
process is interrupted.

The log contains:

- Application startup, shutdown, process, runtime, terminal, and command-line
  context.
- Screen entry and exit events, selected solution/table/column identities, and
  form actions.
- Every key read by the TUI, including the screen and the active operation.
- Dataverse connection state, readiness, connection errors, and client
  disposal.
- Dataverse request and response records with operation IDs, elapsed time,
  request type, parameters, query filters, result counts, and metadata
  identities.
- Validation failures, cancellations, timeouts, unknown mutation outcomes,
  exceptions, and readback failures.

Known access-token, client-secret, password, refresh-token, and bearer
authorization values are redacted before a line is written. The log can still
contain environment URLs, solution names, schema names, metadata IDs, and
values entered into forms because those are needed to reconstruct the TUI
state and the Dataverse request.

When reporting a problem, stop dvtui and provide the complete log file from
the relevant session. Do not paste only the last screen; the request ID and
preceding UI events are often required to identify the failure.
