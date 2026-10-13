# Threat model

**Status:** first draft, written in Phase 0 before any feature exists. Section 2 (storage)
was brought up to date in Phase 2 with what was built and tested. Phase 9 revisits every
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
| I | Recording content an app marked private (password managers) | Never capture the three privacy markers; never capture from excluded apps; Incognito stops capture | 1, 3, 7 | CaptureFilter and integration tests |
| I | Secrets stay in History | Sensitive clips are masked and expire in minutes | 1, 7 | SensitiveDetector and expiry tests |
| T | Malformed or oversized formats crash a parser or exhaust memory | Size cap on every parser; defaults 2 MB text and 20 MB image; no unbounded recursion | 1, 3, 9 | Unit and property tests |
| I | A privacy flag is present but cannot be read, so it is ignored | `CanIncludeInClipboardHistory` present with an unreadable value counts as "do not record" (fail closed) | 1 | CaptureFilter tests (done) |
| I | A clip, its content or a result is printed into a log by accident | `ToString` of clips, contents and results shows the kind, size and error code only, never content or the source app | 1 | Unit tests (done) |
| D | Hostile text makes a secret scan, a search or a preview slow | Hand-written linear scanners with no backtracking regular expressions; only the first 1,000,000 characters are scanned and 20,000 indexed; previews read only what they show | 1 | Hostile input tests (done) |
| I | A secret is not recognised, so it is kept as an ordinary clip | Residual risk: the detector favours few false alarms, so it misses short keys (under 32 characters, such as AWS access key ids), hex keys and any key with no letter and digit mix. Phase 7 reviews the list of patterns with the owner | 1, 7 | SensitiveDetector tests state what is and is not flagged |
| D | Another process holds the clipboard open, or floods it | Retry with backoff, then give up; one event per change by sequence number | 3 | Fake-surface tests |
| S | A process impersonates another to dodge the exclusion list | Exclusion matches the owning process's executable name; the source name is advisory | 3 | Integration tests |
| E | Capture needs elevation | None: `asInvoker`, never elevate | 0 | Manifest test (done) |

## 2. Storage (SQLite and crypto)

Built in Phase 2 (ADR 0008 and ADR 0009). The key is wrapped through an interface, and the
Windows binding for it (DPAPI) is built at the start of Phase 3 (ADR 0009), so until then the
wrapping is tested against a fake that behaves like DPAPI.

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| I | Reading the history database (`clipbench.db`) from disk or a backup | AES-256-GCM per value (content, preview, source app); the master key is wrapped for the current user, with the Windows binding in Phase 3 | 2, 3 | Leak tests read every byte of every file in the storage folder (database, `-wal`, `-shm`, key file) in UTF-8 and UTF-16, while open, after close and after later changes; a control proves the scan can see a leak; mutations that store a preview, source, content or key in plain are caught |
| T | Swapping or editing ciphertext | GCM tag plus associated data (clip id, column, format); failure is closed; a damaged record is skipped and counted and never returned | 2 | Tests flip every bit of a sealed value and swap values between rows, columns, ids and formats |
| T | Editing the plain metadata (times, pin flag, size, format) | **Residual, accepted.** An attacker with the file can reorder, pin, expire or break clips. They cannot reveal, forge or move content, and the format is bound into the content | 2 | A test records what a change can and cannot do |
| T | Deleting rows, or putting back an older copy of the database | **Residual, not defended.** The history has no counter beyond the schema version | | |
| I | Guessing a clip by comparing hashes | Duplicate detection uses HMAC-SHA256 under a key derived from the master key, over the format and the content | 2 | Different keys give different hashes; a known-answer test pins the derivation |
| I | Using the wrong key shows garbage or fails clip by clip | A sealed key check is verified once at open; a wrong key is one clear "history can't be decrypted" error and nothing is changed | 2 | Open and storage tests |
| I | Content in logs, crash dumps or temp files | Storage logs nothing, and prints no clip, summary or result with its text. SQLite keeps temp files in memory. Phase 4 adds logging with a marker test; Phase 9 adds crash handling and an audit | 2, 4, 9 | Print tests; the leak tests see no extra files; log marker test later |
| I | Key material or plaintext lingers in memory | Working keys and the master key are zeroed when disposed, and plaintext buffers after use. **Residual:** .NET strings cannot be wiped, and the search index holds decrypted text | 2 | Review |
| I | `key.bin` readable by other users | The file is created in the folder the caller names and inherits its permissions. For `%LOCALAPPDATA%` that is the current user only | 2, 3, 9 | Review; Phase 9 checks the real folder |
| I | A deleted or evicted clip stays in the file | `secure_delete`; evicting, deleting and wiping overwrite the value. Wipe all also rebuilds the file, empties the log, deletes the files and overwrites the key file before deleting it (best effort on a solid-state drive, where old blocks may remain out of reach) | 2 | Tests read the files after delete, after retention, after wipe and at the moment the files are deleted |
| E | SQL injection | Constants and named parameters only. CA2100 is an error, the one helper that takes variable SQL accepts constants only (CA1857), and a source scan rejects anything else | 2 | Hostile-text tests; the scan test; mutations that add concatenation, interpolation and a variable name are caught |
| D | A lost, wrong or damaged key or database | One recoverable error; nothing is replaced; the user is offered a fresh start, which is a separate action. A database whose key file is missing is never given a new key. A database from a newer version is refused | 2 | Key store and storage tests |
| D | A key that was written but cannot be read back | A new wrap is unwrapped before it is written; the file is written beside the target and moved into place | 2 | Key store tests |
| D | The history grows without bound | 1,000 clips, 30 days and 256 MiB for unpinned clips, applied in memory and on disk by the same rules; a size cap on every encoded clip and a part limit on file lists | 1, 2 | Retention tests, a randomized agreement test, codec limit tests |
| D | Hostile or damaged stored bytes crash the decoder | Counts and lengths are checked before anything is allocated; invalid UTF-8 and trailing bytes are rejected | 2 | Truncation, trailing-byte, huge-count and random-input tests |

Residual: the in-memory search index is decrypted while the app runs.

## 3. Popup (UI)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| I | Screen capture or sharing shows secrets | `SetWindowDisplayAffinity` (setting, on by default); sensitive previews masked | 4 | Manual QA |
| T | Clip content abused through rendering (script in HTML) | Previews are plain text and images only; no web view | 4 | Review |
| D | A huge clip freezes the UI | Previews are truncated | 4 | ViewModel tests |

## 4. Paste (focus, clipboard write, key injection)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| S | Pasting into the wrong window after a focus change | Record the target window before the popup; restore focus and verify before sending keys | 3 | Command-sequence tests |
| T | Another process replaces the clipboard between write and paste | Check the clipboard sequence number before sending Ctrl+V | 3 | Integration tests |
| I | Pasted content lingers on the clipboard | Optional restore of the previous clipboard (on by default); own writes carry a private format and are not re-captured | 3, 5 | Integration tests |
| E | Input into an elevated window is blocked (UIPI) | Detect it and notify; never elevate | 3 | Unit tests |
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
