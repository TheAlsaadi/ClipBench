# ClipBench — Implementation Plan

This is the complete build plan for ClipBench, an advanced, security-first clipboard manager for Windows 11. It is written for Claude Code to execute phase by phase. **`CLAUDE.md` contains the non-negotiable rules** (no AI attribution, TDD, security baseline, workflow, Phase Gate). They apply to every step below.

---

## 0. How to use this plan (for Claude Code)

1. Read `CLAUDE.md`, then this file, completely.
2. Run `/skills` and tell the user which skills from the CLAUDE.md skills map are installed and which are missing.
3. Execute **one phase at a time**. For each phase:
   - Create branch `phase-NN-name` (use `using-git-worktrees` if available).
   - Use `writing-plans` to expand the phase into small tasks (2–15 minutes each). Each task names the test to write first. Show the user the task list.
   - Execute with `subagent-driven-development` (or `executing-plans`), applying `test-driven-development` to every task.
   - Run the **Phase Gate** from `CLAUDE.md`.
   - Stop and wait for the user.
4. In each phase below, "Tests first" lists the behaviors to cover **before** writing the implementation. Expand them into concrete test cases during `writing-plans`.

---

## 1. Product scope

### 1.1 Vision

ClipBench replaces "one item at a time" pasting with a workbench. Everything you copy is captured into an encrypted local history. A keyboard-first popup lets you search, multi-select, transform, and paste several items in one action.

### 1.2 Feature list by release

**v0.1 — MVP (Phases 0–5)**
- Background capture of text, rich text (HTML/RTF), images, and file lists
- Encrypted local history with a configurable size and age limit
- Popup at the cursor via a global hotkey, with instant fuzzy search
- Keyboard navigation and multi-select
- **Batch paste**: select several clips and paste them in one action, joined by a separator (new line, comma, tab, space, or custom)
- **Queue paste**: load a queue, then each Ctrl+V pastes the next item, for form filling
- **Collect mode**: each copy appends to a single growing clip
- **Table paste**: paste the selection as TSV rows or columns for Excel and Sheets
- Pinning
- Tray icon, a pause/incognito toggle, and basic settings

**v0.2 (Phases 6–8)**
- Paste-time transforms: plain text, case changes, trim, removing line breaks, JSON format/minify, URL and Base64 encode/decode, find and replace
- Boards (named groups of pinned clips)
- Snippets with variables (`{date}`, `{time}`, `{clipboard}`, `{input:Name}`)
- Privacy center: app exclusion list, sensitive-clip detection with auto-expiry, wipe all
- Local OCR: select a screen region and copy its text (Windows.Media.Ocr)
- Smart detection: color swatches for hex/RGB values, link titles from clip text only (no fetching), code detection

**v1.0 (Phases 9–10)**
- Security hardening pass and threat-model review
- Signed installer `.exe` and portable `.zip` for x64 and ARM64
- Reproducible CI release with checksums, SBOM, and build provenance

**Out of scope for v1** (record as ideas in `docs/ROADMAP.md`): cloud sync, plugins, update checks, and replacing Win+V.

### 1.3 Platform

- Primary target: Windows 11 (x64 and ARM64).
- Minimum: Windows 10 22H2 (best effort; document any gaps).
- The app runs per-user as a normal (non-admin) process.

---

## 2. Architecture

### 2.1 Solution layout

```
ClipBench/
├─ .claude/settings.json            ← provided by the owner; do not modify attribution
├─ .githooks/commit-msg             ← provided by the owner; strips AI attribution lines
├─ .github/
│  ├─ workflows/ci.yml, codeql.yml, release.yml, scorecard.yml
│  ├─ dependabot.yml
│  ├─ ISSUE_TEMPLATE/ (bug, feature), PULL_REQUEST_TEMPLATE.md
├─ docs/
│  ├─ adr/                           ← Architecture Decision Records (0001-…)
│  ├─ security/THREAT_MODEL.md, PRIVACY.md
│  ├─ ROADMAP.md, ARCHITECTURE.md, MANUAL_QA.md
├─ installer/ClipBench.iss
├─ scripts/attribution-guard.sh, tests/test-attribution-guard.sh
├─ src/
│  ├─ ClipBench.Core/                 net10.0 — domain, no Windows/UI refs
│  ├─ ClipBench.Storage/              net10.0 — SQLite repo + crypto (key protection via interface)
│  ├─ ClipBench.Platform.Windows/     net10.0-windows10.0.19041.0 — clipboard, hotkeys, paste, DPAPI, OCR
│  ├─ ClipBench.Presentation/         net10.0 — ViewModels (CommunityToolkit.Mvvm), no WinUI refs
│  └─ ClipBench.App/                  WinUI 3 app — views (XAML), tray, composition root
├─ tests/
│  ├─ ClipBench.Core.Tests/
│  ├─ ClipBench.Storage.Tests/
│  ├─ ClipBench.Platform.Windows.IntegrationTests/
│  ├─ ClipBench.Presentation.Tests/   view-model tests
│  ├─ ClipBench.UiSmokeTests/         FlaUI
│  └─ ClipBench.ArchitectureTests/
├─ Directory.Build.props, Directory.Packages.props, global.json, nuget.config
├─ .editorconfig, .gitignore, .gitattributes
├─ CLAUDE.md, PLAN.md, README.md, LICENSE (MIT), SECURITY.md, CONTRIBUTING.md
└─ CODE_OF_CONDUCT.md, CHANGELOG.md, GLOSSARY.md
```

