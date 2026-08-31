# 0012 — Official MCP SDK and unified entry point

## Status

Accepted

## Context

The desktop application, command line, and local MCP server must use the same encrypted index, search rules, export behavior, and synchronization jobs. A handwritten JSON-RPC or MCP implementation would duplicate protocol negotiation, schema generation, cancellation, and transport behavior. STDIO also requires stdout to contain protocol messages only.

## Decision

Use the stable official `ModelContextProtocol` C# SDK and `Microsoft.Extensions.Hosting`. The WPF executable dispatches by its first argument: no arguments opens the desktop UI, `mcp` starts a long-lived stream transport over standard input/output, and other supported arguments run the command line interface. MCP diagnostics are written only to stderr.

MCP exposes bounded tools for indexed conversation listing, search, context, one-conversation synchronization, job status/cancellation, and plaintext export. A synchronization call is authorized by its explicit account, conversation type, conversation identifier, and bounded time range; when dates are omitted the shared rule selects the most recent seven natural days. It never interprets chat text as commands. Search results also carry an explicit untrusted-data notice.

## Consequences

- Protocol compatibility and tool schemas are maintained by the Tier 1 SDK rather than local protocol code.
- GUI, CLI, and MCP share the application runtime and persistent job manager.
- The MCP process is local STDIO only and does not open a listening network port.
- Export remains an explicit plaintext-producing operation and defaults to basic redaction.
- The main process stays non-elevated; only the existing bounded snapshot helper may request elevation during sync.

## Sources

- <https://github.com/modelcontextprotocol/csharp-sdk>
- <https://modelcontextprotocol.io/specification/2025-11-25/basic/transports>
- <https://modelcontextprotocol.io/specification/2025-11-25/server/tools>
