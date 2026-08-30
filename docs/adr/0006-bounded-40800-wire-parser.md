# ADR 0006: Bounded clean-room 40800 wire parser

- Status: Accepted
- Date: 2026-08-30

## Context

QQ stores ordered message content segments in the `40800` BLOB as Protobuf wire data. Community research documents the outer repeated field and important text, QQ-face, and reply fields, but the available reference implementation and `.proto` schema are GPL-3.0. Copying them would conflict with the project's licensing plan, while hand-writing low-level varint and length parsing would duplicate a mature security-sensitive codec.

Message bodies are untrusted local data. Corruption, extreme lengths, recursion, invalid UTF-8, unknown fields, and new QQ content types must not crash synchronization or be silently presented as fully understood.

## Decision

Use the BSD-3-Clause `Google.Protobuf` runtime's `CodedInputStream` directly, without importing generated code or an upstream `.proto` file. The clean-room parser recognizes only independently verified field numbers: outer repeated content `40800`; segment ID/type `45001`/`45002`; UTF-8 text `45101`; QQ-face ID/text `47601`/`47602`; and reply candidates, summary, and bounded embedded content at `47401`, `47402`, `47404`, `47413`, and `47710`.

The parser preserves segment order and enforces a 16 MiB body limit, 256 segment limit, and four-level embedded-reply limit. Text fields use strict UTF-8. It never guesses that arbitrary length-delimited bytes are text or nested messages.

Results are `Complete` only when every encountered field in the supported subset is understood. Well-formed bodies with unknown fields, invalid recognized text, or a reached parser limit are `Partial`; invalid wire data is `Malformed`. Unknown fields are counted but their payload is not exposed in this first layer. Models redact text, summaries, and identifiers from `ToString()`.

The version adapter exposes aggregate validation for an explicit `SyncRequest` and its half-open UTC range. This path reads selected `40800` values and returns only counts, enabling safe version acceptance without logging content.

## Consequences

- Text, QQ-face metadata, and reply candidates can be extracted without GPL code or a generated schema.
- Media and other extension fields remain explicitly partial until their minimal metadata parsers are implemented.
- A structurally valid but unsupported new content form cannot be mistaken for a complete message.
- Resolving reply candidates to a stable main-table message ID remains part of normalized message construction, not the wire parser.