### 2.2 Dependency rules (enforced by ArchitectureTests)

```
App ──► Presentation ──► Core
 ├────► Platform.Windows ──► Core
 └────► Storage ───────────► Core
Core ──► (nothing but the BCL)
```

- Core references no `Microsoft.UI.*`, `Windows.*`, `Microsoft.Data.Sqlite`, or `System.Net.*`.
- Presentation references only Core and CommunityToolkit.Mvvm. It has no `Microsoft.UI.*` references, so ViewModels are testable without a UI thread. It talks to platform services only through Core interfaces.
- No `src/` assembly references `System.Net.Http`, `System.Net.Sockets`, or `System.Net.WebClient`.
- Storage does not reference Platform.Windows. It receives `IKeyProtector` by injection.

### 2.3 Key components

| Component | Layer | Responsibility |
|---|---|---|
| `Clip`, `ClipContent`, `ClipFormat` | Core | Immutable models: text, HTML, RTF, image (PNG bytes), file list (paths only) |
| `ClipHistory` | Core | Add, dedupe (content hash), pin, remove, retention (count/age), ordering |
| `ClipSearch` | Core | Fuzzy and substring search over an in-memory decrypted index |
| `PasteComposer` | Core | Batch join with separators, table (TSV) building, transform pipeline |
| `PasteQueue` | Core | Queue state machine: Idle → Armed → Pasting → Exhausted/Cancelled |
| `CollectSession` | Core | Appends captures into one clip with a separator |
| `Transforms` | Core | Pure `string → string` functions with a registry |
| `SnippetEngine` | Core | Variable parsing and expansion with an injected `TimeProvider` |
| `SensitiveDetector` | Core | Heuristics (Luhn card numbers, IBAN, JWT, PEM private keys, high-entropy tokens) |
| `CaptureFilter` | Core | Decides whether a capture is recorded (privacy markers, excluded apps, incognito, size caps) |
| `ICryptoService` / `AesGcmCryptoService` | Storage | AES-256-GCM encryption with a per-record nonce; AAD = record ID + field name |
| `IKeyProtector` | Core (interface) | Wraps and unwraps the master key; `DpapiKeyProtector` lives in Platform.Windows |
| `ClipRepository` | Storage | SQLite persistence, migrations, expiry sweeps, secure wipe |
| `ClipboardListener` | Platform | `AddClipboardFormatListener` on a message-only window; raises events on `WM_CLIPBOARDUPDATE` |
| `ClipboardReader` / `ClipboardWriter` | Platform | Read/write supported formats with size caps; mark own writes |
| `PasteInjector` | Platform | Restore focus to the target window, set clipboard, `SendInput` Ctrl+V, optionally restore the previous clipboard |
| `HotkeyService` | Platform | `RegisterHotKey` and conflict detection |
| `ForegroundTracker` | Platform | Records the target window before the popup opens; detects elevated targets (UIPI) |
| `OcrService` | Platform | Screen region capture and `Windows.Media.Ocr` |
| `TrayIcon` | App | `Shell_NotifyIcon` via CsWin32; menu: Open, Pause, Settings, Exit |
| `PopupViewModel`, `TrayViewModel`, `QueueHudViewModel`, `SettingsViewModel` | Presentation | All UI state and commands; unit-tested |
| `PopupWindow` and other views | App | XAML bound to the ViewModels; no logic beyond view plumbing |

### 2.4 Data and crypto design

