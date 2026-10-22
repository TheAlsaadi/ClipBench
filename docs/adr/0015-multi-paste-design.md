---
status: accepted
---

# Phase 5 design: batch, queue, collect and table paste

Phase 5 adds the four ways of pasting more than one clip that PLAN.md calls the headline of v0.1. This record holds the decisions that shape them. Where PLAN.md said something else, it says so. It is updated in the same phase when a decision changes.

## Decisions

1. **The owner chose how the features feel (2026-10-22).**
   - Queue: while a queue runs, Ctrl+V and Esc belong to ClipBench. Esc stops the queue, and so does the tray menu. After the last clip the queue ends, and both keys go back to the other programs at once. The popup offers no loop option. The loop that `PasteQueue` already supports stays unused for now.
   - Collect: a session starts and finishes from the tray menu, from the popup, or with a global shortcut, Win+Alt+C. Finishing saves one clip at the top of the history and also puts it on the clipboard, ready for Ctrl+V. While a session runs, copies are not also saved as separate clips. A setting can change that.
   - Popup keys: when clips are marked, Enter pastes all of them, joined by the current separator, in the order they were marked. Ctrl+Space marks a clip even while searching. Ctrl+S moves to the next separator. Ctrl+Q queues the marked clips. Ctrl+T pastes them as a spreadsheet column, and Ctrl+Shift+T as a row. With nothing marked, the highlighted clip is used, as before. The footer has buttons for the same actions.
   - v0.1.0 is a git tag only. Installers and downloads stay in Phase 10.
2. **Queue mode uses RegisterHotKey only, never a keyboard hook (ADR 0002).** Arming a queue claims Ctrl+V and Esc as temporary global shortcuts. On a press of Ctrl+V the queue releases Ctrl+V, pastes the next clip into whatever has focus, and then claims Ctrl+V again unless the queue has ended. The user is still holding Ctrl, so the paste taps only V (ADR 0011). If either shortcut cannot be claimed, the queue does not start, nothing stays claimed, and the user is told why. On 2026-10-22 a probe on Windows 11 found Win+Alt+C, Ctrl+V and a plain Esc all free.
3. **Every way out releases the shortcuts.** The queue releases Ctrl+V and Esc when it runs out, when it is stopped, when a paste fails, when a new queue replaces it, and when the app exits. If the process dies, Windows releases them with the window that owns them, so a crash cannot leave Ctrl+V taken. The decisions live in a Core class, `QueueRunner`, around the existing `PasteQueue` state machine. It reaches Windows only through `IHotkeyService` and `IPasteInjector`, so every exit path has a unit test with fakes.
4. **The queue HUD never takes focus and shows no content.** A small always-on-top window shows "Queue 2/5 · Esc to stop" near the corner of the work area. It does not activate when shown, it lets clicks pass through, it is excluded from screen capture like the popup, and it shows counts, never clip text. Taking focus would move the paste to the wrong window.
5. **Collect is routed inside capture.** The capture service passes each accepted capture to an active collect session before the history sees it. A text capture is appended. It is not stored on its own unless the "keep copies" setting is on. An image, a capture that would pass the session's size limit, and ClipBench's own writes are handled as before. The first time the limit is reached the user is told once. The session joins captures with the separator chosen in the popup when the session started. The finished clip is added to the history and written to the clipboard with the own-write marker, so the listener does not capture it a second time.
6. **Batch and table paste compose text first and paste it once.** The popup builds the text with `PasteComposer` from the marked clips in marking order, or from the highlighted clip alone. A composer failure, such as an image in the selection or text over the size limit, keeps the popup open with the reason. Nothing is pasted. Marks are kept by clip id in marking order, so they survive a new search. A clip that leaves the history leaves the marks.
7. **A small settings file arrives now, ahead of Phase 7.** PLAN.md asks that the separator picker remember the last choice and that "restore previous clipboard after paste" be a setting that defaults to on. Both need to persist before the Settings window of Phase 7 exists. A `settings.json` in the data folder holds the chosen separator (a preset or one custom separator, at most 64 characters as typed, using the escape rules of `Separator.Parse`), the restore switch, and the collect "keep copies" switch. It is read with `System.Text.Json` from the base library, so no package is added. The file is size-capped on read and written by replacing a temporary file. A missing, damaged or oversized file falls back to the defaults, and a damaged one is kept beside it for inspection. Unknown keys are ignored. The file never holds clip content. Phase 7 extends it with validation ranges and the Settings window.
8. **The restore switch is a ticked item in the tray menu until Phase 7.** A change applies to the next paste, including the next paste of a running queue.

## Consequences

- While a queue runs, Esc does nothing in other programs, and Ctrl+V pastes the queue's next clip instead of the clipboard. The HUD says so, and the queue ends on its own after the last clip.
- Ctrl+V pressed while ClipBench's own popup has focus also takes the next queued clip. This is accepted: opening the popup is a deliberate step away from the form being filled.
- The settings file is plain JSON, not encrypted. It holds no clip content, only choices, and the threat model records this.
- No new packages. The UI smoke test for queue mode sends real keys, so it runs on demand, like the Phase 4 smoke tests.
