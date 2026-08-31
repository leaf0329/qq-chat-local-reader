# 0017 — Reproducible SQLCipher Windows x64 build

## Status

Accepted

## Decision

Build the release `sqlcipher.dll` from SQLCipher Community 4.9.0 source commit `c7e811b399379c948b423872ad7ba91d2ce38434`. Use the OpenSSL port definition at vcpkg commit `701d832d37ccc61ec86855927d71c55dd7f624dc` with the `x64-windows-static-md` triplet, so OpenSSL is statically linked while the DLL uses the normal dynamic MSVC runtime.

Compile with SQLCipher's required codec and extra-init definitions, OpenSSL crypto, memory-backed temporary storage, FTS5, disabled loadable extensions, and the supported no-log-device option. Omitting SQLCipher's internal log device prevents rejected candidate keys from flooding CLI or MCP standard error; application-level errors remain available. The build script verifies both source commits after checkout. Package the exact resolved SQLCipher and OpenSSL license files, an Apache-2.0 copy for `SQLitePCLRaw.provider.sqlcipher` 3.0.5, and a plain-text provenance record beside the application.

The managed provider imports `sqlcipher.dll` explicitly. Release construction fails if that file is absent or if the legacy `e_sqlcipher.dll` is present. Lock files pin the managed provider; native inputs are pinned independently because its NuGet package deliberately does not ship official SQLCipher binaries.

## Consequences

- A Windows runner with MSVC can reconstruct the native dependency from reviewed inputs instead of accepting an opaque legacy binary.
- The portable ZIP and installer carry the licenses required by the redistributed native components.
- Updating SQLCipher, OpenSSL, vcpkg, compiler options, or target architecture requires a new review and real-database acceptance.
- Exact byte-for-byte output can still vary with the hosted MSVC toolchain; GitHub provenance attests the actual release artifacts and the provenance file identifies all non-toolchain native inputs.

## Sources

- <https://github.com/sqlcipher/sqlcipher/tree/c7e811b399379c948b423872ad7ba91d2ce38434>
- <https://github.com/sqlcipher/sqlcipher/blob/c7e811b399379c948b423872ad7ba91d2ce38434/README.md>
- <https://www.nuget.org/packages/SQLitePCLRaw.provider.sqlcipher/3.0.5>
- <https://github.com/microsoft/vcpkg/tree/701d832d37ccc61ec86855927d71c55dd7f624dc>
