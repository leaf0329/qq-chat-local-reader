# ADR 0009: Bounded index search and conversation context

- Status: Accepted
- Date: 2026-08-31

## Context

GUI, CLI, and MCP need identical message search behavior over the encrypted normalized index. Search results may span several explicitly selected conversations, so pagination must remain stable even when timestamps collide. Chinese text also needs reliable substring matching; relying on an unverified FTS tokenizer could silently miss expected matches.

## Decision

Search accepts one account, one or more explicit conversations, a half-open UTC range, an optional text keyword, an optional exact sender identifier, a page size, and an opaque continuation cursor. The default page size is 100 and the hard maximum is 500.

Order results by timestamp, conversation type, conversation identifier, and stable message identifier. The cursor stores exactly that last ordering key in a bounded, versioned Base64URL payload. Cursor data is treated as untrusted input, size-checked, strict-UTF-8 decoded, and used only through SQLite parameters. It is an opaque pagination token, not an authorization token; every page still reapplies account, conversation, date, and sender scope.

For the first supported index version, match keywords with SQLite `instr(lower(text), lower(keyword))` over separately stored ordered text segments. This provides dependable Chinese substring behavior without assuming that the legacy native SQLCipher package includes a suitable FTS5 trigram tokenizer. A future maintained SQLCipher build may add a measured FTS index without changing the public search contract.

Context lookup requires an exact account, conversation, and stable message identifier. It returns at most 100 messages before and 100 after the anchor, defaults to 20 each way, never crosses a conversation boundary, and reports the anchor position explicitly.

## Consequences

- All application surfaces can share one deterministic query contract.
- A modified cursor cannot widen the selected account or conversation scope.
- Chinese substring search is correct for the stored text, though large indexes may be slower than a reviewed FTS implementation.
- Context expansion cannot accidentally mix similarly named conversations.
