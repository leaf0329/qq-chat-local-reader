# 0015 — Verified group information in the message snapshot

## Status

Accepted

## Context and evidence

The message database provides stable group identifiers but not reliable group titles. Read-only discovery found QQ's sibling `group_info.db`. A real snapshot proved it uses the same account database key and contains `group_list` and `group_member3`. The configured acceptance group matched a non-empty group title. For the recent seven-day acceptance range, all 181 comparable message display names matched member column `20002`; no chat text or names were printed by the probe.

The database also contains virtual FTS tables requiring QQ's private `pinyin_letter` tokenizer. Generic schema inspection therefore skips virtual tables rather than loading or emulating the private tokenizer.

## Decision

Include `group_info.db` and its verified companion files in the same bounded VSS request as `nt_msg.db`. Prepare both encrypted images inside the same short-lived snapshot and use the already-resolved in-memory account key. The strict QQ 9.9.33 group adapter validates required numeric columns before reading group identifiers/titles and member identifiers/display names.

## Consequences

- Listing conversations needs only one elevation prompt and shows verified group names when available.
- MCP can list verified members for one explicit group.
- Unsupported schema or missing group information fails explicitly; message synchronization does not invent titles.
- The helper allowlist expands only to the exact sibling group database and known WAL/material suffixes.
