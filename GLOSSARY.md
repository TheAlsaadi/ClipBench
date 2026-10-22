# ClipBench

A clipboard manager for Windows that keeps an encrypted history of what you copy and lets you paste several clips in one action. This glossary fixes the words used in code, docs and the interface.

## Language

### Clips and history

**Clip**:
One captured piece of clipboard content (text, rich text, an image or a list of files) together with when it was copied and where it came from.
_Avoid_: item, entry, record

**Capture**:
Recording a clipboard change as a Clip.
_Avoid_: grab, snapshot

**History**:
The ordered collection of Clips that ClipBench keeps, limited by count and age.
_Avoid_: log, archive

**Duplicate**:
A Capture whose content is already in the History. It moves the existing Clip to the top instead of adding a new one, and the existing Clip keeps its pin.
_Avoid_: copy, repeat

**Preview**:
The short single-line text that stands for a Clip in lists. It is cut between whole characters, never in the middle of one.
_Avoid_: summary, snippet text

**Pin**:
To protect a Clip from being removed by the History limits. A pinned Clip stays until the user unpins or removes it.
_Avoid_: favorite, star, lock

**Board**:
A named group of pinned Clips.
_Avoid_: folder, collection, list

**Snippet**:
A reusable piece of text with variables, such as the date, that is filled in when it is pasted.
_Avoid_: template, macro

### Pasting

**Popup**:
The window that opens at the cursor from the global hotkey, for searching, selecting and pasting Clips.
_Avoid_: picker, panel, launcher

**Multi-paste**:
Any way of pasting more than one Clip: batch paste, queue paste, collect session or table paste.
_Avoid_: bulk paste

**Batch paste**:
Pasting several selected Clips at once, joined by a separator.

**Separator**:
The text placed between Clips when they are joined, such as a line break, a comma or a custom text.
_Avoid_: delimiter, joiner

**Queue paste**:
Loading a Queue and then pasting its Clips one at a time, one per Ctrl+V, in order.

**Queue**:
The ordered list of Clips waiting to be pasted by queue paste.

**Collect session**:
A period in which every copy is appended to one growing Clip until the user finishes it. Finishing saves that Clip at the top of the History and puts it on the clipboard. In the app it is called "collecting".
_Avoid_: collect mode

**Table paste**:
Pasting the selected Clips as rows or columns of tab-separated cells, for spreadsheets.

**Transform**:
A change applied to text as it is pasted, such as changing case, trimming or formatting JSON. A Transform never changes the stored Clip.
_Avoid_: filter, formatter

### Privacy

**Sensitive clip**:
A Clip that looks like a secret, such as a card number, key or token. It is masked in the interface and expires quickly.

**Privacy marker**:
A clipboard format that an application sets to ask that its content not be recorded. A Clip that carries one is never captured.

**Exclusion list**:
The applications whose clipboard changes are never captured.

**Incognito**:
The state in which ClipBench captures nothing until it is switched off.
_Avoid_: pause, private mode

**Wipe**:
Deleting the entire History together with its encryption key. The stored values are overwritten before the files are deleted.
_Avoid_: reset, clear all

### Storage

**Master key**:
The random key from which the encryption key and the duplicate-detection key are derived. It is kept on disk only in wrapped form, readable by the current Windows user, and in memory only while ClipBench runs.
_Avoid_: password, secret

**Sealed value**:
Stored data that is encrypted, checked for tampering when read, and tied to the Clip, column and format it was written for. A sealed value that was moved or changed is refused, never returned.
_Avoid_: encrypted blob, ciphertext

**Key check**:
A sealed constant stored with the History. It tells ClipBench at start-up whether the key it has is the one that created the History.

**Retention**:
Removing the oldest unpinned Clips when the History passes its limits on count, age or total size, and removing expired Clips.
_Avoid_: cleanup, eviction

**Start fresh**:
Throwing away a History that can no longer be decrypted, together with its key, and beginning an empty one. ClipBench offers it and never does it without the user's agreement.
_Avoid_: reset, repair

### Windows

**Message window**:
The invisible window that runs on a thread of its own and does every call Windows ties to a window thread: registering shortcuts, hearing clipboard changes and owning the clipboard.
_Avoid_: hidden window, helper window

**Sequence number**:
The counter Windows increases every time any program changes the clipboard. ClipBench uses it to tell one change from another, and to notice that the clipboard changed between a write and a paste.
_Avoid_: change count, version

**Own write**:
Content that ClipBench itself put on the clipboard, tagged with a private format so it is not recorded again. A restored clipboard is tagged too.
_Avoid_: internal copy, echo

**Snapshot**:
A raw copy of everything on the clipboard, privacy markers included, taken before a paste so the user's clipboard can be put back exactly as it was.
_Avoid_: backup, save

**Foreground target**:
The window that had focus when a paste was prepared, with its process and whether Windows would block keystrokes sent to it.
_Avoid_: active window, destination

**Integrity level**:
How much trust Windows gives a program. Windows drops keystrokes sent to a program with a higher level than the sender (User Interface Privilege Isolation), so a paste into an administrator's program fails silently unless ClipBench checks first.
_Avoid_: elevation, privilege

**Shortcut**:
A global key combination registered with Windows, such as Win+Alt+V. The default opens the Popup.
_Avoid_: hotkey, key binding

**Running copy**:
The one ClipBench process that is active for the signed-in user. A second launch asks it to open the Popup and exits.
_Avoid_: instance, main process

### The app

**Popup**:
The compact window that opens with the shortcut: a search box, the list of clips and the key hints. It opens next to the text cursor, or the mouse pointer when the program shows no text cursor, and it hides when it loses focus.
_Avoid_: panel, flyout, window (when the popup is meant)

**Library**:
The clips as the app uses them: held in memory so they can be searched, and kept in step with the encrypted history. A change is made in memory first and then stored.
_Avoid_: cache, database

**Mask**:
The row of dots a clip that looks like a secret shows instead of its text, until the user reveals it. Its length never depends on the clip.
_Avoid_: blur, redaction

**Mark**:
The number a user puts on a clip with Space, or Ctrl+Space while searching, to choose it for a multi-paste. The number is the clip's place in the paste order.
_Avoid_: check, tick, selection (a selection is the highlighted row)

**Queue window**:
The small window in the corner of the screen that shows a running Queue: which clip Ctrl+V pastes next and how to stop. It never keeps the focus and shows no clip content. The code calls it the queue HUD.
_Avoid_: overlay, toast

**Settings**:
The user's choices that last between runs, kept in `settings.json`: the Separator, whether the clipboard is put back after a paste, and whether copies are also kept while collecting. Never clip content.
_Avoid_: preferences, options

**Notice**:
A short message shown next to the tray icon when something that happened in the background did not work, such as a shortcut another program already uses. Its text is always written by the app, never taken from a clip.
_Avoid_: toast, alert, notification

**Activity log**:
The small file of what the app did: event names, kinds, sizes and error codes. It cannot hold a copied text, a file name or a program name.
_Avoid_: debug log, trace

