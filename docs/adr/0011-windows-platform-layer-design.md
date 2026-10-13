---
status: accepted
---

# The Windows platform layer: one window thread, privacy before content, and a paste that checks itself

Phase 3 puts the clipboard, global shortcuts, pasting and the key wrap on Windows behind the interfaces in Core. The owner delegated the design on 2026-10-13 (ADR 0010 records the one package). These are the decisions a later reader would otherwise have to guess.

## Decisions

**One thread owns every window-bound call.** Windows ties some calls to the thread of a window: a shortcut is registered for the window that receives its presses (from another thread, `RegisterHotKey` fails with error 1408), clipboard-change messages go to a window, and only a window can own the clipboard. A message-only window runs on a thread of its own, work is queued to it, and callers wait on a task. Handlers and queued work cannot throw into Windows, work that never started fails when the window is disposed, and the thread never waits for a subscriber: events go to a small pump that delivers them in order on the thread pool, so a slow or failing subscriber cannot freeze every shortcut.

**The clipboard is open only while bytes are copied.** Other programs wait while it is open, and Windows wants it closed by the thread that opened it. Opening, copying and closing are one synchronous stretch. Waiting for a busy clipboard (doubling from 10 ms up to 320 ms, eight attempts, about a second in all) happens before it, never inside it. A clipboard that stays busy is reported as busy and not as empty. Converting a picture to PNG happens after it is closed.

**Privacy is decided before content is read.** The reader looks at the names of the formats, who owns them and the history flag, and asks the capture filter. A change that carries a privacy marker, comes from an excluded program, arrives in incognito or is ClipBench's own is rejected with no content copied; the test log of the in-memory clipboard shows no read of the text. The flag `CanIncludeInClipboardHistory` is read from its first four bytes, because Windows may report a larger block than was asked for; a flag that is present but unreadable counts as private. The filter is then asked again with the real size, because UTF-16 is not UTF-8 and a bitmap is not its PNG.

**Size limits are applied twice, to different things.** Before copying, a hard cap on the raw data stops hostile input from using memory: four bytes of raw data for every byte of the text limit, and 36 million pixels for a picture (144 MB when decoded, which an 8K screen fits). After decoding, the capture limits apply to what would be kept. A picture is judged by the size of its PNG, so a 4K screenshot (33 MB as a bitmap) is not turned away by the 20 MiB limit.

**What is kept is chosen by a fixed rule.** A file list wins. With text on the clipboard the richest text form wins (HTML, then rich text, then plain text) and a picture is the last resort. Without text a picture beats HTML, because a browser's *Copy image* puts an `<img>` tag beside the picture and the picture is what was meant. A form that cannot be read falls back to the next one. Rich text without text is not kept, because it has no text alternative. HTML without text gets one from a small linear tag stripper. A damaged form is reported as damaged, and an empty clipboard as empty.

**Pictures are captured from bitmaps only, and only ClipBench writes PNG.** The reader takes CF_DIBV5 (else CF_DIB) and writes the PNG itself, so every stored PNG is 8 bits per channel, RGB or RGBA, not interlaced. The reader accepts exactly that kind, which is what lets the writer turn a stored PNG back into a bitmap without a general PNG decoder. A 32-bit bitmap whose alpha values are all zero is treated as opaque, because screenshots are stored that way. The writer sets the registered `PNG` format (alpha kept for programs that read it) and CF_DIBV5; Windows makes the plain bitmap for older programs. The tests compare against Windows' own PNG decoder and encoder.

**ClipBench's own writes are marked and ignored three times.** The writer adds the private format `ClipBench.Internal` in the same open stretch. The listener drops a change whose current content carries it, the capture filter rejects it, and a restored clipboard is tagged too, so putting the user's clipboard back is not recorded as a new copy.

**A paste checks itself at every step.** The order is the design. It looks at the target before touching anything and stops with `TargetElevated` if Windows would drop the keys (a program with a higher integrity level, or one that refuses to be asked, which is what a program with more rights does). It keeps a raw copy of the user's clipboard, writes the content, brings the target to the front and believes only `GetForegroundWindow` (Windows may refuse or pretend), checks that the clipboard sequence number is still the one the write returned, waits up to half a second for Alt, Shift and Windows to be released because Ctrl+V would become another shortcut, presses Ctrl+V, waits the restore delay and restores. No key is sent unless the target has focus. A restore never overwrites something another program copied meanwhile. When the user already holds Ctrl, as in queue paste, only V is tapped so their next tap still works. With restore turned off there is no wait.

