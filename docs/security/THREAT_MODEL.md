# Threat model

**Status:** first draft, written in Phase 0 before any feature exists. Section 2 (storage)
was brought up to date in Phase 2 and sections 1 and 4 (capture and paste) in Phase 3, with what was
built and tested. Phase 9 revisits every
row and checks that each mitigation has a test or a documented reason.

ClipBench sees everything a user copies, including passwords, tokens and personal data.
That makes it a high-value target, and it shapes every decision below.

## Assets

| Asset | Why it matters |
|---|---|
| Clip content, previews and source-app names | May hold secrets and personal data |
| The master key and `key.bin` | Whoever has it can decrypt the history |
| History metadata (times, counts, formats, sizes, pinned flag) | Reveals habits, even without content |
| The release artifacts (installer, zip) | A tampered build would run as the user |
| The repository and its workflows | The path from source to a signed release |

## Assumptions and what is not defended

ClipBench trusts the Windows kernel, the user's account and DPAPI. It does **not**
defend against:

- **Malware running as the same user.** It can read the clipboard and the process
  memory directly. Encryption at rest cannot help against it. This is stated in the
  README and in [SECURITY.md](../../SECURITY.md).
- **An administrator or SYSTEM-level attacker.**
- **Someone using the user's unlocked session.**
- **Cold-boot and memory-forensics attacks.** While running, the search index holds
  decrypted text.

**Visible metadata:** timestamps, item counts, formats, sizes, pinned flags and board
membership are stored in plain columns by design (ADR 0003).

## Method

STRIDE per component: **S**poofing, **T**ampering, **R**epudiation, **I**nformation
disclosure, **D**enial of service, **E**levation of privilege. "Phase" is when the
mitigation is built; "Evidence" is how it is checked.

## 1. Capture (clipboard listener and readers)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| I | Recording content an app marked private (password managers) | Never capture the three privacy markers; never capture from excluded apps; Incognito stops capture. The filter is applied before any content is read, so a rejected change is never copied into memory | 1, 3, 7 | CaptureFilter tests; reader tests on an in-memory clipboard show no read of the content; the same checks against the real clipboard (markers, the history flag as Windows stores it, an excluded program by its real name, incognito) |
| I | Secrets stay in History | Sensitive clips are masked and expire in minutes | 1, 7 | SensitiveDetector and expiry tests |
| T | Malformed or oversized formats crash a parser or exhaust memory | Size cap on every parser; defaults 2 MB text and 20 MB image; a hard cap on the raw data before it is copied (4 bytes per byte of the text limit, 36 million pixels); no unbounded recursion; a decompressor that stops at the size the header promises | 1, 3, 9 | Parser tests for HTML Format, file lists, bitmaps and PNG: damaged and hostile input, the boundary of every limit, random and mutated input, and a 256 MB decompression bomb that allocates almost nothing |
| I | A privacy flag is present but cannot be read, so it is ignored | `CanIncludeInClipboardHistory` present with an unreadable value counts as "do not record" (fail closed) | 1 | CaptureFilter tests (done) |
| I | A clip, its content or a result is printed into a log by accident | `ToString` of clips, contents and results shows the kind, size and error code only, never content or the source app | 1 | Unit tests (done) |
| D | Hostile text makes a secret scan, a search or a preview slow | Hand-written linear scanners with no backtracking regular expressions; only the first 1,000,000 characters are scanned and 20,000 indexed; previews read only what they show | 1 | Hostile input tests (done) |
| I | A secret is not recognised, so it is kept as an ordinary clip | Residual risk: the detector favours few false alarms, so it misses short keys (under 32 characters, such as AWS access key ids), hex keys and any key with no letter and digit mix. Phase 7 reviews the list of patterns with the owner | 1, 7 | SensitiveDetector tests state what is and is not flagged |
| D | Another process holds the clipboard open, or floods it | Retry with a doubling wait (about a second in all), then report "busy" (never "empty"); the clipboard is open only while bytes are copied; one event per change by sequence number | 3 | Fake-surface tests for the waits and the dedupe; a real clipboard held open by another thread |
| S | A process impersonates another to dodge the exclusion list | Exclusion matches the owning process's executable name; the source name is advisory. The owner is found with query-only rights, and a program that refuses is "unknown" and is never asked again with more rights | 3 | Owner lookup tests (every link can fail); real tests find this program by its real name |
| E | Capture needs elevation | None: `asInvoker`, never elevate | 0 | Manifest test (done) |
| T | A wrong call into Windows corrupts memory (a bitmap handle read as a block of memory) | One definition of which formats are memory; the binding refuses the others before calling Windows | 3 | A regression test that crashed the test host with a heap-corruption code before the fix; unit tests of every range boundary |
| I | ClipBench records its own paste, or the restored clipboard, as a new copy | A private format tags every write, including a restore; the listener drops tagged changes and the capture filter rejects them | 3 | Listener, writer, snapshot and real-clipboard tests |
| E | A hook, a keyboard reader, a program launcher, a privilege change or access to another process's memory is added | The list of Win32 functions is checked, the built assembly is inspected, the source is scanned, and only modifier keys are ever polled | 3 | Architecture tests, each shown to fail by adding what it forbids |

