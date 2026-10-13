---
status: accepted
---

# Phase 4 design: the app shell, the library and the popup

Phase 4 turns the platform layer of Phase 3 into an app a person can use: a tray icon, a global shortcut, a popup with search, and paste of one clip. This record holds the decisions that shape it. Where PLAN.md said something else, it says so. It is updated in the same phase when a decision changes.

## Decisions

1. **The history is one object with a store behind an interface.** `ClipLibrary` (Core) holds the in-memory history, keeps it in step with an `IClipStore` (Core), searches it and remembers which clips look like secrets. `ClipStore` (Storage) implements the interface over `ClipRepository`. Presentation therefore needs neither Storage nor SQLite, which keeps the dependency rule of PLAN section 2.2 (Presentation references only Core and the MVVM toolkit). A change is made in memory first and stored second; a failing store never hides a clip the user just copied, and the failure is returned so it can be shown. Changes are stored one at a time, in the order they were made.
2. **A secret is detected once per clip, on first use, not for the whole history at start-up.** The detector reads up to a million characters per clip, so scanning a full history on every launch would be slow. The verdict is cached per clip id and dropped when the clip leaves the library.
3. **Packages and start-up.** CommunityToolkit.Mvvm and dependency injection, without the generic host and without a logging package (ADR 0012). FlaUI for the smoke test (ADR 0013).
4. **Logs cannot hold clip content, by construction.** Core defines a small log interface (`IActivityLog`) with a fixed list of events and details that are only codes, sizes, formats and types, with no string field that could carry text. An unexpected exception is logged by its type, never its message, because messages can quote what was being read. `FileActivityLog` (Windows layer) writes it to a small set of size-capped files. A test still reads the written files for a marker string, as the project's security rules require.
5. **View models are plain objects with injected time and threading.** `ClipBench.Presentation` references no UI assembly. The search debounce uses `TimeProvider` timers and updates reach the UI thread through an `IUiDispatcher` that tests replace with an immediate one, so no test needs a UI thread or `Thread.Sleep`.
6. **The popup.** A compact list, about 420 by 520 device-independent pixels, chosen by the owner on 2026-10-14: a search box on top, rows with a short preview, source program and age, a pin mark, and a footer of key hints. It opens at the text caret when Windows reports one and at the mouse pointer otherwise, clamped to the work area of the monitor that holds that point. It hides when it loses focus. The placement is a pure function with its own tests.
7. **The tray icon needs a real, hidden, top-level window, not a message-only one.** Windows sends the `TaskbarCreated` broadcast, which tells a tray icon to add itself again after Explorer restarts, to top-level windows only. The window runs on its own thread like the hotkey and clipboard window of ADR 0011, and calls reach the UI thread through the dispatcher. The tray menu is the native popup menu (`TrackPopupMenu`): it is documented, robust and keyboard accessible. It follows the system theme less closely than a XAML flyout would; this can be revisited in Phase 7 when the settings window arrives.
8. **Hidden from screen capture by default.** The popup asks Windows to exclude it from screenshots and screen sharing (`SetWindowDisplayAffinity`), as PLAN.md Phase 4 requires. The setting arrives in Phase 7; until then the default is on.
9. **Sensitive clips are masked in the list** until the user reveals them. Detection is a heuristic (Phase 1), so masking is a courtesy and never a guarantee.
10. **Test seams on the command line.** `--data-dir <folder>` keeps the history and key in another folder, and `--hotkey <key>` replaces the default shortcut. The smoke test uses both so that it neither reads the user's history nor takes the user's shortcut. Anyone who can start the app with arguments can already run code as the user, so these add no reach.
11. **No code in views beyond plumbing.** XAML binds to view models. Keyboard handling, focus and window placement are view code, kept thin, and the logic they call is tested.

## Consequences

- The popup layout was chosen from a sketch, not from running the app. The owner checks it by hand with `docs/MANUAL_QA.md`, and a smoke test covers start, open, filter and close.
- Three things are deferred on purpose: multi-select paste (Phase 5; Space already toggles a mark), the settings window and the hide-from-capture switch (Phase 7), and short expiry for sensitive clips (Phase 7).
- The native tray menu will not follow dark mode as closely as the rest of the app. Recorded as a known gap.
