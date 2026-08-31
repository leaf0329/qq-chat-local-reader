# Third-party notices

QQ Chat Local Reader redistributes or depends on the following principal components. Their own licenses continue to apply.

- .NET and Microsoft.Extensions packages — MIT License — <https://github.com/dotnet/runtime>
- ModelContextProtocol C# SDK — Apache License 2.0 — <https://github.com/modelcontextprotocol/csharp-sdk>
- Microsoft.Data.Sqlite — MIT License — <https://github.com/dotnet/efcore>
- Google.Protobuf — BSD 3-Clause License — <https://github.com/protocolbuffers/protobuf>
- SQLitePCLRaw provider 3.0.5 — Apache License 2.0 — pinned package source commit `ed046114d5a30534e13294d94d78eb73de896ad4` — <https://github.com/ericsink/SQLitePCL.raw>
- SQLCipher Community 4.9.0 — BSD-style license — built from pinned source commit `c7e811b399379c948b423872ad7ba91d2ce38434` — <https://github.com/sqlcipher/sqlcipher>
- OpenSSL — Apache License 2.0 — statically linked from the package definition at pinned vcpkg commit `701d832d37ccc61ec86855927d71c55dd7f624dc` — <https://github.com/openssl/openssl>
- Inno Setup (installer build tool and runtime) — Inno Setup License — <https://jrsoftware.org/isinfo.php>
- Inno Setup Simplified Chinese messages — Inno Setup source license; pinned from `jrsoftware/issrc` commit `1ae7bf81dc0d2013235dfe4bb0b6f4e4a0b6b25c` and hash-verified during the build — <https://github.com/jrsoftware/issrc/blob/main/license.txt>

Release archives include the SQLCipher, OpenSSL, and SQLitePCLRaw license texts plus native build provenance under `THIRD-PARTY-LICENSES` and `BUILD-PROVENANCE.txt`. The legacy `SQLitePCLRaw.bundle_e_sqlcipher` binary is not permitted in a release archive.
