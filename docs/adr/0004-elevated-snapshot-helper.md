# ADR 0004: Narrow elevated snapshot helper

- Status: Accepted
- Date: 2026-08-30

## Context

Creating a VSS snapshot requires administrator privileges on a normal Windows installation. Running the complete application as administrator would enlarge the privileged surface and would also expose message parsing, indexing, UI, and future MCP code to privileges they do not need.

A non-elevated process and its elevated child run at different integrity levels. The .NET `PipeOptions.CurrentUserOnly` check includes elevation identity on Windows, so it cannot be used for this parent-child channel even though both processes belong to the same Windows account.

## Decision

The main application remains `asInvoker`. It launches a separate `requireAdministrator` snapshot helper with the Windows `runas` verb only when a fresh snapshot is required. Refusing or cancelling the UAC prompt is a normal, recoverable outcome and does not weaken the policy automatically.

The command line contains only a random pipe name and the main process ID. Source and destination paths travel through a one-shot named pipe whose ACL grants access only to the current Windows user. The pipe uses the first-server-instance guarantee, and both endpoints verify the peer process ID through the Windows named-pipe APIs before exchanging a request.

The helper independently reloads the configured QQ data root. It accepts only an existing `nt_msg.db` in the direct `<QQ root>/<account>/nt_qq/nt_db/` layout, rejects reparse points in that path, and accepts at most the known QQ companion files beside that database. The destination is fixed to the application's per-user temporary directory. Keys, chat content, account identifiers, and filesystem paths are not accepted as command-line arguments and are not written to standard output or error.

The helper creates the VSS snapshot, copies the bounded database set, deletes the shadow copy immediately, and returns ownership of the temporary copy to the main process. Failed handoff is cleaned by the helper; the main process also removes recognized orphan snapshot directories older than one day without recursive traversal or following reparse points.

## Consequences

- Only VSS creation and the bounded snapshot copy run elevated; decryption, parsing, indexing, UI, and MCP remain non-elevated.
- The explicit pipe ACL plus mutual PID verification replaces `PipeOptions.CurrentUserOnly` for this elevation boundary.
- An attacker already controlling the user's session is outside this channel's protection boundary, but an unrelated process cannot win the pipe race or redirect the helper to an arbitrary source or destination.
- Actual UAC/VSS acceptance remains a Windows integration test and is not triggered by ordinary unit tests.
