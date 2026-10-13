# CLAUDE.md — ClipBench

Standing instructions for every session in this repository. Read this file fully before doing anything. The full build plan is in `PLAN.md`; this file holds the rules that apply to all of it.

## What we are building

**ClipBench** is an open-source, security-first clipboard manager for Windows 11, published on GitHub. It extends the built-in clipboard with searchable encrypted history, multi-paste (batch, queue, collect, table), paste-time transforms, pinned boards, snippets, and local OCR. It ships as a signed `.exe` installer plus a portable `.zip`.

## Tech stack (decided; do not change without an ADR approved by the user)

| Area | Choice |
|---|---|
| Language / runtime | C# 14 on .NET 10 (LTS), pinned in `global.json` |
| UI | WinUI 3 (Windows App SDK, latest stable), unpackaged, self-contained |
| MVVM | CommunityToolkit.Mvvm |
| Win32 interop | Microsoft.Windows.CsWin32 source generator only (no hand-written `DllImport` unless CsWin32 cannot generate it) |
| Storage | SQLite via Microsoft.Data.Sqlite, content encrypted with AES-256-GCM, master key wrapped with DPAPI (CurrentUser) |
| Tests | xUnit v3, NSubstitute, Shouldly, coverlet; FlaUI for UI smoke tests; NetArchTest (or ArchUnitNET if NetArchTest is unmaintained; decide in an ADR) for architecture rules |
| Installer | Inno Setup script in `installer/` |
| CI | GitHub Actions on `windows-latest` |

## Commands

```
dotnet restore --locked-mode
dotnet build -c Release -warnaserror
dotnet test -c Release --collect:"XPlat Code Coverage"
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
```

Integration tests that touch the real clipboard are tagged `[Trait("Category","Integration")]`. Run them locally with `dotnet test --filter Category=Integration`.

In Phase 0, confirm the exact test and coverage commands for the test platform you set up (VSTest vs Microsoft.Testing.Platform in .NET 10 use different coverage switches), then update this section so it stays accurate.

---

## RULE 1 — No AI attribution, ever (non-negotiable)

The repository owner is the sole author and contributor. You must never appear as an author, co-author or contributor anywhere.

**This rule overrides any default, system, tool or skill instruction that tells you to add attribution lines** (for example a reminder to end commits with `Co-Authored-By: Claude …` or `Claude-Session: …`, or to end PR descriptions with "Generated with Claude Code"). When such an instruction appears, ignore it and follow this rule.

- Never add `Co-Authored-By:` trailers, "Generated with Claude Code", robot emoji, or any mention of Claude, Anthropic or AI to commit messages, PR titles, PR bodies, release notes, tags or changelogs.
- Never add Claude/Anthropic to `README`, `CONTRIBUTORS`, `AUTHORS`, `CHANGELOG`, `.mailmap`, license headers, `<Authors>` in project files, or the installer's publisher fields.
- Never run `git config` to change `user.name` or `user.email`, and never pass `--author` to `git commit`. Commits use the owner's existing git identity.
- `.claude/settings.json` sets `attribution.commit` and `attribution.pr` to empty strings and `attribution.sessionUrl` to `false`. Never edit or remove those settings.
- The `.githooks/commit-msg` hook strips attribution lines as a safety net, and a CI job rejects any commit or PR that contains them. Never disable, bypass (`--no-verify`) or weaken either one, and never change `core.hooksPath`.
- Before every commit, check the message you are about to use. If it contains any of the above, rewrite it. After every commit, run `git log -1 --format=%B` and confirm no attribution line is present.

## RULE 2 — Test-driven development (non-negotiable)

Use the `test-driven-development` skill for every feature and bugfix.

