# ADR 0007: Verified media metadata and conversation-scoped reply resolution

- Status: Accepted
- Date: 2026-08-30

## Context

The bounded message-body parser exposes ordered content segments, but the application also needs useful attachment metadata and stable reply relationships. Numeric QQ fields are version-specific and some community descriptions are incomplete. Treating an uncertain field as a duration, path, or identifier would silently invent user data.

Reply candidates are especially context-dependent. A group sequence is meaningful only within its group, while a private reply may carry a main-table message identifier. Candidate values must not create cross-conversation links or ambiguous matches.

## Decision

For adapter version `9.9.33-52230`, recognize only independently documented media fields: subtype, file name, sending/cache path, file size, file extension, image dimensions, video dimensions and duration, and file/video preview path. The parser exposes these values only for image, file, voice, and video segments. Field `45410` is exposed only as video duration; voice duration remains absent because no reliable mapping has been established. Hash bytes are not exposed as a filename or text value.

Construct normalized records from the message-table primary key, conversation type and identifier, timestamp, direction, sender, parsed body, and resolved reply targets. Resolve group reply sequence candidates only against messages from the same selected group and snapshot. Resolve private message-ID candidates only against messages from the same selected private conversation and snapshot. A target is accepted only when exactly one row matches; otherwise the candidate remains unresolved. No text or timestamp similarity fallback is permitted.

All record and metadata `ToString()` implementations omit identifiers, names, paths, message contents, and summaries.

## Consequences

- The UI, index, exports, and MCP can consume one normalized read model instead of reinterpreting numeric QQ columns.
- Confirmed attachment metadata becomes useful without claiming to understand media contents.
- Voice messages are identified as voice but do not show an unverified duration.
- Replies outside the selected time range remain unresolved until a wider explicit read includes their target.
- New QQ versions must pass their own schema and real-data acceptance before using this mapping.
