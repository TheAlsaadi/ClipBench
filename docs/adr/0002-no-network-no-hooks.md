---
status: accepted
---

# No network and no low-level hooks

ClipBench's own code makes no network calls and installs no low-level keyboard or mouse hooks. Hotkeys use `RegisterHotKey`, and queue mode registers a temporary Ctrl+V hotkey instead of intercepting keys. A program that sees every clipboard must not be able to leak it, and a program that hooks the keyboard looks like a keylogger to people and to antivirus software.

An architecture test fails the build if any source assembly depends on `System.Net.Http`, `System.Net.Sockets`, `System.Net.WebSockets`, `System.Net.Mail`, `WebClient`, `WebRequest`, `HttpWebRequest`, `FtpWebRequest` or `Dns`. A second test, added in Phase 3, forbids `SetWindowsHookEx` and the `WH_KEYBOARD_LL` and `WH_MOUSE_LL` hook types.

## Considered options

- **Update checks and telemetry.** Rejected, even though they are convenient. Releases are downloaded on purpose and verified with checksums and attestations.
- **A low-level keyboard hook for queue paste.** Rejected. `RegisterHotKey` does the job and carries none of the keylogger reputation. The cost is that Ctrl+V must be unregistered and re-registered around each synthetic key press (PLAN section 2.6).

## Consequences

- Users update by hand, and old versions stay in use until they do.
- A hotkey can already be taken by another program (ADR 0004), so registration failures must be handled and shown.
- The test runner's own telemetry is outside the app and is handled separately (ADR 0006).
