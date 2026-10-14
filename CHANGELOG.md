# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The app shell (`ClipBench.App`, ADR 0012 to ADR 0014): the app starts with its icon in the notification
  area, watches the clipboard and keeps what is copied in the encrypted history, and a shortcut (Win+Alt+V
  by default) opens a compact popup next to the text cursor or the mouse pointer. The popup has a search box,
  rows with a two-line preview, an icon for the kind of clip, the source program and the age, pin and mark
  signs, and the key hints; it hides when it loses focus and hides itself from screenshots and screen
  sharing. Up and Down move (and wrap), Enter pastes the highlighted clip into the window that had focus,
  Escape closes, Control+P pins, Control+R shows a hidden secret, Space marks a clip and Delete removes one
  (a pinned clip asks first). Secrets (card numbers, tokens, keys) show as dots and are never read out by a
  screen reader. The tray menu offers Open, Pause or Resume capturing, and Exit; notices (a shortcut that is
  taken, a paste that could not be done) appear next to the icon, and a paste into an administrator window
  leaves the clip on the clipboard. The popup is checked by driving the real app with the keyboard (FlaUI,
  ADR 0013).
- A log of what the app does (`clipbench.log`, kept to a fixed size) that holds only event names, kinds, sizes and
  codes: its contract has no text field, so a copied text, a file name or a program name cannot get into it.
- New command-line switches for checks and tests: `--data-dir`, `--hotkey` and `--allow-screen-capture`.
  One copy runs per data folder.
- The view models of the popup and the tray (`ClipBench.Presentation`, with CommunityToolkit.Mvvm), the
  placement of the popup on the monitor, the paste flow with its plain-words notices, and the library that
  keeps the history in memory and in step with the encrypted store. CI now enforces 80% line coverage on
  `ClipBench.Presentation`.
- Windows platform layer (`ClipBench.Platform.Windows`, ADR 0010 and ADR 0011): reading the clipboard
  (text, HTML, rich text, file lists and pictures) with the privacy check made before any content is read;
  writing it back in every form a program may ask for; a listener that reports each change once and
  ignores ClipBench's own writes; global shortcuts that report a conflict by name; a paste that records the
  target, refuses a program Windows would block, brings the target to the front and checks it, waits for held
  keys, presses Ctrl+V and puts the user's clipboard back without overwriting a newer copy; and one running
  copy per signed-in user. The master key is wrapped with the Windows data protection API for the current
  user. Pictures are converted between Windows bitmaps and PNG by ClipBench's own reader and writer, checked
  against Windows' own decoder. Architecture tests forbid key hooks, keyboard readers beyond the modifier
  keys, program launchers, privilege changes and access to other processes' memory.
- Encrypted history storage (`ClipBench.Storage`): clip content, previews and source app
  names are sealed with AES-256-GCM, each tied to its clip, column and format, so a value
  that was moved or changed is refused and never returned. The history is kept in SQLite
  (write-ahead logging, secure delete) with a key check that spots a wrong key at start-up,
  atomic schema migrations, retention by count, age and total size that follows the same
  rules as the in-memory history, expiry, and a wipe that overwrites stored values before the
  files are deleted. A key or database that cannot be used is reported and left untouched,
  and starting fresh is a separate step. Tests read every byte on disk to prove nothing
  readable is stored, and the SQL is constants with named parameters, checked by the compiler
  and by a source scan. The Windows binding for the key (DPAPI) follows at the start of
  Phase 3 (ADR 0009).
- The in-memory history now also limits the unpinned clips to 256 MiB together
  (`HistoryOptions.MaxTotalBytes`); the newest clip is always kept and pinned clips are exempt.
- Core domain (`ClipBench.Core`, no Windows code): the `Clip` model with text, HTML, RTF,
  image and file-list content and previews that never split a character; the clip history
  with duplicate handling, pinning and retention by count and age; ranked substring and fuzzy
  search that handles Arabic, Hebrew, accents and emoji; batch and table paste composition;
  the queue paste state machine; collect sessions; the capture filter (privacy markers,
  excluded applications, incognito, size limits); and a detector for card numbers, IBANs,
  JSON web tokens, private keys and random-looking tokens.
- CI now enforces 90% line coverage on `ClipBench.Core` and 85% on `ClipBench.Storage`, and
  fails a coverage run that measured no lines (an empty assembly reports 100%).
- Solution skeleton for .NET 10: `ClipBench.Core`, `ClipBench.Storage`,
  `ClipBench.Platform.Windows`, `ClipBench.Presentation` and a minimal unpackaged,
  self-contained WinUI 3 app on Windows App SDK 2.5.1, for x64 and ARM64.
- Test stack: xUnit v3 on Microsoft.Testing.Platform, `coverlet.MTP` coverage and
  ArchUnitNET, with a smoke test in every test project.
- Architecture and repository guardrail tests: the allowed project and package
  references, no networking types in any source assembly, the app manifest
  (`asInvoker`, PerMonitorV2), central package versions with lock files, and the
  workflow rules.
- Continuous integration: build and test with warnings as errors, per-project
  coverage, formatting and vulnerable-package checks, CodeQL, dependency review,
  OpenSSF Scorecard and Dependabot. Every action is pinned by commit SHA.
- An attribution guard that checks commit messages, author and committer identities,
  and pull request titles and bodies.
- Community files: security policy, contributing guide, code of conduct, issue forms
  and a pull request template.
- Architecture decision records 0001 to 0009, a glossary, a roadmap and a first
  draft of the threat model.

### Changed

- Dependabot now updates the three CodeQL action steps together in one pull request, because
  the action fails when its steps run different versions. All of them are on 4.38.3, and a test
  keeps it that way.
