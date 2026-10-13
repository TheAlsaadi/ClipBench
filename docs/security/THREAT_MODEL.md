# Threat model

**Status:** first draft, written in Phase 0 before any feature exists. Phase 9 revisits
every row and checks that each mitigation has a test or a documented reason.

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
| D | Another process holds the clipboard open, or floods it | Retry with backoff, then give up; one event per change by sequence number | 3 | Fake-surface tests |
| S | A process impersonates another to dodge the exclusion list | Exclusion matches the owning process's executable name; the source name is advisory | 3 | Integration tests |
| E | Capture needs elevation | None: `asInvoker`, never elevate | 0 | Manifest test (done) |

## 2. Storage (SQLite and crypto)

| STRIDE | Threat | Mitigation | Phase | Evidence |
|---|---|---|---|---|
| I | Reading the history database (`clipbench.db`) from disk or a backup | AES-256-GCM per value; key wrapped by DPAPI | 2 | Plaintext leak test over the db, `-wal` and `-shm` |
| T | Swapping or editing ciphertext | GCM tag plus associated data (row id, column); failure is closed | 2 | Tamper tests on every byte |
| I | Guessing a clip by comparing hashes | Keyed HMAC for duplicate detection | 2 | Different keys give different hashes |
| I | Content in logs, crash dumps or temp files | Logs hold event types, sizes and ids only; no minidumps with clipboard memory | 4, 9 | Log marker test; audit |
| I | `key.bin` readable by other users | Inherits the user-only ACL of `%LOCALAPPDATA%` | 2 | Review |
| E | SQL injection | Parameterized SQL only | 2 | Review and tests |
| D | Corrupt key file or database | Clear, recoverable error and an offer to start fresh | 2 | Key management tests |
| I | Deleted rows linger in the file | `secure_delete`, `VACUUM` on wipe | 2 | Wipe test with marker bytes |

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
- How should the app behave if DPAPI is unavailable, for example for a roaming profile
  restored on a new machine? (Phase 2)
