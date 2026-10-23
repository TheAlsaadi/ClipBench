# ClipBench

[![CI](https://github.com/TheAlsaadi/ClipBench/actions/workflows/ci.yml/badge.svg)](https://github.com/TheAlsaadi/ClipBench/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A security-first clipboard manager for Windows 11. ClipBench replaces
"one item at a time" pasting with a workbench: everything you copy goes into an
encrypted local history, and a keyboard-first popup lets you search, select
several items, and paste them in one action.

> **Status: early development (pre-alpha).** The core clipboard logic, the encrypted
> history, the Windows integration and the app are built and tested: a tray icon, a
> shortcut (Win+Alt+V) that opens a search popup, pasting one clip into the program you
> were in, and multi-paste: batch, queue, collect (Win+Alt+C) and table paste.
> Transforms (Shift+Enter), boards and snippets are built too. The settings window, OCR and the installer come next. Nothing is released yet and there is nothing to install; you
> can build it from source and check it by hand with [docs/MANUAL_QA.md](docs/MANUAL_QA.md).

## Privacy promise

- **No network.** ClipBench's own code makes no network calls: no telemetry, no
  analytics, no update checks, no cloud sync. A build-time architecture test fails
  if any of its assemblies uses the .NET networking types.
- **One thing to know.** The interface runs on Microsoft's Windows App SDK, which
  ships with the app under Microsoft's license terms. Those terms allow the SDK to
  collect usage data. Before v1.0 an automated test will check that the running app
  opens no network connections, and the result will be published here. Until then,
  "no network" is a commitment and a test of ClipBench's own code, not a measured
  result for the whole process.
- **Encrypted at rest.** Clip content, previews and source-app names are
  stored encrypted (AES-256-GCM, with the key protected by Windows DPAPI).
- **Respects privacy markers.** Clipboard data flagged by password managers and
  other apps as "do not record" is never captured.
- **Honest limits.** ClipBench protects your history from other Windows users,
  offline disk access and backups. It cannot protect against malware already
  running as you, which can read the clipboard directly.

These are project commitments. Each one is enforced by an automated test as the
matching feature is built; see [docs/security](docs/security) as it is added.

## Planned features

- Searchable, encrypted clipboard history (text, rich text, images, files)
- Batch paste, queue paste, collect mode and table paste
- Paste-time transforms, pinned boards and snippets
- Privacy center with an app exclusion list and auto-expiring sensitive clips
- Local OCR for copying text from anywhere on screen

The phased plan lives in [PLAN.md](PLAN.md).

## Contributing and security

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.
To report a vulnerability, follow [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)
