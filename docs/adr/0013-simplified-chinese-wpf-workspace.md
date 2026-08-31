# 0013 — Simplified Chinese WPF workspace

## Status

Accepted

## Decision

The first desktop UI is a single resizable WPF workspace with account and date controls at the top, explicit conversation checkboxes on the left, a bounded message result list in the center, and same-conversation context on the right. It intentionally avoids imitating QQ chat bubbles.

User-visible shared labels are loaded from a Simplified Chinese resource dictionary so another locale can be added without redesigning the window. The UI defaults to the most recent seven natural days, offers a 30-day preset and inclusive custom dates, and never treats an empty selection as all conversations. Export format and privacy mode use mutually exclusive selectors; GUI export defaults to raw as specified and always requires a plaintext warning confirmation.

Account discovery does not require elevation. Reading the live conversation catalog and synchronization use the same bounded snapshot path and may request elevation. Missing snapshot-helper deployment does not prevent the UI, encrypted-index browsing, search, or export from starting.

## Consequences

- The main workflow is usable without command-line knowledge.
- Live catalog work remains explicit and may display the Windows elevation prompt.
- Search is capped at 500 visible rows in the first UI; users are directed to narrower filters or cursor-based CLI/MCP pagination for larger result sets.
- A generic MCP configuration can be copied without including QQ content, account secrets, or database paths.
