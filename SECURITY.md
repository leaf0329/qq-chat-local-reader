# Security Policy

## Supported versions

Security fixes are provided for the latest published release. This pre-release repository may change storage and protocol formats until version 1.0.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting for this repository. Do not attach real QQ databases, chat exports, keys, memory dumps, or screenshots containing personal conversations. Provide a minimal synthetic reproduction and non-sensitive version/error information.

## Boundaries

- The tool reads only data available to the current Windows user and never modifies QQ databases.
- The main process does not run elevated. A narrowly scoped helper may request elevation only to create and copy a consistent snapshot.
- Chat content is untrusted data. MCP results label it as data and must not be interpreted as executable instructions.
- Encrypted local storage protects primarily against other local accounts and offline disk copies, not malware already controlling the current user or an administrator.