**The snapshot copies memory formats only.** Every format whose data is a block of global memory is copied, privacy markers included, so restoring a password manager's entry restores its marker. Handle formats (bitmap handle, metafile, palette, owner-display, the private and GDI ranges) are left out, and one shared definition says which they are. The reason is a fault found by a test: reading `CF_BITMAP` with the global-memory functions treated a GDI handle as a pointer and corrupted the heap. The binding now refuses those formats before calling Windows. A clipboard over 64 MiB is not snapshotted and the paste goes ahead without a restore. Reading every format forces programs that render lazily to render them; that is the cost of restoring faithfully.

**Shortcuts use `RegisterHotKey` and nothing else.** `MOD_NOREPEAT` makes holding the keys one press. Numbers stay within Windows' limit (0xBFFF) and are not reused. A shortcut another program has comes back as `HotkeyUnavailable` with its name in the message; any other error is a platform failure with the error number. The tests press F24, a key no keyboard has: a registered shortcut takes it before any program sees it, so nothing is typed into the user's windows. Modifier combinations are avoided in tests on purpose, because Ctrl+Shift and Alt+Shift switch the input language and Win opens the Start menu.

**One running copy per signed-in user.** A named mutex that is never owned (so it belongs to no thread and cannot be abandoned) decides which launch runs; a second launch sets a named event and exits. Both launches open or create the event, so a signal is never lost, and a request that arrives before the running copy has a listener is kept for the first one. Names are checked against a short safe alphabet, and both objects are in the `Local\` namespace, so two people signed in to one PC each get their own copy. It uses the .NET `Mutex` and `EventWaitHandle` and needs no Win32 declaration.

**Process names are all that is kept of a program.** The owner of the clipboard and the target of a paste are found with `PROCESS_QUERY_LIMITED_INFORMATION` and nothing more; a program that refuses is "unknown" and is never asked again with more rights. Only the executable's file name is kept, never a path or a window title.

**The key wrap uses DPAPI with the current user's scope and ClipBench's own entropy,** no prompt, size caps on both sides, and unmanaged buffers given back (the unwrapped secret is overwritten first). The entropy is a constant in the source: it keeps another program that calls DPAPI without it from unwrapping the key by accident, and does not stop malware running as the same user (the threat model says so).

**The list of Win32 functions is guarded.** Architecture tests fail the build if it names a hook, a raw-input or keyboard-state reader, a launcher, a privilege change or access to another process's memory, repeats a name, or keeps a name the code no longer uses; they inspect the built assembly as well as the list, and they check that unsafe code exists only in this layer.

## Considered options

- **The WinRT clipboard API.** Rejected. It needs a focused, UI-thread window, cannot see most of the formats that carry privacy markers, and cannot keep a clipboard open while copying.
- **OLE data objects (`OleGetClipboard`).** Rejected for now. They would allow lazily rendered data, at a cost in complexity and in COM lifetime rules that the memory formats avoid.
- **Polling the sequence number instead of listening.** Rejected. A listener costs nothing while idle and reports a change at once.
- **`System.Drawing` or WIC for PNG.** Rejected. One is a package, the other an asynchronous WinRT dependency, and neither gives a decoder restricted to what ClipBench writes.
- **A general PNG decoder.** Rejected. The restricted one cannot be asked to interpret a file it was not written for.
- **Writing with `keybd_event`.** Rejected. `SendInput` reports how many events Windows accepted, which is how a blocked paste is noticed.

## Consequences

- A paste into a program with more rights than ClipBench is refused with a message, not attempted. ClipBench never elevates.
- The restore delay (250 ms by default) is a guess about how fast programs read the clipboard after Ctrl+V; a slow program can still receive the restored content. The setting is exposed in the request.
- Subscriber exceptions are swallowed so that one bug cannot stop events. Phase 4 adds logging for them.
- The paste test that sends a real Ctrl+V into a test window needs a terminal that is in front, because Windows protects the window the user is working in from other programs taking focus; otherwise it skips and says why. The clipboard tests save the user's clipboard first and put it back afterwards, and they run one at a time.
- CI runs the unit tests only (`Category=Integration` is excluded). The integration tests run on the owner's machine.
