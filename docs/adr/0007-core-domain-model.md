---
status: accepted
---

# Core domain model

Phase 1 builds the pure C# domain in `ClipBench.Core`. The owner delegated the design choices on 2026-10-13; these are the ones a later reader would otherwise have to guess.

## Decisions

- **The model type is `Clip`, not `ClipItem`.** The glossary word is Clip and it says to avoid "item". `PLAN.md` was updated. The content types are `TextContent`, `HtmlContent`, `RtfContent`, `ImageContent` and `FilesContent`. Rich text carries a plain text alternative, because the platform layer reads one with the other.
- **Models are immutable records; services that hold state are small thread-safe classes.** `Clip`, the contents, `CaptureFilter`, `CaptureRequest` and the options are records, changed with `with`. `ClipHistory`, `PasteQueue` and `CollectSession` are mutable classes whose every change happens under one lock and whose readers get immutable snapshots, because the clipboard listener, the hotkey callback and the UI run on different threads. The capture filter is a record so that toggling incognito is `filter with { Incognito = true }`.
- **Expected failures are `Result` values; exceptions are for bugs.** A `Result` carries a `ResultError` with an `ErrorCode` and a message that never contains clip content. An uninitialised result is a failure. Printing a clip, a content or a result shows kind, size and error code only, so an accidental log call cannot leak content.
- **History limits apply to unpinned clips only.** The count cap (1,000) and age cap (30 days) never evict a pinned clip, and pinned clips do not count toward the cap. Limits are enforced when a clip is added and by `Trim`, so after an unpin the history may briefly hold more than the cap. A duplicate keeps the existing id and pin and takes the new time and source. Every removal is returned so storage deletes exactly those clips.
- **Capture is decided from metadata before any content is read.** `CaptureFilter` gets a `CaptureRequest` (format names, source process, kind, size). It rejects, in order: incognito, ClipBench's own writes, privacy markers, excluded applications, oversize. It fails closed: `CanIncludeInClipboardHistory` that is present but unreadable means "do not record".
- **Secret detection is hand-written, linear and conservative.** There are no regular expressions to backtrack. Card numbers need a Luhn check and a known brand prefix and length, because one random number in ten passes Luhn. Tokens need 32 to 256 base64 characters with letters and digits and at least 4.3 bits of entropy per character. It misses short keys (such as 20-character AWS key ids) and hex keys on purpose; Phase 7 reviews the pattern list with the owner.
- **Search compares a normalised form.** Both query and text are lower-cased, put on one line, stripped of Latin accents, Arabic vowel marks and tatweel, Hebrew points and invisible direction marks, and have their digits written as 0-9. Only the first 20,000 characters of a clip are indexed, queries are cut at 256, and fuzzy matching looks for the letters in order within a short stretch near the start.
- **Paste text uses Windows line breaks.** The `NewLine` and `BlankLine` separators and table rows use CR LF. A custom separator is taken as typed: `\n` is LF, so a Windows line break is typed `\r\n`.

## Not in Phase 1

- The Windows interfaces (`IClipboardReader`, `IClipboardWriter`, `IPasteInjector`, `IHotkeyService`) are added in Phase 3, and `IKeyProtector` in Phase 2, when their first user exists. An interface with no caller cannot be tested.
- Whether a clip is sensitive is not stored on the clip yet. Phase 7 adds the flag and the short expiry together with the schema change.
- The transform pipeline and snippets are Phase 6.
- The history has no total byte budget. It caps the number of unpinned clips (1,000), and one image may be 20 MiB, so a history full of large images could hold gigabytes in memory. Phase 2 decides whether the in-memory history keeps image bytes at all, or only a reference to the encrypted stored copy, and adds a byte budget if it keeps them.

## Consequences

- Core has no Windows, UI, database or network reference, which `ClipBench.ArchitectureTests` enforces.
- Several decisions are heuristics with stated limits. Each limit has a test that documents what is and is not flagged or matched.
