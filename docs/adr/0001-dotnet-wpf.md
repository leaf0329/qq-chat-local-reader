# ADR 0001: .NET 10, WPF, and a shared application core

- Status: Accepted
- Date: 2026-08-30

## Context

The application is Windows-only and needs native process inspection, Windows user-scoped secret protection, a desktop GUI, self-contained packaging, CLI dispatch, and a local STDIO MCP server. The host currently has .NET runtimes but no SDK; the repository can use an isolated SDK under the ignored `.tools/` directory.

Python has a strong exploratory ecosystem for existing QQ research, but a Python desktop distribution would add a large interpreter bundle and commonly relies on runtime extraction for single-file builds. Rust would provide a compact native binary but would require a new toolchain and more implementation work for WPF-equivalent UI, MCP, SQLCipher, and Windows APIs.

## Decision

- Target .NET 10 LTS and Windows x64 for the first release.
- Use WPF for the Simplified Chinese desktop UI.
- Publish self-contained Windows builds so end users do not install .NET.
- Keep domain models and application services independent of WPF, CLI, and MCP hosts.
- Use the official `ModelContextProtocol` C# SDK for STDIO MCP and task-oriented long-running sync operations.
- Use narrow P/Invoke wrappers for required read-only Windows process and filesystem APIs.
- Use SQLitePCLRaw/SQLCipher-compatible packages for the encrypted normalized index, subject to resolved-package verification.
- Use Google.Protobuf or a smaller compatible parser only after the actual NTQQ message schema and package licenses are verified.

## Why .NET 10

.NET 10 is the current LTS release and is supported through 2028-11-14. .NET 8 and .NET 9 installed on the development machine both reach end of support in 2026, so starting a new distributable application on them would create immediate migration work.

## Consequences

- The first release is intentionally Windows-only.
- The repository installs an isolated .NET 10 SDK for development; it does not modify the user's system-wide SDK configuration.
- WPF, Windows APIs, and DPAPI integration stay straightforward.
- Portable output is a ZIP containing one main executable plus controlled dependencies; physical single-file output remains optional.
- Native dependencies and self-contained publishing increase artifact size, but reduce end-user setup and runtime ambiguity.

## License notes

- WPF: MIT.
- MCP C# SDK: Apache-2.0.
- SQLitePCLRaw: Apache-2.0.
- SQLCipher Community: BSD-3-Clause.
- Exact NuGet package contents and notices must be verified from the resolved lock file before distribution.

## Sources

- <https://dotnet.microsoft.com/en-us/platform/support/policy>
- <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/>
- <https://github.com/modelcontextprotocol/csharp-sdk>
- <https://github.com/ericsink/SQLitePCL.raw>
- <https://github.com/sqlcipher/sqlcipher>
