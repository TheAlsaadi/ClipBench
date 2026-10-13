---
status: accepted
---

# FlaUI for the UI smoke test

CLAUDE.md names FlaUI for UI smoke tests, and PLAN.md Phase 4 asks for one: the app starts, the hotkey opens the popup, typing filters the list, and Esc closes it. FlaUI is a third-party package, so it needs its own record. The owner approved it in chat on 2026-10-14. The versions and licenses were read from nuget.org the same day.

## What is added

| Package | Version | License | Publisher | Role |
|---|---|---|---|---|
| FlaUI.Core | 5.0.0 | MIT | Roemer (the FlaUI project) | the automation API |
| FlaUI.UIA3 | 5.0.0 | MIT | Roemer | the UI Automation 3 backend; it also brings `Interop.UIAutomationClient` |

FlaUI.Core depends on `Microsoft.Win32.Registry`, `System.Diagnostics.PerformanceCounter`, `System.Drawing.Common`, `System.Management` and `System.Security.Permissions`. They reach the smoke test project only. No `src/` project references FlaUI, and `ClipBench.UiSmokeTests` is not shipped, so nothing from it reaches the installer or the portable zip. The restored lock file of the test project is the authority for the transitive packages.

## Considered options

- **Microsoft's built-in `System.Windows.Automation`** (part of the .NET desktop runtime, no package). It is the older managed wrapper, and it has not been checked against a WinUI 3 window. The owner preferred FlaUI, which is built on UI Automation 3 directly and is the common choice for this kind of test.
- **Raw UI Automation through COM interop.** Rejected. It means writing and maintaining the wrapper that FlaUI already is.
- **WinAppDriver or Appium.** Rejected. WinAppDriver is archived, and both add a server process to the test run.
- **No UI test.** Rejected. Phase 4 asks for it, and the view code is the one part that unit tests cannot reach.

## Consequences

- **One maintainer.** FlaUI is maintained by one person and a small community. It is used only to test. An abandoned or compromised release could, at worst, break or subvert a test run. It could not reach a build artifact. Dependabot tracks it, and the Phase 9 audit re-checks the maintainer.
- **The smoke test must be able to run unattended.** It needs a desktop session. If the CI runner cannot give it one reliably, the test is tagged `Category=Integration` and run locally, like the real-clipboard tests, and the reason is recorded.
- **The test never touches real data.** The app is started with `--data-dir` pointing at a temporary folder and with a test hotkey (`--hotkey F24`), so a smoke run neither reads the user's history nor takes the user's shortcut.
