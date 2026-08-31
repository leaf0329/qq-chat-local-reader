# 0016 — Self-contained Windows distribution and update metadata

## Status

Accepted for pre-release builds

## Decision

Publish the WPF application for `win-x64` as a self-contained .NET 10 directory distribution whose primary entry is `qq-chat-local-reader.exe`. Publish the bounded elevation helper as its own self-contained single-file executable and place it beside the main program. This preserves the privilege boundary while requiring no system .NET installation.

The portable ZIP contains the same files as the per-user Inno Setup installation. The installer defaults to offering Codex registration after installation and invokes the installed program, which uses the official local `codex mcp add/remove` commands. It never overwrites an existing same-name registration that points elsewhere and only removes a registration still pointing to this executable.

The only default network operation is a fixed GitHub REST request for this repository's latest Release metadata. It sends a fixed product/version User-Agent, validates the returned HTTPS GitHub page, never downloads or executes an update, and can be disabled in Settings.

Tagged builds run tests on GitHub's Windows runner, generate ZIP/setup/checksums, request GitHub artifact provenance attestation, and then publish the tag's Release. Test builds are not represented as Authenticode-signed. Microsoft Store signing remains a later option.

## Release gate

The legacy SQLCipher native bundle remains a documented pre-release blocker. A stable release requires a maintained or reproducibly built native SQLCipher package plus complete binary license notices and Defender/SmartScreen acceptance results.

## Sources

- <https://docs.github.com/en/rest/releases/releases#get-the-latest-release>
- <https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations-to-establish-provenance-for-builds>
- <https://jrsoftware.org/isinfo.php>
