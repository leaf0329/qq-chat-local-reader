# 0014 — DPAPI-protected MCP authorization profiles

## Status

Accepted

## Decision

Every copied or installed MCP registration receives a random profile identifier. The identifier may appear in the MCP client configuration, but it is not an authorization secret. Its display name and trusted/untrusted state are stored in a separate DPAPI CurrentUser-protected profile file under an ACL-restricted application directory.

An untrusted registration that requests synchronization opens a local 120-second confirmation window showing the actual account, conversation, and local-time range. The user chooses one of three explicit outcomes: reject, allow once, or trust this registration and allow. A trusted profile skips later per-sync prompts, but tool validation still requires a concrete account, one concrete conversation, and a bounded date range. Starting `mcp` without a profile remains untrusted and cannot persist trust.

## Consequences

- Client-supplied names are not used as authorization identity.
- Copying a generic configuration creates a new independent, initially untrusted profile.
- Trust can be changed without putting a secret in command arguments, environment variables, logs, or public configuration.
- A missing, damaged, or different-Windows-user profile fails closed.
- Tool-capable clients can additionally apply their own approval policy to the non-read-only sync tool.
