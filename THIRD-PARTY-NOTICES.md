# Third-party notices

QQ Chat Local Reader redistributes or depends on the following principal components. Their own licenses continue to apply.

- .NET and Microsoft.Extensions packages — MIT License — <https://github.com/dotnet/runtime>
- ModelContextProtocol C# SDK — Apache License 2.0 — <https://github.com/modelcontextprotocol/csharp-sdk>
- Microsoft.Data.Sqlite — MIT License — <https://github.com/dotnet/efcore>
- Google.Protobuf — BSD 3-Clause License — <https://github.com/protocolbuffers/protobuf>
- SQLitePCLRaw — Apache License 2.0 — <https://github.com/ericsink/SQLitePCL.raw>
- SQLCipher — BSD-style license — <https://github.com/sqlcipher/sqlcipher>
- Inno Setup (installer build tool and runtime) — Inno Setup License — <https://jrsoftware.org/isinfo.php>
- Inno Setup Simplified Chinese messages — Inno Setup source license; pinned from `jrsoftware/issrc` commit `1ae7bf81dc0d2013235dfe4bb0b6f4e4a0b6b25c` and hash-verified during the build — <https://github.com/jrsoftware/issrc/blob/main/license.txt>

Release archives should include the exact dependency license files generated or collected by the release process before a stable release is declared. The legacy `SQLitePCLRaw.bundle_e_sqlcipher` dependency remains approved for development builds only until replaced or reproducibly rebuilt with complete binary notices.
