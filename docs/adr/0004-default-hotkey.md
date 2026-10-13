---
status: accepted
---

# Default hotkey: Win+Alt+V

The popup opens from a global hotkey that is always configurable. The default is **Win+Alt+V**, confirmed by the owner on 2026-10-13: it was free on the probe machine, it sits next to the familiar Win+V, and Win-modified combinations are almost never used as shortcuts inside applications, so registering it globally takes nothing from them.

## Probe

A throwaway console program (kept out of the repository) asked Windows to register each combination with `RegisterHotKey`, then released it. No hook was installed and no key press was observed. Windows 11 build 26300, 2026-10-13, with the usual background apps running:

| Combination | Result |
|---|---|
| Win+Alt+V | free |
| Ctrl+Shift+Space | free |
| Ctrl+Alt+Space | refused: error 1409, already registered |
| Win+Shift+V | refused: error 1409, already registered |
| Win+V (control) | refused: error 1409, as expected for Windows clipboard history |
| Ctrl+Shift+V (information) | free, but never a default |

## Why not the others

- **Ctrl+Shift+V** means "paste as plain text" in browsers and many editors. Taking it globally would break that.
- **Ctrl+Shift+Space** was free here, but editors such as Visual Studio and VS Code use it for parameter hints, and a global registration would steal it from them.
- **Ctrl+Alt+Space** and **Win+Shift+V** were refused on the probe machine.

## Consequences

- The result is a snapshot of one machine at one time. Other machines will differ, and a future Windows update could claim Win+Alt+V.
- The app must report a failed registration clearly, show which combination was refused, and let the user pick another. This is covered by the Phase 3 hotkey service tests.
