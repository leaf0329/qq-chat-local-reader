# ADR 0002: Read-only QQ process inspection

- Status: Accepted
- Date: 2026-08-30

## Context

The local QQ message database is encrypted, and the database key is available only while the user's QQ session is running. Launching a modified QQ process, injecting code, installing hooks, or writing breakpoints into process memory would violate the product's read-only boundary and increase compatibility and security risk.

## Decision

The Windows adapter may inspect the already-running QQ process using only these access rights:

- `PROCESS_QUERY_INFORMATION`
- `PROCESS_VM_READ`

The adapter must not request process write, memory-operation, suspend, terminate, debug, or all-access rights. It must not enable `SeDebugPrivilege` for scanning.

Only committed, readable, private memory regions are scanned. A candidate is a bounded 16-byte printable ASCII value and is passed directly as bytes to local SQLCipher validation. Candidates are never written to logs, files, command lines, environment variables, standard streams, the clipboard, or managed strings. Reusable scan buffers are zeroed on success, cancellation, and failure.

The infrastructure project enables unsafe compilation only because the .NET `LibraryImport` source generator requires it. The implemented interop surface uses safe handles and managed buffers; handwritten pointer arithmetic and write-capable process APIs are excluded.

## Consequences

- The running QQ process remains unmodified and is not paused.
- Some protected or differently structured QQ versions may not be readable; the adapter must fail safely rather than broaden permissions automatically.
- Candidate extraction alone never establishes that a key is correct. SQLCipher parameters, required schema, account ownership, and database integrity must all pass before messages are parsed.
