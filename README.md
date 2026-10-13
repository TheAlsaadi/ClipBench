# ClipBench

[![CI](https://github.com/TheAlsaadi/ClipBench/actions/workflows/ci.yml/badge.svg)](https://github.com/TheAlsaadi/ClipBench/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A security-first clipboard manager for Windows 11. ClipBench replaces
"one item at a time" pasting with a workbench: everything you copy goes into an
encrypted local history, and a keyboard-first popup lets you search, select
several items, and paste them in one action.

> **Status: early development (pre-alpha).** The repository scaffolding is
> being built. Nothing is released yet and there is nothing to install.

## Privacy promise

- **No network.** ClipBench makes no network calls: no telemetry, no analytics,
  no update checks, no cloud sync. A build-time architecture test fails if any
  application assembly references the .NET networking types.
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
