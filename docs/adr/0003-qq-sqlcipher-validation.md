# ADR 0003: QQ SQLCipher candidate validation

- Status: Accepted
- Date: 2026-08-30

## Context

The QQ message database has a 1024-byte product-specific prefix followed by an encrypted, 4096-byte-page SQLCipher image. The live database also uses a standard 4096-byte-page SQLite WAL. A printable 16-byte value found in process memory is only a candidate and must not be treated as a key without database-level proof.

## Decision

Validation runs only on a point-in-time snapshot. A separate prepared image is created beside the snapshot by removing exactly 1024 bytes from the main database; the matching WAL is validated structurally and copied unchanged under the prepared database name. The transient SHM file is not reused.

The candidate is passed to SQLCipher through the byte-span `sqlite3_key` API and is never interpolated into a PRAGMA or converted to a managed string. After setting the key and before the first database operation, the adapter applies and reads back this version profile:

- `cipher_page_size = 4096`
- `kdf_iter = 4000`
- `cipher_hmac_algorithm = HMAC_SHA1`
- `cipher_kdf_algorithm = PBKDF2_HMAC_SHA512`

The adapter also verifies that the loaded native provider reports a SQLCipher version. A candidate passes the fast stage only if both `c2c_msg_table` and `group_msg_table` can be read from the schema. The single matching candidate is copied into a disposable, zeroable byte buffer, then must pass both `cipher_integrity_check` and SQLite `quick_check` before use.

Connections are read-only, unpooled, query-only, use memory-backed temporary storage, disable memory mapping, and enable SQLCipher memory security. Prepared files and any generated WAL/SHM state are deleted before the owning snapshot is removed.

## Consequences

- Wrong candidates cause no writes and are discarded without being logged.
- A structurally invalid prefix, page layout, WAL, missing schema, unsupported SQLCipher provider, profile mismatch, page-authentication failure, or SQLite integrity failure stops synchronization.
- Account ownership and message-column compatibility remain separate required checks in the version adapter; successful decryption alone is insufficient authorization to index messages.
