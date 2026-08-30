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
