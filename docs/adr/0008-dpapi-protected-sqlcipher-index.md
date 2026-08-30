# ADR 0008: DPAPI-protected SQLCipher message index

- Status: Accepted for development; native SQLCipher packaging requires release review
- Date: 2026-08-30

## Context

Parsed messages need a durable local index for browsing, search, export, CLI, and MCP. The index contains message text, display names, identifiers, reply relationships, and local attachment paths, so a plaintext SQLite file or a key stored beside it in plaintext would violate the product privacy boundary.

The QQ database key is an ephemeral source credential and must never become the application index key. Index batches must also be idempotent and atomic: a repeated synchronization must not duplicate messages, and a failed batch must not leave a partial result.

## Decision

Create an independent 256-bit random index key. Wrap it with Windows DPAPI using `DataProtectionScope.CurrentUser` and fixed application entropy. Store a versioned protected-key envelope beside the index, created through a same-directory write-through temporary file and atomic rename. Raw key buffers and temporary protected-data buffers are cleared after use. The key is never represented as text.

The dedicated index directory disables inherited ACLs and grants inheritable full control only to the current Windows user. The encrypted database, WAL, temporary files, and protected key inherit this boundary. This protects against ordinary access by other local accounts and offline copying, but does not claim protection from malware already running as the same user or an administrator that takes ownership.

Use SQLCipher with an explicitly verified version-4 profile: 4096-byte pages, 256000 KDF iterations, HMAC-SHA512, and PBKDF2-HMAC-SHA512. Enable SQLCipher memory security, memory-backed temporary storage, disabled memory mapping, foreign keys, WAL, and full synchronous durability. On open, verify the application/schema identifiers, SQLCipher page authentication, and SQLite quick check.

Store messages under the composite key `(account, conversation type, conversation ID, stable message ID)`. A batch uses one immediate serializable SQLite transaction. Upsert the current observation, replace its ordered text segments and reply targets, and commit only after every record succeeds. The full normalized body is retained as encrypted structured JSON so media and reply metadata round-trip without inventing another lossy schema; ordered text is also stored relationally for the upcoming search layer.

`SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11 is reused from the existing QQ decryption chain, but NuGet now marks this package as legacy and unmaintained. It is acceptable for the current development baseline because resolved binaries are pinned and real acceptance passes. A maintained replacement or a reproducible, reviewed native SQLCipher build is a release blocker; silently floating to an unknown native binary is not acceptable.

## Consequences

- Installing or copying the application does not expose a reusable plaintext index key.
- Another Windows user cannot normally open the index or its key file.
- Repeated reads are idempotent, and exceptions roll back the complete batch.
- GUI, CLI, export, and MCP can share the same encrypted normalized store.
- Moving an index to another Windows user profile does not make it portable; migration needs an explicit future export/import design.
- The current native SQLCipher package must not pass the release gate without the documented dependency review.
