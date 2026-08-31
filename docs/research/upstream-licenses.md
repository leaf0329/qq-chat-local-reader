# Upstream license review

Reviewed on 2026-08-30. Repository metadata and license texts must be checked again before copying or packaging any upstream file.

| Upstream | Observed license | Project policy |
| --- | --- | --- |
| `QQBackup/qq-win-db-key` | Custom Non-Commercial Open Source License v1 | Research behavior and file formats only. Do not copy source into the PolyForm-licensed core without separate permission and a documented compatibility review. |
| `QQBackup/QQDecrypt` | Custom Non-Commercial Open Source License v1 | Use as documentation/research input. Do not copy protected expression into the PolyForm-licensed core. |
| `Mythologyli/qq-nt-db` | Custom Non-Commercial Open Source License v1 | Research only unless separate permission and compatibility review are recorded. |
| `QQBackup/nt_msg_db_util` | GPL-3.0 | Do not link, embed, or copy into the PolyForm-licensed core. Any separate-process use or distribution would require a fresh legal and packaging review. |
| `QQBackup/ntdb-plaintext-extracter` | AGPL-3.0 | Do not link, embed, copy, or expose as part of the PolyForm-licensed application. |
| `QQBackup/QQ-History-Backup` | MIT | Candidate for selective reuse after file-level provenance and dependency review; preserve copyright and license notices. |
| `GPDdev/qq-chat-exporter-and-report-generator` | Apache-2.0 | Candidate for selective reuse after file-level review; preserve license, attribution, NOTICE, and modification requirements. |
| `Zzzzzzyt/ntqq-data-merge` | Unlicense | Candidate for selective reuse after verifying the relevant file has no additional notice or incompatible dependency. |
| `g122622/synthos` | MIT | Candidate for selective reuse after file-level provenance and dependency review; preserve copyright and license notices. |
| `Google.Protobuf` 3.36.0 | BSD-3-Clause | Approved runtime dependency for bounded Protobuf wire decoding; include its copyright and license notice in distributed third-party notices. |
| `System.Security.Cryptography.ProtectedData` 10.0.11 | MIT | Approved Windows DPAPI wrapper; include its license notice in distributed third-party notices. |
| `SQLitePCLRaw.provider.sqlcipher` 3.0.5 | Apache-2.0 | Approved managed provider pinned by lock files. It imports a separately supplied `sqlcipher.dll` and does not include a native binary. |
| SQLCipher Community 4.9.0 at `c7e811b399379c948b423872ad7ba91d2ce38434` | BSD-style terms | Approved for a reproducible Windows x64 source build. Preserve its exact license in binary distributions. |
| OpenSSL resolved from vcpkg at `701d832d37ccc61ec86855927d71c55dd7f624dc` | Apache-2.0 | Approved as the statically linked SQLCipher crypto provider. Preserve its resolved copyright/license file in binary distributions. |
| `ModelContextProtocol` 2.2.0 | Apache-2.0 | Official Tier 1 C# SDK used for the local STDIO MCP server. Include its license and transitive dependency notices in release artifacts. |

## Rules

- Facts, observed schemas, protocol behavior, and independently verified interoperability information may guide a clean implementation; source expression is not copied merely because a repository is public.
- Every reused file or substantial excerpt requires a provenance entry, an exact upstream revision, and a license compatibility check.
- The distributed application must include all required third-party notices.
- GPL, AGPL, custom reciprocal, or unclear-license code is excluded from the PolyForm core unless the licensing plan is explicitly revised first.
- Dependency licenses are checked from the resolved release artifacts, not only from repository badges or package metadata.

## Sources

- <https://github.com/QQBackup/qq-win-db-key>
- <https://github.com/QQBackup/QQDecrypt>
- <https://github.com/QQBackup/nt_msg_db_util>
- <https://github.com/QQBackup/ntdb-plaintext-extracter>
- <https://github.com/QQBackup/QQ-History-Backup>
- <https://github.com/Mythologyli/qq-nt-db>
- <https://github.com/GPDdev/qq-chat-exporter-and-report-generator>
- <https://github.com/Zzzzzzyt/ntqq-data-merge>
- <https://github.com/g122622/synthos>
- <https://www.nuget.org/packages/Google.Protobuf/3.36.0>
- <https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData/10.0.11>
- <https://www.nuget.org/packages/SQLitePCLRaw.provider.sqlcipher/3.0.5>
- <https://github.com/sqlcipher/sqlcipher/tree/c7e811b399379c948b423872ad7ba91d2ce38434>
- <https://github.com/microsoft/vcpkg/tree/701d832d37ccc61ec86855927d71c55dd7f624dc>
