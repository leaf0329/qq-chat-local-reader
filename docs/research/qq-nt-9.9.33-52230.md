# QQ NT 9.9.33-52230 structural observations

Observed on 2026-08-30 from the current Windows QQ installation. No account identifiers, database paths, message contents, or key material were recorded.

## Runtime

- The active desktop process can be distinguished from QQ's Chromium helper processes by the loaded `resources/app/wrapper.node` module.
- The version directory containing that module reports `9.9.33-52230`. This is used as the adapter version, because the launcher executable's file version can lag behind the active resource version.

## Message database set

- The configured account directory contains `nt_qq/nt_db/nt_msg.db`.
- The live database set includes a WAL file, an SHM file, and first/last material files.
- The main database length minus 1024 bytes is aligned to 4096-byte pages.
- Neither byte zero nor byte 1024 begins with a plaintext SQLite header, consistent with an encrypted page image after the QQ-specific prefix.
- The WAL has a standard SQLite WAL magic value, declares a 4096-byte page size, and its frames are aligned to `24 + 4096` bytes.

These observations identify candidate layout rules only. A version adapter is accepted only after a point-in-time snapshot decrypts successfully, exposes the required schema, matches the selected account, and passes database integrity checks.

## Validated read chain

- A self-contained elevated helper created and cleaned a real VSS snapshot while QQ remained running.
- The read-only process scanner found a candidate that passed the required SQLCipher profile, both required table checks, `cipher_integrity_check`, and SQLite `quick_check`.
- The decrypted snapshot exposed 31 tables. The two message tables use numeric column identifiers.
- Clean-room field mapping was cross-checked against the published `QQBackup/nt_msg_db_util` schema research, whose GPL-3.0 source code is not copied or linked into this project.
- Local value-domain checks confirmed `40030` as the stable peer/group QQ identifier, `40013` as direction, `40033` as the actual sender QQ, `40050` as the Unix timestamp, and `40001` as the message-table primary key for this version.
- Database ownership was proven by matching the account directory identifier to sender `40033` on a self-direction row. Both configured local acceptance conversation types were found by exact stable identifier; their identifiers and names were not recorded.
