---
status: accepted
---

# Secure storage: sealed values, a key check and a bounded history

Phase 2 stores the history in SQLite so that it survives restarts and shows nothing readable on disk. The owner delegated the design on 2026-10-13. These are the decisions a later reader would otherwise have to guess, and one thing that was planned for this phase and is not in it.

## Decisions

- **Every value that reveals what was copied is sealed.** Content, preview and source app are stored as `nonce(12) || ciphertext || tag(16)` under AES-256-GCM with a fresh random nonce. The nonce source is not injectable, because a caller-chosen nonce is the classic way to reuse one; a test draws 10,000 and checks that none repeat.
- **A sealed value is bound to where it was written.** The associated data is `"CB1C" || clip id (16 bytes, big-endian) || column name || format`. It adds the format to what the plan listed (row id and column), so a changed format column fails authentication instead of making the content parse as another kind. A value moved to another row, column or format is detected, skipped and counted. Tests cover swapping, copying between columns, changing the id and changing the format. The layout is pinned by tests, with known-answer tests for the key derivation, because data written today must open after an update.
- **Keys are derived, not reused.** HKDF-SHA256 turns the 32-byte master key into an encryption key and a separate hash key under distinct context strings. The duplicate hash is HMAC-SHA256 over the format byte and the encoded content under the hash key, so the database alone cannot confirm a guess about a clip.
- **The metadata columns are not authenticated, on purpose.** Ids, times, the pin flag, sizes and the hash are plain. Someone with the file can reorder, pin or expire clips, or make one unreadable, but cannot read, forge or move content. A per-row MAC would have to be rewritten on every pin and every re-copy, to protect timestamps against an attacker who can already delete the whole file. The threat model records this as a residual risk.
- **A key check row detects the wrong key once.** An encrypted constant is stored at creation and verified at open. A wrong key gives one clear "history can't be decrypted" error instead of a failure per clip. A database that has clips but no key check is not trusted with a new one.
- **A key or database that cannot be used is reported and never replaced.** A new key would make every saved clip permanently unreadable, so the key store and `ClipStorage.OpenAsync` return `HistoryUnreadable` and change nothing. In particular a database whose key file is missing is not given a new key. `StartFreshAsync` is a separate call, for the caller to make after the user agrees. A database from a newer version of the app is refused with its own error, because starting fresh would destroy it.
- **A new key is written only if it can be read back.** The store unwraps its own wrap before writing, writes beside the target and moves into place, and lets a key file that appeared first win. Deleting the key overwrites the file first, which is best effort on a solid-state drive.
- **Database settings.** Write-ahead logging, `secure_delete` (the open fails if it cannot be switched on), `trusted_schema` off and temp files in memory, so no unsealed data reaches a temp file. Tables are `STRICT` with `CHECK` constraints. Migrations run as one immediate transaction: a failing step leaves the database as it was. Connections are not pooled, so closing the repository releases the files.
- **A damaged record never hides the others.** Loads skip a record that fails authentication or decoding, or whose plain capture time is not a date, and report how many they skipped. Nothing unauthenticated is ever returned, and a tampered plain column cannot make a load throw.
- **Removing a clip also empties the write-ahead log.** `secure_delete` zeroes the database pages, but the copy written when the clip was saved stays in the log until it is checkpointed. Delete, retention and sweep therefore truncate the log when they removed something. The security review of this phase found that the first version relied on closing the database to do it, which the tests could not see.
- **Retention is one SQL statement with the same rules as `ClipHistory.Trim`.** Unpinned clips only: too old first, then the count, then the size budget, as a prefix rule so an older clip never outlives a newer one. A randomized test runs both on 250 histories and compares what each removes. Retention also removes expired clips, and an expiry removes even a pinned clip, because an expiry is asked for explicitly. Phase 7 may revisit that when it adds the sensitive flag.
- **The history has a byte budget.** This settles the open question in ADR 0007. The in-memory history keeps full clips, so `HistoryOptions.MaxTotalBytes` (256 MiB by default, unpinned clips only, the newest clip always kept) bounds memory, and storage applies the same budget to the disk. Summaries (id, format, time, pin, size, preview, source) can be read without decrypting any content, so a later phase can list clips without opening large images, or drop image bytes from memory, without a schema change.
- **Wipe all scrubs before it deletes.** Rows are deleted with `secure_delete` on, the file is rebuilt and the log emptied, and only then are the files deleted and the key shredded. A test reads the files at the moment they are deleted and finds no sealed value in them.
- **SQL is constants and named parameters.** CA2100 is an error for the storage library; the one helper that takes variable SQL accepts only constants, which the compiler enforces (CA1857); and a test scans the source for anything else. The migrator is the single reviewed exception, fed by constants that the assembly owns.
- **Errors never repeat what failed.** A database exception becomes a result with a code and a plain message. The exception text, which can contain a statement or data, is dropped.

## Considered options

- **Authenticating the metadata with a row MAC.** Rejected for now, for the reason above.
- **A connection per operation.** Rejected. One connection behind one lock is simpler to reason about, and a second repository on the same file waits on SQLite's busy timeout.
- **Replacing a lost key and keeping the old file "just in case".** Rejected. The file would be unreadable, and keeping it would only keep ciphertext around.

## Not in this phase: the Windows key protector

The plan put `DpapiKeyProtector` in Phase 2. It moves to the start of Phase 3, because every way to build it needs something the owner has not approved:

- **`CryptProtectData` through CsWin32.** This is the route the project rules require. It is a second new package. CsWin32 itself is MIT, but it pulls in three build-time metadata packages (`Microsoft.Windows.SDK.Win32Metadata`, `Microsoft.Windows.SDK.Win32Docs` and `Microsoft.Windows.WDK.Win32Metadata`) that carry the Windows SDK license, not an OSI license, and are labeled preview, alpha and experimental. ADR 0001 listed CsWin32 as MIT and noted the labels but not this license. The owner approved one new package for this phase, on the condition that it be MIT or Apache.
- **`System.Security.Cryptography.ProtectedData`.** Checked: it is not part of the .NET 10 Windows framework, so it is also a new package.
- **Windows' newer WinRT `DataProtectionProvider`.** It needs no package, but it has no app-specific entropy, which ADR 0003 specifies, so it would change the crypto design.
- **A hand-written `DllImport`.** The project rules forbid it where CsWin32 can generate the call.

`IKeyProtector` is in Core, and the key store, the storage and every test use it through a fake that behaves like DPAPI (it refuses a key wrapped for another user and a damaged wrap). Nothing in the app calls storage until Phase 4, so no history can be written unprotected in the meantime. Phase 3 starts with the owner's decision on CsWin32 and a second documented exception, next to the Windows App SDK, for the metadata licenses in the Phase 9 license audit. Then it adds `DpapiKeyProtector` with a Windows-only integration test.

## Consequences

- The "history can't be decrypted" error is the single path for a lost key, a wrong key, a damaged key file and a damaged database. The interface offers to start fresh and never does it by itself.
- Plain metadata is a documented leak, and unauthenticated metadata is a documented risk (see the threat model).
- Integrity is per record. A tampered record is lost, and the rest of the history still loads.
- The stored format is versioned in two places: the schema version table and the `CB1` tags in the associated data. A future change adds a version rather than editing one.