- **Master key**: 32 random bytes from `RandomNumberGenerator`, wrapped with DPAPI (`CurrentUser` scope plus app-specific entropy) and stored in `%LOCALAPPDATA%\ClipBench\key.bin`.
- **Database**: `%LOCALAPPDATA%\ClipBench\clipbench.db` (SQLite, WAL mode, `secure_delete=ON`).
- **Encrypted columns**: content blobs, preview text, and source app name. Each is stored as `nonce(12) || ciphertext || tag(16)`. The AAD binds each value to its row ID and column name, so ciphertexts cannot be swapped between rows.
- **Plaintext columns**: ID, created/expiry timestamps, format type, pinned flag, board ID, size, and content hash. The hash is HMAC-SHA256 keyed with a key derived from the master key (HKDF), not a plain hash, so it can't be used to confirm guessed content.
- **Search**: content is decrypted into an in-memory index at startup. Default history cap is 1,000 items and 30 days (configurable). The index is cleared on exit and on "wipe all".
- **Honest threat model** (document it in `THREAT_MODEL.md`): this design protects history against other Windows users, offline disk access, backups, and a stolen disk while the user is logged out. It **does not** protect against malware running as the same user, which can read the clipboard directly anyway. Timestamps and item counts are visible metadata.

### 2.5 Paste mechanics

1. When the hotkey fires, `ForegroundTracker` records the target window `HWND` before the popup takes focus.
2. The user chooses clips and an action. The popup hides.
3. `PasteInjector` restores focus to the target and writes the composed content to the clipboard, tagged with the private format `ClipBench.Internal` so the listener ignores it. It waits until the clipboard is set, then sends Ctrl+V via `SendInput`.
4. After a configurable delay (default 250 ms), it optionally restores the user's previous clipboard.
5. If the target is elevated and ClipBench is not, Windows blocks the input (UIPI). Detect this and show a notification instead of failing silently.

### 2.6 Queue mode mechanics (no keyboard hooks)

- When the user arms a queue, ClipBench registers **Ctrl+V** as a temporary global hotkey with `RegisterHotKey`.
- On each press: unregister Ctrl+V, write the next queue item, send Ctrl+V with `SendInput`, then re-register. The synthetic keypress passes through to the target app.
- A small always-on-top HUD shows "Queue 2/5 — Esc to stop".
- When the queue is exhausted or cancelled (Esc hotkey or tray), unregister everything and return to idle.
- `PasteQueue` (Core) holds the state machine, so it is fully unit-testable. The platform layer only executes its commands.

---

## 3. Phases

Each phase lists: **Goal**, **Skills**, **Tests first**, **Implementation**, **Acceptance**, and **Security checks**. The Phase Gate from `CLAUDE.md` always applies at the end.

---

### Phase 0 — Repository bootstrap and guardrails

**Goal**: An empty but fully guarded solution: builds, tests, CI, security tooling, and community files, with no features yet.

**Skills**: `brainstorming` (confirm the working name and license with the user), `writing-plans`, `domain-modeling` (start `GLOSSARY.md` and ADRs), `verification-before-completion`.

**Tests first**
- ArchitectureTests: Core has no forbidden references; Presentation has no `Microsoft.UI.*` references; no `src/` assembly references networking types; Storage does not reference Platform.Windows. Write these against empty projects first; they should pass, then guard everything that follows.
- A trivial "smoke" test in each test project to prove the test runner and coverage collection work in CI.