## 2. Storage (SQLite and crypto)

Built in Phase 2 (ADR 0008 and ADR 0009). The key is wrapped through an interface; its Windows
binding (DPAPI, current user, ClipBench's own entropy) was built first in Phase 3 (ADR 0009, ADR 0010).

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| I | Reading the history database (`clipbench.db`) from disk or a backup | AES-256-GCM per value (content, preview, source app); the master key is wrapped for the current user, with the Windows binding in Phase 3 | 2, 3 | Leak tests read every byte of every file in the storage folder (database, `-wal`, `-shm`, key file) in UTF-8 and UTF-16, while open, after close and after later changes; a control proves the scan can see a leak; mutations that store a preview, source, content or key in plain are caught |
| T | Swapping or editing ciphertext | GCM tag plus associated data (clip id, column, format); failure is closed; a damaged record is skipped and counted and never returned | 2 | Tests flip every bit of a sealed value and swap values between rows, columns, ids and formats |
| T | Editing the plain metadata (times, pin flag, size, format) | **Residual, accepted.** An attacker with the file can reorder, pin, expire or break clips. They cannot reveal, forge or move content, and the format is bound into the content | 2 | A test records what a change can and cannot do |
| T | Deleting rows, or putting back an older copy of the database | **Residual, not defended.** The history has no counter beyond the schema version | | |
| I | Guessing a clip by comparing hashes | Duplicate detection uses HMAC-SHA256 under a key derived from the master key, over the format and the content | 2 | Different keys give different hashes; a known-answer test pins the derivation |
| I | Using the wrong key shows garbage or fails clip by clip | A sealed key check is verified once at open; a wrong key is one clear "history can't be decrypted" error and nothing is changed | 2 | Open and storage tests |
| I | Content in logs, crash dumps or temp files | Storage logs nothing, and prints no clip, summary or result with its text. SQLite keeps temp files in memory. Phase 4 adds logging with a marker test; Phase 9 adds crash handling and an audit | 2, 4, 9 | Print tests; the leak tests see no extra files; log marker test later |
| I | Another program of the same user unwraps the key by accident | The wrap uses ClipBench's own entropy, so a program that calls DPAPI without it cannot unwrap the key. **Residual:** the entropy is a constant in the open source, so it does not stop malware running as the same user that knows it (see the assumptions) | 3 | Wrong entropy fails, every byte of a wrap changed in turn fails, a truncated wrap fails (tests run against the real DPAPI, also on CI) |
| I | Key material or plaintext lingers in memory | Working keys and the master key are zeroed when disposed, and plaintext buffers after use. **Residual:** .NET strings cannot be wiped, and the search index holds decrypted text | 2 | Review |
| I | `key.bin` readable by other users | The file is created in the folder the caller names and inherits its permissions. For `%LOCALAPPDATA%` that is the current user only | 2, 3, 9 | Review; Phase 9 checks the real folder |
| I | A deleted or evicted clip stays in the file | `secure_delete`; evicting, deleting and sweeping overwrite the value and empty the write-ahead log, which is best effort while another process has the database open (it has to wait for that reader). Wipe all also rebuilds the file, empties the log, deletes the files and overwrites the key file before deleting it (best effort on a solid-state drive, where old blocks may remain out of reach) | 2 | Tests read the files after delete, retention and sweep (while the database is still open, and after it is closed), after wipe, and at the moment the files are deleted |
| E | SQL injection | Constants and named parameters only. CA2100 is an error, the one helper that takes variable SQL accepts constants only (CA1857), and a source scan rejects anything else | 2 | Hostile-text tests; the scan test; mutations that add concatenation, interpolation and a variable name are caught |
| D | A lost, wrong or damaged key or database | One recoverable error; nothing is replaced; the user is offered a fresh start, which is a separate action. A database whose key file is missing is never given a new key. A database from a newer version is refused | 2 | Key store and storage tests |
| D | A key that was written but cannot be read back | A new wrap is unwrapped before it is written; the file is written beside the target and moved into place | 2 | Key store tests |
| D | The history grows without bound | 1,000 clips, 30 days and 256 MiB for unpinned clips, applied in memory and on disk by the same rules; a size cap on every encoded clip and a part limit on file lists | 1, 2 | Retention tests, a randomized agreement test, codec limit tests |
| D | Hostile or damaged stored bytes crash the decoder | Counts and lengths are checked before anything is allocated; invalid UTF-8 and trailing bytes are rejected | 2 | Truncation, trailing-byte, huge-count and random-input tests |

Residual: the in-memory search index is decrypted while the app runs.

## 3. Popup (UI)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| I | Screen capture or sharing shows secrets | The popup asks Windows to leave it out of screenshots, recordings and screen sharing (`SetWindowDisplayAffinity`, on by default; the setting arrives in Phase 7). The `--allow-screen-capture` switch turns it off for tests only | 4 | A real window keeps the setting (read back from Windows); a screenshot of the popup comes out black; manual QA |
| I | A secret is read over the shoulder, or aloud by a screen reader | Clips that look like secrets (card numbers, IBANs, tokens, keys) show a mask whose length does not depend on the secret and are never read out; Ctrl+R reveals one. The detector is a heuristic, so this is a courtesy, not a guarantee. A list of files is not scanned: a path is not a secret | 4 | Row, popup and library tests; the UI smoke test reads the names a screen reader would read |
| T | Clip content abused through rendering (script in HTML) | Previews are plain text and thumbnails only; there is no web view in the app. Thumbnails are decoded at 96 pixels wide by the system decoder | 4 | Review; the app references no web-view assembly |
| D | A huge clip freezes the UI | Rows show at most two lines of 100 characters and only the start of a very long line is looked at; the list holds at most 50 rows; pictures are decoded at thumbnail size, and only for the rows on screen | 4 | Row text tests (long lines, many lines) |
| I | The activity log reveals what was copied | `IActivityLog` has no text field: events are a fixed list, details are codes, sizes, kinds and types. An unexpected exception is logged by type only. Files are size-capped and rotated | 4 | Marker test over the written files (UTF-8 and UTF-16), log format tests |
| I | A notice reveals what was copied | The text of every notice is written by the app; the tray tool tip is fixed text | 4 | Paste flow tests (every message), review |
| T | The command line changes behavior | Three switches exist (`--data-dir`, `--hotkey`, `--allow-screen-capture`); wrong ones are ignored with a note and never fatal. Whoever can start the program with arguments already runs code as the user | 4 | Startup option tests |
| D | A second copy opens the same history | One running copy per data folder (a named mutex derived from the folder); a second launch opens the first copy's popup | 3, 4 | Guard tests; the smoke test starts the app on its own folder next to a running one |
| T | The tray icon is spoofed or lost | The icon lives on a hidden top-level window of the app, is added again when Explorer restarts, and is removed on exit | 4 | Fake-window tests; a real icon is added, changed and removed; manual QA |

## 4. Paste (focus, clipboard write, key injection)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| S | Pasting into the wrong window after a focus change | Record the target window before the popup; restore focus and believe only the foreground window itself; no key is sent unless the target has focus | 3 | Command-sequence tests on scripted focus (refused, slow, never); a real window test that skips unless the test window can take focus |
| T | Another process replaces the clipboard between write and paste | Check the clipboard sequence number before sending Ctrl+V, and again before restoring: a restore never overwrites a newer copy | 3 | Command-sequence tests; real sequence-number tests |
| I | Pasted content lingers on the clipboard | Optional restore of the previous clipboard (on by default) as a raw copy of every memory format, privacy markers included, overwritten after use; own writes and restores carry a private format and are not re-captured | 3, 5 | Snapshot tests (byte for byte, markers, size cap, overwrite on dispose); paste tests; a real snapshot |
| E | Input into an elevated window is blocked (UIPI) | Compare integrity levels first, and treat a program that refuses to be asked as higher; report "target elevated" before touching the clipboard; never elevate | 3 | Tracker and paste tests for every level and for a refusal; real level reads (this program, the desktop shell, the system process) |
| E | The target is an administrator window and the paste cannot be made | The clip is put on the clipboard instead and a notice says to press Ctrl+V; if even that fails, the notice says only that Windows blocked it. The same for keystrokes Windows blocks | 4 | Paste flow tests |
| D | Queue mode leaves Ctrl+V registered after a fault | Unregister on exhaustion, cancel, exit and crash paths; the OS releases it when the process ends | 5 | Dispose-path tests |

## 5. OCR (Phase 8)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| I | Screen pixels written to disk | Region captured and processed in memory only; saved only if the user pins the result | 8 | Review and test |
| I | Recognized text holds secrets | Goes through the same capture filter and sensitive-clip detection | 8 | Tests |
| T | Hostile image content | Uses the operating system's OCR component; no custom image parsers | 8 | Review |

## 6. Installer and distribution (Phase 10)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| S, T | A tampered or fake download | Signed binaries when signing is approved, `SHA256SUMS`, SBOM, build provenance attestations; draft releases the owner publishes by hand | 10 | Release workflow |
| E | Installer asks for admin rights | Per-user install by default; the app is `asInvoker` | 10 | Installer smoke test |
| T | A writable install folder allows DLL planting | Per-user folder under the user's profile; self-contained; no elevation | 10 | Review |
| I | Uninstall leaves history behind | Option to delete user data; wipe | 10 | Packaging test |

There is **no update mechanism** (ADR 0002). Users on old builds stay on them until they
download a new one, which is an accepted cost of having no network code.

## 7. Build and supply chain

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| T | A malicious or vulnerable dependency | Central package versions, lock files, `nuget.org` only with source mapping, vulnerability scan, dependency review, Dependabot | 0 | CI and guardrail tests |
| T | A compromised workflow action | Every action pinned by commit SHA; read-only permissions; no `pull_request_target`; event text never reaches a script | 0 | Workflow tests |
| T | Static analysis finds a flaw late | CodeQL, OpenSSF Scorecard | 0 | CI |
| I | Secrets in the repository | `.gitignore` for keys and `.env`; deny rules for reading them; none are used | 0 | Review |
| I | Runtime or test-runner telemetry | CI and contributors opt out of the test runner's; the Windows App SDK terms allow usage data, so Phase 9 tests that the running app opens no network connections | 0, 9 | CI env; Phase 9 test |
| R | Authorship of the history is falsified | Commit hook plus a CI guard on messages, identities and pull request text | 0 | Guard tests |

## Open questions

- Does the Windows App SDK runtime open any network connection in an unpackaged,
  self-contained app? (Phase 9)
- Which Windows reserved hotkeys will a future update claim? (ADR 0004)
- Which permission does the real storage folder have on a machine whose
  `%LOCALAPPDATA%` is redirected or shared? (Phase 3 and Phase 9)

Answered:

- **If DPAPI cannot unwrap the key** (a roaming profile restored on a new machine, a
  changed account): the key store reports "history can't be decrypted" and changes
  nothing. The interface offers to start fresh and never does it by itself (ADR 0009).
