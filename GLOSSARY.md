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
Deleting the entire History together with its encryption key.
_Avoid_: reset, clear all
