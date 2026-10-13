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
A period in which every copy is appended to one growing Clip until the user finishes it.
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
