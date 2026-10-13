# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Core domain (`ClipBench.Core`, no Windows code): the `Clip` model with text, HTML, RTF,
  image and file-list content and previews that never split a character; the clip history
  with duplicate handling, pinning and retention by count and age; ranked substring and fuzzy
  search that handles Arabic, Hebrew, accents and emoji; batch and table paste composition;
  the queue paste state machine; collect sessions; the capture filter (privacy markers,
  excluded applications, incognito, size limits); and a detector for card numbers, IBANs,
  JSON web tokens, private keys and random-looking tokens.
- CI now enforces 90% line coverage on `ClipBench.Core` and fails a coverage run that
  measured no lines (an empty assembly reports 100%).
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
- Architecture decision records 0001 to 0007, a glossary, a roadmap and a first
  draft of the threat model.

### Changed

- Dependabot now updates the three CodeQL action steps together in one pull request, because
  the action fails when its steps run different versions. All of them are on 4.38.3, and a test
  keeps it that way.
