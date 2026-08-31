# ADR 0010: Safe plaintext message exports

- Status: Accepted
- Date: 2026-08-31

## Context

Users need Markdown, JSON, and CSV exports from the normalized encrypted index. Export files are intentionally plaintext and may contain untrusted chat text, spreadsheet formulas, HTML, identifiers, and local attachment paths. All formats must represent the same selected message set without allowing message content to influence filenames or executable syntax.

## Decision

Build one normalized export row per message and render all three formats from that array. Each row includes account and conversation identity, stable message ID, UTC timestamp, sender, flattened verified content, resolved reply targets, and verified attachment metadata. Output names contain only a fixed prefix, local timestamp, random identifier, and fixed extension.

Write to a same-directory, write-through temporary file and atomically rename only after the complete export succeeds. Cancellation or failure deletes the temporary file. The result explicitly reports the format and message count. User-facing application layers must warn that the final file is plaintext before calling the exporter.

Raw mode preserves indexed values. Basic-redaction mode creates a fresh random HMAC salt for every export. Account, conversation, sender, message, phone, QQ, and identity-card values receive stable aliases within that export but different aliases in another export. Visible names, summaries, filenames, and message text are scanned as well. Local and preview paths become an explicit local-path placeholder.

CSV quotes every cell and prefixes an apostrophe when the first non-whitespace character is `=`, `+`, `-`, or `@`. Markdown escapes raw HTML and table delimiters. JSON is produced by `System.Text.Json`; chat content is data and never controls property names or output paths.

## Consequences

- Markdown, JSON, and CSV share identical input message counts and identifiers.
- Redacted exports preserve relationships inside one file without enabling deterministic cross-export correlation.
- CSV opened in common spreadsheet software does not directly execute formula-shaped chat content.
- Exported plaintext is outside DPAPI/SQLCipher protection and must be disclosed clearly in GUI, CLI, and MCP flows.