**Implementation**
1. Confirm with the user: the project name (check that it's not taken on GitHub), MIT license, and the owner's GitHub username for the README and SECURITY contact.
2. Check `git config user.name` and `user.email` *by asking the user*, not by changing them. The user confirms the identity that will author all commits.
3. **Repository (already created by the owner).** The owner created the public GitHub repository (public, so SignPath and free Actions minutes apply), its initial commit (`README.md` only), the local clone, and `git config core.hooksPath .githooks`. Do **not** run `git init` or make a separate initial commit. Confirm with `git config core.hooksPath` and `git remote -v`. The bootstrap files arrive through the `phase-00-bootstrap` pull request, and all work happens on phase branches. Push a branch only after the user approves. (Recorded in ADR 0005.)
4. **Test the attribution safety net.** Create branch `phase-00-bootstrap`. On it, make a throwaway commit whose message contains a `Co-Authored-By: Claude <noreply@anthropic.com>` line and a `Claude-Session:` line. Confirm the hook removed both lines, then drop that commit (`git reset --hard HEAD~1`, with the user's approval).
5. Create `global.json` (pin the .NET 10 SDK feature band), `Directory.Build.props` (Nullable, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `Deterministic`, `ContinuousIntegrationBuild` in CI, `EnableNETAnalyzers`, `<Authors>` = owner's name), `Directory.Packages.props` (central package management, `RestorePackagesWithLockFile=true`), and `nuget.config` (`nuget.org` only, with package source mapping).
6. Create all `src/` and `tests/` projects from §2.1. The App project should be a minimal WinUI 3 unpackaged app (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `Platforms=x64;ARM64`, `app.manifest` with `asInvoker` and PerMonitorV2 DPI awareness).
7. Add `.editorconfig`, `.gitignore` (VisualStudio template plus `*.pfx`, `*.key`, `.env*`), and `.gitattributes` (keep `.githooks/*` with LF line endings so the hook runs under Git for Windows).
8. CI workflow `ci.yml` on `windows-latest`. Pin every action by full commit SHA. Set `permissions: contents: read` and `concurrency` to cancel superseded runs. Steps: setup-dotnet from `global.json` → `restore --locked-mode` → `build -warnaserror` → unit and architecture tests with coverage → enforce coverage floors → `format --verify-no-changes` → vulnerable-package check.
9. **Attribution guard job** in `ci.yml` (runs on every push and PR, on `ubuntu-latest` for speed): scan every commit message in the push or PR range (`git log --format=%B base..head`), plus the PR title and body (from the event payload, passed through an environment variable, never interpolated into the script), for these case-insensitive patterns: `Co-Authored-By:.*(claude|anthropic)`, `Claude-Session:`, `Generated with \[?Claude Code`, `claude\.ai/code/session_`. Fail with a clear message if any match. Make it a required check on `main`.
10. `codeql.yml` (C#), the `dependency-review-action` on PRs, `dependabot.yml` (NuGet and GitHub Actions, weekly), and `scorecard.yml` (OpenSSF Scorecard).
11. Community files: README skeleton (vision, status badge, "no telemetry, no network" promise), `SECURITY.md` (GitHub private vulnerability reporting), `CONTRIBUTING.md` (TDD, Conventional Commits), `CODE_OF_CONDUCT.md` (Contributor Covenant), `CHANGELOG.md` (Keep a Changelog format).
12. ADRs: `0001-tech-stack.md`, `0002-no-network-no-hooks.md`, `0003-encryption-at-rest.md`.
13. **Hotkey spike** (`0004-default-hotkey.md`): write a tiny throwaway console probe that calls `RegisterHotKey` for these candidates on Windows 11: Win+Alt+V, Ctrl+Alt+Space, Ctrl+Shift+Space, Win+Shift+V. Record which are free. Note that Ctrl+Shift+V is "paste as plain text" in browsers and many editors, so it must not be the default. Pick the default with the user. It is always configurable.
14. `docs/security/THREAT_MODEL.md` first draft (STRIDE over: capture, storage, popup, paste, OCR, installer, update/distribution).

**Acceptance**: CI (including the attribution guard) is green on a PR; `main` is protected with the CI and attribution-guard jobs as required checks (the user enables this; give them exact steps); every guardrail is in place.

**Security checks**: actions pinned by SHA; workflow permissions are minimal; there are no secrets in the repo; the attribution setting, the commit-msg hook (`core.hooksPath` set) and the CI attribution guard are all present and tested.

---

### Phase 1 — Core domain (pure C#, no Windows)

**Goal**: All clipboard logic implemented and tested without any OS dependency.

**Skills**: `writing-plans`, `test-driven-development` (strict), `subagent-driven-development`, `domain-modeling` (update the glossary: Clip, Board, Queue, Collect Session, Transform, Snippet, Capture), `requesting-code-review`.

**Tests first**
- `Clip`: equality; size calculation; previews truncated safely (no surrogate pair split); file-list clips store paths only.
- `ClipHistory`: adding a duplicate moves it to the top instead of duplicating; pinned items are never evicted; eviction by count and by age uses `TimeProvider`; remove and clear.
- `ClipSearch`: case-insensitive substring; fuzzy matching ranks prefix matches higher; Arabic and other RTL text; emoji; an empty query returns recent items; results are stable when ranks are equal.
- `PasteComposer` batch: each separator type; a custom separator with escape sequences (`\n`, `\t`); selection order is preserved; a mix of text and images is rejected with a clear result (images can't be joined into text).
- `PasteComposer` table: builds rows vs columns; escapes tabs and newlines inside cells per TSV/Excel rules; handles a single item.
- `PasteQueue` state machine: arm → next → next → exhausted; cancel at any state; next while idle is a no-op with an error result; queue from the selection preserves order; the "loop" option restarts.
- `CollectSession`: start → append ×N → finish yields one clip; empty session yields nothing; separator respected; ignores ClipBench's own writes.
- `CaptureFilter`: rejects privacy-marker formats, excluded processes (case-insensitive executable name match), incognito, and oversize content (defaults: 2 MB text, 20 MB image; configurable); accepts normal content.
- `SensitiveDetector`: valid Luhn card numbers flagged and invalid ones not; IBAN; JWT; PEM private key blocks; long high-entropy tokens; normal prose not flagged (include false-positive cases).

**Implementation**: Implement only what the tests demand. Keep types immutable. Use records. No static state.

**Acceptance**: Core coverage ≥ 90%; all ArchitectureTests are green.

**Security checks**: no parser recursion without limits; all inputs size-checked.

---

### Phase 2 — Secure storage

**Goal**: Encrypted, durable history that survives restarts and leaks no plaintext to disk.

**Skills**: `writing-plans`, `test-driven-development`, `systematic-debugging` (for any SQLite or crypto issues), `/security-review` (mandatory), `requesting-code-review`.

**Tests first**
- `AesGcmCryptoService`: encrypt/decrypt round-trip; nonce is unique per call (generate 10k and check for no collisions); tampering with any byte of the ciphertext, tag, or nonce fails authentication; wrong AAD (a different row ID or column) fails; wrong key fails.
- Key management with a fake `IKeyProtector`: first run creates and stores a wrapped key; second run unwraps the same key; a corrupted key file gives a clear, recoverable error ("history can't be decrypted — start fresh?") and never crashes.
- `ClipRepository`: save/load round-trip for every format; pin and unpin; expiry sweep deletes expired items; retention trims to the cap while keeping pinned items; migrations from an empty DB to the current schema; concurrent writes are serialized safely.
- **Plaintext leak test**: save clips containing a unique marker string, close the DB, and read the raw bytes of `clipbench.db`, `-wal`, and `-shm`. Assert the marker (in UTF-8 and UTF-16) appears nowhere.
- Wipe all: deletes all rows, runs `VACUUM`, deletes the key file, and leaves no marker bytes behind.
- Content hash is a keyed HMAC: the same content under different master keys gives different hashes.

**Implementation**: Microsoft.Data.Sqlite with parameterized SQL only (no string concatenation). Schema versioning table. `PRAGMA secure_delete=ON`, WAL mode. `System.Security.Cryptography.AesGcm`. HKDF to derive separate subkeys for encryption and HMAC from the master key. In Platform.Windows, implement `DpapiKeyProtector` (with an integration test that runs on Windows only).

**Acceptance**: Storage coverage ≥ 85%; the leak test is green; the security review is clean.

**Security checks**: no SQL injection surface; key material is never logged; key file permissions inherit from `%LOCALAPPDATA%` (user-only); crypto failures fail closed.

---

### Phase 3 — Windows platform layer

**Goal**: Real clipboard capture, writing, hotkeys, and paste injection behind the Core interfaces.

**Skills**: `writing-plans`, `test-driven-development` (integration tests for real OS behavior, unit tests for the logic around it), `systematic-debugging` (clipboard timing bugs are common; reproduce first), `/security-review`.

**Tests first**
- Integration (`Category=Integration`, Windows only): writing text and reading it back; HTML Format and RTF round-trip; image round-trip (DIB ↔ PNG); file list (CF_HDROP) paths; data written with `ExcludeClipboardContentFromMonitorProcessing` is reported as excluded; ClipBench's own writes (tagged `ClipBench.Internal`) are ignored by the listener; the listener raises exactly one event per change (sequence-number dedupe).
- Unit (with fakes): clipboard-open retry with backoff when another process holds the clipboard (`OpenClipboard` fails); source-process resolution via `GetClipboardOwner` → `GetWindowThreadProcessId`, failing gracefully; the HTML Format header parser handles malformed offsets and oversized input; `HotkeyService` reports conflicts when `RegisterHotKey` fails; the `PasteInjector` command sequence is correct (focus → set clipboard → send keys → optional restore) using a fake Win32 surface.

**Implementation**
- CsWin32 `NativeMethods.txt` lists only the APIs needed.
- A message-only window plus `AddClipboardFormatListener` for capture. Do not use the legacy clipboard viewer chain.
- Supported formats: `CF_UNICODETEXT`, `HTML Format`, `Rich Text Format`, `CF_DIB`/`CF_DIBV5` → PNG, `CF_HDROP`.
- `SendInput` for Ctrl+V. Detect UIPI blocking: compare the integrity level of the target process with our own.
- A single-instance guard: a named mutex scoped to the user session; a second launch signals the first to open the popup.

**Acceptance**: All integration tests pass locally on Windows 11. Document any that can't run on CI runners and mark them `Skip` *only on CI* with a reason.

**Security checks**: no hooks; every parsed format is capped; no shell execution of clipboard file paths; the process name lookup never elevates.

---

### Phase 4 — App shell, tray, and popup UI

**Goal**: A usable app: tray icon, global hotkey, popup at cursor with search, list, previews, and single-item paste.

**Skills**: `writing-plans`, `frontend-design` (visual direction that is native to Fluent and Windows 11, not a web look), `test-driven-development` (ViewModels), `brainstorming` (popup layout and keyboard model; confirm with the user before building), `/code-review`.

**Tests first** (ViewModel tests in `ClipBench.Presentation.Tests`, no UI thread needed)
- `PopupViewModel`: typing filters results with a debounce (via an injected scheduler); Up/Down moves the selection and wraps; Space toggles multi-select; Enter pastes the selection; Esc closes and clears state; Ctrl+P pins; Delete removes (pinned items require confirmation); an empty state is shown when there are no clips; the empty search state shows a hint.
- Previews: text is truncated to N lines; images show a thumbnail; sensitive items are masked (`••••`) until revealed.
- `TrayViewModel`: the Pause toggle updates the capture filter; Exit disposes services cleanly.
- UI smoke test (FlaUI): the app starts, the hotkey opens the popup, typing filters, and Esc closes it.

**Implementation**
- The popup is a borderless, topmost WinUI 3 window positioned at the caret if obtainable, otherwise at the cursor. It is clamped to the current monitor's work area and is DPI-aware.
- Mica backdrop, rounded corners, light/dark following system theme, Segoe Fluent Icons.
- Accessibility: full keyboard operation, UI Automation names on all controls, visible focus, high-contrast theme support, and respect for "reduce motion".
- The tray icon is implemented through CsWin32 `Shell_NotifyIcon`, with no third-party tray package (Rule 3, dependencies).
- Composition root uses `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Hosting`.
- Logging uses `Microsoft.Extensions.Logging` with a small size-capped rolling file sink written in-house. Add a test that log output never contains clip text (use a marker string again).

**Acceptance**: The user can copy things, open the popup, search, and paste one item into Notepad, Word, a browser, and VS Code. Write `docs/MANUAL_QA.md` with a checklist.

**Security checks**: the window does not appear in screen captures when "hide from screen capture" is on (`SetWindowDisplayAffinity`, a setting defaulting to on); sensitive previews are masked.

---

### Phase 5 — Multi-paste features (the headline)

**Goal**: Batch, queue, collect, and table paste working end to end.

**Skills**: `writing-plans`, `test-driven-development`, `subagent-driven-development` (the four features are independent tasks), `systematic-debugging`, `requesting-code-review`, `/security-review`.

**Tests first**
- Batch: the ViewModel builds the correct `PasteComposer` request from the selection order and chosen separator; the separator picker remembers the last choice.
- Queue: the ViewModel arms `PasteQueue`; the HUD ViewModel shows "n/m"; Esc cancels; the queue unregisters the Ctrl+V hotkey on exhaustion, cancel, app exit, and crash paths (test the dispose path).
- Collect: the tray/popup toggle starts and stops a session; captures during the session are appended and not added as separate items (configurable); finishing produces one clip.
- Table: choosing rows vs columns produces the expected TSV.
- Integration: queue mode pastes 3 items into a test WinUI/Win32 text box in order (UI smoke test).

**Implementation**: Follow §2.5 and §2.6. Add a settings toggle for "restore previous clipboard after paste" (default on). Add a separator picker UI in the popup footer: `[Paste ⏎] [Paste all ▾ separator] [Queue] [Table ▾]`.

**Acceptance**: The demo scenarios from `MANUAL_QA.md` all pass:
- 5 emails become one comma-separated list in a mail "To" field.
- A 4-field web form is filled using queue mode.
- 10 quotes are collected from an article and pasted as one block.
- 6 values become a column in Excel.

**Release**: Tag **v0.1.0** after the user approves. (The Phase 10 release pipeline can be pulled forward in a minimal form here if the user wants an early unsigned preview; ask.)

---

### Phase 6 — Transforms, boards, and snippets

**Goal**: Power features that make every paste smarter.

**Skills**: `writing-plans`, `test-driven-development`, `brainstorming` (snippet syntax; confirm with the user), `/code-review`.

**Tests first**
- Every transform has table-driven tests, including edge cases: empty string; Unicode (Arabic, combining marks, emoji, Turkish dotted/dotless i for case transforms with `CultureInfo.InvariantCulture` vs current culture, documented); very large input; invalid JSON or Base64 returns an error result instead of throwing; find/replace with regex has a timeout (`Regex` match timeout) to prevent ReDoS.
- Transforms chain in order; "paste as plain text" strips HTML/RTF and keeps the text.
- Boards: create, rename, delete (with confirmation), add/remove clips, and reorder. Board membership persists (Storage tests).
- `SnippetEngine`: `{date:yyyy-MM-dd}`, `{time}`, `{clipboard}`, `{input:Name}` prompts; escaped braces `{{ }}`; unknown variables are left as-is with a warning; recursion is impossible (variables never expand inside expanded values); expansion length is capped.

**Implementation**: A transform picker on Shift+Enter in the popup; a board sidebar; a snippet editor in settings.

**Acceptance**: The coverage floors hold, and `MANUAL_QA.md` is extended.

---

### Phase 7 — Privacy center and settings

**Goal**: Users control exactly what is captured and kept.

**Skills**: `writing-plans`, `test-driven-development`, `/security-review` (mandatory), `frontend-design`.

**Tests first**
- Settings model: defaults; validation (ranges for history size, age, and delays); JSON round-trip; unknown keys ignored; a corrupted file falls back to defaults with a backup of the bad file.
- Exclusions: the default list (KeePass, KeePassXC, 1Password, Bitwarden, Dashlane, Windows Credential UI, and others; review the list with the user); add and remove; matching by executable name.
- Sensitive clips: detected items get a short expiry (default 2 minutes, configurable) and are masked in the UI; the expiry sweep removes them from memory and disk.
- Incognito: it stops capture until turned off; the tray icon shows the state.
- Start with Windows: writes and removes the `HKCU\…\Run` entry (behind an interface; fake in tests). It is **off by default** and asked during first run.
- Wipe all: confirmation, then everything is deleted (reuses the Phase 2 wipe and clears the in-memory index).

**Implementation**: A Settings window with sections: General, Hotkeys, History, Privacy, Paste, Snippets, About. The About section shows the version, license, the "no network" promise, and a link to the repo.

**Acceptance**: Every privacy setting has a test and a QA checklist item. Update `docs/security/PRIVACY.md`.

---

### Phase 8 — OCR and smart detection

**Goal**: Copy text from anything on screen, with clips that understand their content.

**Skills**: `writing-plans`, `test-driven-development`, `systematic-debugging`, `/security-review`.

**Tests first**
- Region selection math: multi-monitor, mixed DPI, and negative coordinates (pure functions).
- The OCR service returns text for a bundled test image (integration test; supply a generated PNG with known text).
- When an OCR language pack is missing, show a clear message.
- Detectors: hex/RGB/HSL colors produce a swatch model; URLs are detected and shown as links **without fetching**; code is detected with a simple heuristic and a language guess; phone and email detection.

**Implementation**: An overlay window for region selection (Esc cancels). Use `Windows.Graphics.Capture` or GDI capture of the selected region **only**, processed in memory with `Windows.Media.Ocr`. The image is never saved to disk unless the user pins the result.

**Acceptance**: The coverage floors hold, and `MANUAL_QA.md` is extended. Tag **v0.2.0** after the user approves.

---

### Phase 9 — Security hardening and quality pass

**Goal**: Make v1.0 trustworthy.

**Skills**: `/security-review` (whole codebase, not only the diff), `/code-review high`, `improve-codebase-architecture`, `systematic-debugging`, `thermo-nuclear-code-quality-review` if installed, `verification-before-completion`.

**Tasks** (each with tests where applicable)
1. Update the threat model with everything built; confirm each mitigation has a test or documented reason.
2. Fuzz-style property tests (FsCheck or a hand-rolled generator) for the HTML Format parser, RTF handling, snippet parser, and TSV builder. They must never throw unexpected exceptions or exceed caps.
3. Mutation testing with Stryker.NET on Core; kill surviving mutants in critical code (`CaptureFilter`, `PasteQueue`, crypto wrappers).
4. Resource tests: 10,000-item history, startup time under 1 s on a mid-range PC, popup open under 100 ms, memory budget documented.
5. Dependency audit: licenses (all must be MIT/Apache/BSD-compatible), maintainers, transitive vulnerabilities.
6. Logging audit: grep the codebase for any log call that could include content.
7. Crash handling: unhandled exceptions are logged (without content), the user is notified, and no minidumps are written containing clipboard memory. Opt-in "save diagnostic report" redacts content.
8. Review the `SECURITY.md` disclosure process and enable GitHub private vulnerability reporting (the user does this; give steps).

**Acceptance**: Zero high or medium findings open. All checks green.

---

### Phase 10 — Packaging, signing, and release

**Goal**: A trustworthy `.exe` that users can verify.

**Skills**: `writing-plans`, `verification-before-completion`, `/security-review`, `finishing-a-development-branch`.

**Tests first**
- A packaging smoke test in CI: install the produced installer silently on the runner, launch the app, verify the process starts and the tray icon registers, then uninstall silently and verify no files remain in the install directory (per-user default: `%LOCALAPPDATA%\Programs\ClipBench`). User data in `%LOCALAPPDATA%\ClipBench` is kept unless the user chooses to remove it.

**Implementation**
1. `dotnet publish` self-contained for `win-x64` and `win-arm64` (WinUI 3 + Windows App SDK self-contained, ReadyToRun on). Evaluate single-file publish. Use it only if WinUI resources work reliably; otherwise ship the folder inside the installer. Record the decision in an ADR.
2. Inno Setup script `installer/ClipBench.iss`: per-user install by default (no admin), Start Menu shortcut, optional "start with Windows", a clean uninstaller with an option to delete user data, and the publisher set to the owner's name.
3. Portable `.zip` of the publish folder.
4. `release.yml` on tag `v*`: build → full tests → publish both architectures → build the installer → generate `SHA256SUMS.txt` → generate an SBOM (Microsoft `sbom-tool`, SPDX) → `actions/attest-build-provenance` for every artifact → sign (step below) → create a **draft** GitHub Release. The owner reviews and publishes it manually.
5. **Code signing**: apply to the SignPath Foundation free OSS program (requires an OSI license, a public repo, and automated builds; the user applies). Until approved, the signing step is skipped when its secrets are absent, and the README explains the SmartScreen warning and how to verify checksums and attestations (`gh attestation verify`).
6. README final version: screenshots or GIFs, features, install (installer/portable), verification steps, privacy promise, keyboard shortcuts, FAQ, and a contributing link. **No AI attribution anywhere.**

**Acceptance**: The draft release contains: `ClipBench-Setup-x64.exe`, `ClipBench-Setup-arm64.exe`, `ClipBench-portable-x64.zip`, `ClipBench-portable-arm64.zip`, `SHA256SUMS.txt`, and the SBOM, with provenance attestations. The user tags **v1.0.0**.

---

## 4. Security checklist (cross-phase reference)

| # | Control | Phase | Verified by |
|---|---|---|---|
| S1 | No network APIs referenced | 0+ | ArchitectureTests |
| S2 | No admin elevation (`asInvoker`) | 0 | Manifest review, installer test |
| S3 | No low-level keyboard/mouse hooks | 3+ | Code review; grep test for `SetWindowsHookEx` |
| S4 | AES-256-GCM at rest, DPAPI-wrapped key, AAD binding | 2 | Crypto tests, leak test |
| S5 | Privacy markers and excluded apps honored | 1, 3 | CaptureFilter + integration tests |
| S6 | No clip content in logs or crash data | 4, 9 | Log marker test, audit |
| S7 | Input size caps and safe parsers | 1, 3, 9 | Unit + property tests |
| S8 | Regex timeouts (no ReDoS) | 6 | Transform tests |
| S9 | Sensitive clips masked and auto-expired | 1, 7 | Detector + expiry tests |
| S10 | Popup hidden from screen capture (setting) | 4 | Manual QA |
| S11 | Locked dependencies, `nuget.org` source mapping, vulnerability scan | 0+ | CI |
| S12 | Actions pinned by SHA, minimal permissions, CodeQL, Scorecard | 0 | CI |
| S13 | Signed binaries, checksums, SBOM, provenance | 10 | Release workflow |
| S14 | No AI attribution in history or artifacts | all | Rule 1, `attribution` settings, commit-msg hook, CI attribution guard |

---

## 5. Testing strategy summary

| Layer | Tool | Runs in CI | Notes |
|---|---|---|---|
| Core unit | xUnit v3 + Shouldly | ✅ | Fast, deterministic, ≥ 90% coverage |
| Storage | xUnit + temp dirs | ✅ | Real SQLite, fake key protector |
| Platform integration | xUnit, `Category=Integration` | ⚠️ where the runner allows | Real clipboard; document skips |
| ViewModels (`ClipBench.Presentation`) | xUnit + NSubstitute | ✅ | No UI thread, ≥ 80% coverage |
| Architecture | NetArchTest | ✅ | Guards layering and no-network |
| UI smoke | FlaUI | Local, CI if stable | Popup, queue paste |
| Property/fuzz | FsCheck | ✅ | Parsers and composers |
| Mutation | Stryker.NET | Manual / nightly | Core only |
| Packaging | Silent install/uninstall | ✅ release | Phase 10 |

---

## 6. Prerequisites for the owner (do these before Phase 0)

- Windows 11 machine with Visual Studio 2026 (required for .NET 10 and C# 14) including the **.NET desktop development** and **WinUI application development** workloads, or the .NET 10 SDK plus the Windows App SDK build tools.
- Git configured with **your** name and email (`git config --global user.name` / `user.email`).
- GitHub CLI (`gh`) authenticated as you.
- Claude Code running on that Windows machine (WinUI's XAML compiler only runs on Windows).
- The Superpowers plugin installed in Claude Code for the workflow skills: run `/plugin marketplace add obra/superpowers-marketplace`, then `/plugin install superpowers@superpowers-marketplace`.
- `CLAUDE.md` and `PLAN.md` in the repository root, `settings.json` placed at `.claude/settings.json`, and `commit-msg` placed at `.githooks/commit-msg`, **before** the first Claude Code session, so that even the first commit has no attribution. Claude Code turns the hook on in Phase 0 with `git config core.hooksPath .githooks`.

## 7. Kick-off prompt (paste into Claude Code)

> Read CLAUDE.md and PLAN.md completely. Then do section 0 of PLAN.md: list which skills from the skills map are installed, and start Phase 0 by confirming the project name, license and my git identity with me. Do not write any code until I approve the Phase 0 task list.
