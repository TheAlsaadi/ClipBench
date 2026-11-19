---
status: accepted
---

# Phase 9 quality tools and crash handling

Phase 9 asks for property tests on every parser, mutation testing on the critical code, crash handling that leaks nothing, and a check of the history folder's permissions. Two of these need tools the project did not have, and two needed a product decision. The owner approved all four in chat on 2026-11-20. Versions and licenses were read from nuget.org the same day.

## Decisions

### D1. FsCheck for property tests

| Package | Version | License | Publisher | Role |
|---|---|---|---|---|
| FsCheck | 3.6.0 | BSD-3-Clause | Kurt Schelfthout and contributors | generates inputs and shrinks a failing one to the smallest case |
| FsCheck.Xunit.v3 | 3.6.0 | BSD-3-Clause | Kurt Schelfthout and contributors | the `[Property]` attribute for xUnit v3 |
| FSharp.Core | 11.0.101 | MIT | Microsoft | FsCheck's runtime library. FsCheck asks for 5.0.2 or later; it is pinned to the current release so an old copy is not restored |

The packages reach two test projects only: `ClipBench.Core.Tests` and `ClipBench.Platform.Windows.IntegrationTests`. No `src/` project references them, so nothing reaches the installer or the portable zip.

Every property runs 1,000 cases from one fixed seed, so a run is the same every time (the deterministic-test rule). To explore new inputs, change the seed by hand and run the tests locally. Keep any failure found that way as a plain regression test.

**Considered options:**
- **A hand-rolled generator.** PLAN.md allows it. It needs no package, but it cannot cut a failing input down to the smallest one, and that is most of the value when a 40 KB random input breaks a parser.
- **CsCheck** (Apache-2.0). It is a good C# library, but smaller and maintained by one person. FsCheck is older, has many maintainers and has an xUnit v3 package from the same project.

### D2. Stryker.NET for mutation testing

`dotnet-stryker` is a local dotnet tool, listed in `.config/dotnet-tools.json`. It runs by hand (`scripts/run-mutation.ps1`), not in CI, and never ships. The version, the license and the results are recorded in `docs/MUTATION.md` when the tool is first run.

Stryker runs on `ClipBench.Core` and, beyond what PLAN.md names, on the encryption code in `ClipBench.Storage` (`Crypto/` and `Keys/`). PLAN.md asks for the crypto wrappers to be checked, and they live in Storage, not in Core.

### D3. Crash handling

- **UI thread.** An exception nothing caught is logged as `AppCrashed` with its type and where it surfaced, never its message. The services are stopped, one notice says the app has to close, and the app exits. Carrying on could leave the popup half-working while it holds decrypted clips.
- **Background task.** A faulted task that nothing observed is logged as `BackgroundFaulted`, and the app keeps running.
- **Process.** When the runtime itself is ending the process, the crash is logged only, because no window can be shown.
- **Windows crash reports.** `WerSetFlags(WER_FAULT_REPORTING_FLAG_NOHEAP)` is called before anything is decrypted, so a report Windows makes leaves the heap out. ClipBench writes no minidump of its own.
- **Diagnostic report.** It is opt-in: a button in Settings → About saves a text file only where the user chooses. It holds versions, counts and the recent log lines. A log line goes in only if it matches exactly what the log writer produces. Any other line is left out and counted.

### D4. History folder permissions

The history folder gets a protected access list of its own: the current user and SYSTEM, full control, inherited by every file inside. The Administrators group is not on it. A redirected or shared `%LOCALAPPDATA%` therefore cannot open the files to other accounts. An administrator can still take ownership, which no permission can stop. If Windows refuses, the app logs it and starts anyway, because the content is encrypted either way.

## Consequences

- Two new test-only packages and one dev tool, each recorded here and in `docs/DEPENDENCIES.md`.
- The CI unit run gets slower by the time the properties take. Each property is kept to 1,000 cases.
- A crash now closes the app instead of leaving it in an unknown state. The user sees a notice.