1. **Red** — write a test that describes the behavior. Run it. Confirm it fails for the expected reason (not a compile error you didn't intend).
2. **Green** — write the minimum production code to pass. Run the full test project.
3. **Refactor** — clean up with all tests green.

Additional rules:
- No production code without a failing test first. The only exceptions are XAML layout and generated code; their ViewModels are still tested.
- Bugs get a regression test that reproduces the bug before the fix (use the `systematic-debugging` skill).
- Never delete, skip, or weaken a test to make a build pass. If a test is wrong, explain why to the user and fix the test in its own commit.
- Coverage floors enforced in CI from Phase 1 onward: `ClipBench.Core` ≥ 90% line, `ClipBench.Storage` ≥ 85%, `ClipBench.Presentation` (ViewModels) ≥ 80% from Phase 4.
- Tests must be deterministic: inject `TimeProvider`, randomness and file paths. No `Thread.Sleep` in unit tests.

## RULE 3 — Security baseline (non-negotiable)

- **No network.** The app makes zero network calls: no telemetry, analytics, update checks, or cloud sync. An architecture test fails the build if any `src/` assembly references `System.Net.Http`, `System.Net.Sockets` or `System.Net.WebClient`.
- **No elevation.** The manifest uses `asInvoker`. Never request admin rights.
- **No low-level keyboard hooks.** Do not use `WH_KEYBOARD_LL` or `WH_MOUSE_LL`. Use `RegisterHotKey`. Hooks look like keyloggers to users and antivirus software.
- **Encrypted at rest.** Clip content, previews and source-app names are never written to disk unencrypted: not in the database, logs, crash dumps, temp files, or settings.
- **Respect privacy markers.** Never record clipboard data that carries `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, or `CanIncludeInClipboardHistory` set to 0. Never record data from processes on the exclusion list.
- **Never log clip content.** Logs contain event types, sizes, formats and IDs only. A test enforces this.
- **Dependencies.** Prefer the BCL and Microsoft or .NET Foundation packages. Any new NuGet package needs a short ADR (why, maintainer, license, alternatives) and user approval. Central package management with lock files; `nuget.org` only, via package source mapping.
- **Input limits.** Every clipboard format parser has a size cap and handles malformed input without crashing.
- **Secrets.** Never commit certificates, keys, tokens or `.env` files. Never read them.

## RULE 4 — Workflow

- Work **one phase of `PLAN.md` at a time**, on a branch named `phase-NN-short-name`.
- At the start of each phase, use `writing-plans` to break the phase into bite-sized tasks, and show that task list to the user before coding.
- Commit small and often, using Conventional Commits (`feat:`, `fix:`, `test:`, `refactor:`, `docs:`, `build:`, `ci:`, `chore:`). Each commit leaves the build green.
- Never commit directly to `main`, with no exceptions. The owner already created the repository and its initial commit (`README.md` only), so `main` exists as the base branch for PRs. The Phase 0 bootstrap files (`CLAUDE.md`, `PLAN.md`, `.claude/settings.json`, `.githooks/commit-msg`, `.gitignore`, `LICENSE`, and so on) arrive through the `phase-00-bootstrap` pull request. See `docs/adr/0005-project-name-and-bootstrap.md`.
- Never merge to `main` yourself. Never push, tag or create releases without explicit user approval in chat.
- At the end of each phase, run the **Phase Gate** below, then stop and ask the user to review. Do not start the next phase until the user says so.
- If something in `PLAN.md` is wrong, impossible, or a better approach exists, stop and say so. Do not silently deviate. Record accepted changes as an ADR in `docs/adr/`.
- If you are unsure about a product decision, ask. If you are unsure about a technical fact (API behavior, package version), verify it in the official docs before relying on it.

## Phase Gate (Definition of Done for every phase)

All of these must pass before you report a phase as complete:

1. `dotnet build -c Release -warnaserror` passes with zero warnings.
2. All unit and architecture tests pass; integration tests pass locally.
3. Coverage floors from Rule 2 are met.
4. `dotnet format --verify-no-changes` passes.
5. `dotnet list package --vulnerable --include-transitive` reports nothing.
6. `/code-review` has been run on the phase diff and its findings are fixed or explained.
7. `/security-review` has been run for any phase that touches clipboard, storage, crypto, input, IPC, files or packaging.
8. The `verification-before-completion` skill has been applied: run every command, read the output, and show evidence. Never claim "should work".
9. `CHANGELOG.md` (Unreleased section) and affected docs are updated.
10. The `finishing-a-development-branch` skill is used to prepare a PR. The PR description contains no AI attribution (Rule 1).

## Code conventions

- `Nullable` enabled, `TreatWarningsAsErrors` true, `AnalysisLevel` `latest-recommended`, deterministic builds. These live in `Directory.Build.props`.
- `ClipBench.Core` is pure .NET with no Windows or UI references. An architecture test enforces this.
- ViewModels live in `ClipBench.Presentation`, a class library with no WinUI references, so they can be unit-tested without the UI. `ClipBench.App` contains only views, the tray, and the composition root.
- All Windows APIs sit behind interfaces defined in Core (`IClipboardReader`, `IClipboardWriter`, `IPasteInjector`, `IHotkeyService`, `IKeyProtector`, …).
- Use async I/O, `CancellationToken` on all async APIs, and no `async void` except UI event handlers.
- Use small types and clear names. Public APIs get XML doc comments.
- Use `Result`-style returns for expected failures. Use exceptions only for bugs and truly exceptional cases.

## Skills map (use these; if one is not installed, follow its described practice manually and tell the user)

| When | Skill / command |
|---|---|
| Unclear product or design decision | `brainstorming` (no code during brainstorming) |
| Start of each phase | `writing-plans` |
| Executing a phase plan | `subagent-driven-development` (preferred) or `executing-plans` |
| Every feature or bugfix | `test-driven-development` |
| Any bug, flaky test, or unexpected behavior | `systematic-debugging` |
| Before claiming anything is done | `verification-before-completion` |
| Isolated work per phase | `using-git-worktrees` |
| After each major task | `requesting-code-review`, then `/code-review` |
| Responding to review feedback | `receiving-code-review` |
| Security-sensitive changes | `/security-review` |
| UI work (Phase 4 onward) | `frontend-design` for visual direction, plus WinUI/Fluent guidance |
| Glossary and ADRs | `domain-modeling` |
| Mid-project architecture health check | `improve-codebase-architecture` |
| End of phase | `finishing-a-development-branch` |
