# ADR 0011: Authorized persistent synchronization jobs

- Status: Accepted
- Date: 2026-08-31

## Decision

All GUI, CLI, and MCP synchronization requests use one application-layer job manager. A job receives a random identifier, persists its exact account/conversation/time request inside the encrypted index, passes through an authorization interface, and runs through one serialized source gate before atomically upserting its message batch.

States are awaiting authorization, running, completed, rejected, canceled, and failed. Public failures contain stable non-sensitive codes rather than exception messages. Completion records the committed message count. Cancellation propagates through authorization, snapshot creation, database preparation, and key scanning where supported, and is checked again before the index commit.

Job metadata and request JSON are stored in the SQLCipher index. On restart, terminal jobs remain queryable. Jobs left awaiting authorization or running are marked failed with `interrupted_by_restart`; restarting creates a new job from the persisted explicit request instead of assuming that an earlier partial operation succeeded.

The production source re-discovers the configured account database and supported running QQ process, creates a fresh elevated snapshot, verifies the account and adapter, and confirms every requested conversation against the snapshot's real conversation list before returning normalized messages.

## Consequences

- Application surfaces cannot bypass authorization or implement inconsistent sync behavior.
- At most one local QQ snapshot/key scan/index write pipeline runs at once.
- A process restart never reports an uncertain batch as completed.
- Persisted requests remain encrypted and can be safely resubmitted by an explicit restart action.
