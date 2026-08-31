# 0016 — Self-contained Windows distribution and update metadata

## Status

Accepted

## Decision

Publish the WPF application for `win-x64` as a self-contained .NET 10 directory distribution whose primary entry is `qq-chat-local-reader.exe`. Publish the bounded elevation helper as its own self-contained single-file executable and place it beside the main program. This preserves the privilege boundary while requiring no system .NET installation.

The portable ZIP contains the same files as the per-user Inno Setup installation. The installer defaults to offering Codex registration after installation and invokes the installed program, which uses the official local `codex mcp add/remove` commands. It never overwrites an existing same-name registration that points elsewhere and only removes a registration still pointing to this executable.

The only default network operation is a fixed GitHub REST request for this repository's latest Release metadata. It sends a fixed product/version User-Agent, validates the returned HTTPS GitHub page, never downloads or executes an update, and can be disabled in Settings.

Tagged builds run tests on GitHub's Windows runner, generate ZIP/setup/checksums, request GitHub artifact provenance attestation, and then publish the tag's Release. Test builds are not represented as Authenticode-signed. Microsoft Store signing remains a later option.

## Release gate

The native dependency gate is satisfied by ADR 0017's pinned SQLCipher/OpenSSL source build and packaged license/provenance files. A release still requires automated tests, real supported-QQ acceptance, clean portable and installer smoke tests, and a privacy scan. Unsigned beta builds must be described as unsigned; Defender/SmartScreen results are recorded rather than implied to be equivalent to Authenticode signing.

## Sources

- <https://docs.github.com/en/rest/releases/releases#get-the-latest-release>
- <https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations-to-establish-provenance-for-builds>
- <https://jrsoftware.org/isinfo.php>
