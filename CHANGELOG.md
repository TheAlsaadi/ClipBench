# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Transforms, boards and snippets (ADR 0016). **Transforms**: Shift+Enter in the popup, or the Transform button,
  opens a picker over the list. Type to narrow it, move with the arrow keys, and Enter pastes the highlighted
  transform applied to the text that would have been pasted (the highlighted clip, or the marked clips joined by
  the separator). Ctrl+Space adds a transform to a chain and Backspace takes the last one off, so "Trim, then
  Title Case" is one paste. A preview shows the result before it is pasted, and is hidden for a secret. The
  transforms are plain text (drops formatting), UPPER, lower, Title and Sentence case, trim, remove line breaks,
  JSON format and minify, URL and Base64 encode and decode, and find and replace. A transform never changes the
  stored clip, and text it cannot handle (invalid JSON, a broken escape, a bad pattern) gives a message and
  pastes nothing. Case changes use the invariant culture, so they do not depend on the language of Windows.
  JSON is copied token by token, so numbers and escapes are not changed. Find and replace takes plain text or a
  pattern; a pattern runs on the non-backtracking engine where it can, with a one-second limit, so it cannot
  hang the paste.
- **Boards**: Ctrl+B opens a sidebar beside the list with History, Pinned, one entry for each board and Snippets
  (Ctrl+1 to Ctrl+9 jump to an entry). Ctrl+N makes a board, F2 renames it, and Ctrl+Shift+Delete deletes it after
  a question; its clips stay, pinned. Ctrl+D (or the Board button) puts the highlighted or marked clips on a board,
  Ctrl+Shift+D takes them off, and Ctrl+Up and Ctrl+Down move a clip within its board. A clip is on at most one
  board, putting a clip on a board pins it, and unpinning a clip takes it off. Board names are sealed like clip
  content, and the database moved to schema step 2 (boards and snippets tables, and the board and place on each
  clip) without touching the saved clips.
- **Snippets**: reusable text with variables. `{date}` and `{time}` (or `{date:dd/MM/yyyy}` and any .NET custom
  format), `{clipboard}` (the text of the newest text clip in the history), `{input:Name}` (asks for a value
  before pasting) and `{{` and `}}` for the braces themselves. Anything else in braces is pasted as typed, and
  the editor warns about it. A snippet is read once from left to right and what is put in is never read again,
  with limits on its length, the values it asks for and the result. The Snippets view of the popup lists them,
  searches by name and text, and Enter pastes one, with a small form first when it asks for values. Snippets are
  written in a Snippets window (tray menu, or Ctrl+E in the popup) with live warnings and a preview. Names and
  texts are sealed in the database, and the log records only that a snippet was pasted, how many values it asked
  for and how big it was.

- **Everything can be done with the mouse**, and every key still works. A button beside the search box shows and hides
  the sidebar, and another closes the popup. While the pointer is over a clip, small buttons appear on its row to
  select it for a multi-paste, pin it, add it to a board or delete it. The right mouse button (or the Menu key, or
  Shift+F10) opens a menu of everything that can be done with the clip, with each shortcut shown. Boards have a menu
  too (rename, delete, new board), deleting one asks with a button, and the name box has Save and Cancel. The
  transform picker has Paste, Add to chain, Remove last and Back buttons.
- **Lock and move the popup.** A lock button (or Ctrl+K) keeps the popup on screen, on top of other windows, after a
  paste and when another window is used. A click on a locked popup does not take the focus from the program you are
  in, so a click on a clip pastes straight into that program, and again for the next clip. Clicking the search box, or
  opening the transform picker, a snippet prompt or a board name, takes the focus so typing works. Only the **X**
  button closes a locked popup; Esc gives the focus back to the program, and the shortcut takes the popup up again
  (pressed in the popup, it gives the focus back). The handle beside the lock button moves the popup with the mouse,
  locked or not. A popup that is closed opens unlocked, near the active window as before.

### Changed

- The popup is a little wider (440 instead of 420 device-independent pixels), and grows by 168 more while the sidebar
  is shown. The footer gains a Transform button and a Board button, and the tray menu gains Snippets.
- The messages that quote a limit write the number the same way on every machine ("10,000"), whatever the language of
  Windows.
- The popup has a thin border and rounded corners of its own that follow the theme, instead of the bright outline
  Windows drew around it.

### Fixed

- The popup lost three pixels on every side to a frame that Windows keeps around a window like it, so its contents were
  six pixels too narrow and too short: the last button of the footer and the edge of the sidebar could be cut off.
  The contents now fill the whole window.
- Clicking in the popup (the sidebar, a button, a row) took the cursor out of the search box, so typing and the arrow
  keys stopped working. The cursor now goes straight back to the box that is being typed in.
- Enter in a menu or in the box for a custom separator pasted a clip instead of choosing or saving. Keys typed in a menu
  or a flyout now belong to it.
- A snippet that asked for values, or a transform, could paste nowhere: the popup moved the cursor in its own hidden
  window as it closed and took the focus from the program being pasted into.

## [0.1.0] - 2026-10-23

The first tag: a source tag only, with no installer or download (see ADR 0015 and the release plan in PLAN.md).

### Added

- Multi-paste (ADR 0015). **Batch paste**: mark clips with Space, or Ctrl+Space while searching; each mark shows
  its place in the paste order, and Enter pastes them joined by the chosen separator (new line, blank line, comma
  and space, comma, semicolon, tab, space, nothing, or a custom one with `\n`, `\r`, `\t` and `\\`). Ctrl+S
  moves to the next separator, and the choice is remembered. **Table paste**: Ctrl+T pastes the marked clips as a
  spreadsheet column and Ctrl+Shift+T as a row, with cells quoted the way Excel reads them. **Queue paste**: Ctrl+Q
  queues the marked clips, and each Ctrl+V then pastes the next one. Ctrl+V and Esc are claimed with RegisterHotKey
  only while a queue runs, and are given back on every way it ends. A small window shows "Queue 2/5 · Esc to
  stop" in the corner without keeping the focus, and is hidden from screen capture. **Collecting**: Win+Alt+C,
  the tray menu or Ctrl+L in the popup start a session in which every copy is appended to one clip. Finishing
  saves that clip at the top of the history and puts it on the clipboard. The popup footer has buttons for
  paste, the separator, queue, table and collect. The tray menu gains Start, Finish and Cancel collecting, Stop
  paste queue and a ticked Restore clipboard after paste.
- A small settings file (`settings.json`, read with the base library's JSON support, so no new package) for the
  separator, the restore switch and whether copies are also kept while collecting. It holds no clip content, is
  read with a size and depth limit, falls back to the defaults, and keeps a damaged file aside as
  `settings.json.bad`.
- A `--collect-hotkey` switch for tests, beside `--hotkey`.
- UI smoke tests for batch, table, queue (three clips pasted in order into a real window) and collect paste.

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

- A paste from the popup ends a running queue first, so the queue cannot take the paste's own Ctrl+V.
- Whether the clipboard is put back after a paste now comes from the settings (the tray tick), read at each
  paste, instead of a fixed option.
- Dependabot now updates the three CodeQL action steps together in one pull request, because
  the action fails when its steps run different versions. All of them are on 4.38.3, and a test
  keeps it that way.
