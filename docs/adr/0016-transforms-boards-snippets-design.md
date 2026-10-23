---
status: accepted
---

# Phase 6 design: transforms, boards and snippets

Phase 6 makes every paste smarter: text can be changed on its way out, clips can be kept in named boards, and reusable text with variables can be pasted. This record holds the decisions that shape the three features. Where PLAN.md said something else, it says so. It is updated in the same phase when a decision changes.

## Decisions

1. **The owner chose how the features feel (2026-10-23).**
   - One sidebar in the popup, opened with Ctrl+B. Its entries are History, Pinned, one entry per board, and Snippets. Ctrl+1 to Ctrl+9 jump to an entry. It is collapsed by default, so the popup looks as it does today.
   - A clip is on at most one board. Putting a clip on a board pins it.
   - Snippets are edited in a small Snippets window, opened from the tray menu and from the Snippets entry in the sidebar. Phase 7 moves the same editor into Settings > Snippets.
   - Transforms are the plan's list and nothing more: plain text, upper case, lower case, title case, sentence case, trim, remove line breaks, JSON format, JSON minify, URL encode and decode, Base64 encode and decode, and find and replace.
2. **A transform is a pure function from text to text.** It lives in Core, takes a string and returns a `Result<string>`, and never throws for bad input: invalid JSON, invalid Base64 and a bad pattern come back as an error with a plain message. A transform never changes the stored clip (GLOSSARY). The output of any chain is plain text, so "paste as plain text" is the chain with no transforms: it pastes the text of an HTML or rich text clip and leaves its formats behind. Images cannot be transformed, and the paste says so, as batch paste does.
3. **Chains run in the order chosen and stop at the first failure.** The error names the transform that failed. Input and output are both capped at the composer's limit (10,000,000 characters), checked before the work starts where the size is known and again after each step.
4. **Case changes use the invariant culture.** Upper and lower case do not depend on the Windows language, so the result is the same on every machine. The consequence for Turkish is that `i` becomes `I` and `ı` stays as it is, and `I` becomes `i`, never `ı`. A test pins this and compares it with the current-culture result. Title case lower-cases the text first and then capitalises the first letter of each word; sentence case capitalises the first letter after a full stop, a question mark, an exclamation mark or a line break. Neither knows about names or acronyms; this is documented in the picker's help text.
5. **Find and replace is bounded.** The pattern is plain text unless the regex switch is on. Regex runs with `RegexOptions.NonBacktracking` where the pattern allows it, a match timeout of one second in every case, and a pattern length cap of 1,000 characters. A timeout or an invalid pattern is an error result. Replacement text in a regex replace uses `$1` style groups, as .NET does; in plain mode it is literal. The search and replacement text are typed by the user for one paste and are never saved, logged or kept in the settings.
6. **URL encoding uses `Uri.EscapeDataString` and `Uri.UnescapeDataString`.** They are in the base library's `System` namespace, so no code in `src/` needs `System.Net` (RULE 3). Encoding is UTF-8 percent encoding of everything outside the unreserved set; decoding fails on a malformed escape instead of passing it through. Base64 is UTF-8 text to standard Base64 and back; decoding accepts missing padding and surrounding white space, and fails on anything else, and on bytes that are not valid UTF-8.
7. **The transform picker works on what Enter would paste.** Shift+Enter opens a list of transforms with a filter box. Ctrl+Space adds the highlighted transform to the chain, as it marks a clip; Enter pastes the transformed text. The picker shows the first 200 characters of the result as a preview. For a sensitive clip the preview is masked like the list is. The input is the marked clips joined by the current separator, or the highlighted clip's text when nothing is marked. Find and replace asks for its two texts in the picker before it runs.
8. **A board is a name and an ordered list of pinned clips.** The name is trimmed, 1 to 64 characters, with no control characters, and unique ignoring case. There can be 100 boards. Board order is creation order. Clips are reordered inside a board with Ctrl+Up and Ctrl+Down while the board is open. Adding a clip pins it. Unpinning a clip takes it off its board, so a board never holds an unpinned clip. Removing a clip from the history, including by expiry, takes it off its board. Deleting a board asks first and keeps its clips: they stay pinned, with no board.
9. **Board names and snippet text are sealed, like clip content.** The names and bodies are what the user typed and can reveal what they do, so they are stored as AES-256-GCM values under the same derived key, bound by their associated data to their record kind, id and column, so a value moved to another row, another column or another kind fails to open. What stays in plain columns is only what ordering and the join need: ids, positions, and a clip's board id and position in the board. A test reads the database file and finds no board name and no snippet text. The metadata is unauthenticated, as for clips (ADR 0009); someone with the file can reorder boards or move a clip between them but cannot read or forge a name.
10. **The schema changes by one migration, 2, and an existing history survives it.** It adds a `boards` table, a `snippets` table, and two nullable columns on `clips` (board id and position in the board). A migration test starts from a version 1 database with clips in it and checks every clip is still readable afterwards. Version 1 is not edited.
11. **Snippet syntax.** A variable is a name in braces; the name is case-insensitive.
    - `{date}` is today's date as `yyyy-MM-dd`; `{date:FORMAT}` uses a .NET custom date format.
    - `{time}` is the time as `HH:mm`; `{time:FORMAT}` uses a custom format.
    - `{clipboard}` is the text of the newest text clip in the history, so a copy that carries a privacy marker or comes from an excluded program, which is never in the history, can never be pasted by a snippet. With no text clip it is empty.
    - `{input:Name}` asks the user for a value before the paste. The same name is asked once. A name is 1 to 40 letters, digits, spaces, hyphens or underscores. Cancelling the prompt cancels the paste.
    - `{{` is a literal `{` and `}}` is a literal `}`.
    - Anything else in braces, a variable with a bad format or name, and a brace that is never closed is left exactly as written, and the engine reports a warning. The editor shows the warnings; the paste still goes ahead.
    Dates and times use the invariant culture and the local time zone from the injected `TimeProvider`, so the same snippet reads the same on any machine and a test can fix the clock.
12. **Expansion is one pass and capped.** The body is read once from left to right. Values (the clipboard text, typed input, dates) are inserted and never read again, so a value that contains `{date}` stays as it is and no snippet can expand itself. Limits: a body of at most 10,000 characters, a date or time format of at most 40, an input value of at most 10,000, at most 20 different input names, and an expansion of at most 100,000 characters. Going over a limit is an error result, never a cut-off paste.
13. **Parsing and expanding are two steps.** `Parse` returns the input names that must be asked for and the warnings; `Expand` takes the values and the context (time, clipboard text) and returns the text. The picker for input values is built on the first step and needs no clock.
14. **Nothing here logs content.** The activity log gets event types, transform ids (a fixed list), counts, sizes and error codes. Never a find or replace text, a board name, a snippet name or a snippet body. The existing log-content test is extended to cover them.

## Considered options

- **Several boards per clip.** Rejected by the owner for now. It needs a join table with an order per board and a meaning for "remove from board", for little gain.
- **Snippets mixed into the clip list.** Rejected. A snippet is not a clip, and marks, queue and table paste have no sensible meaning for it.
- **A `/` prefix for snippet search.** Rejected. A search that starts with a slash, such as a path, would be read as a snippet search.
- **`{clipboard}` reads the live clipboard.** Rejected. It would bring in copies that privacy markers keep out of the history.
- **A template language with conditionals or loops.** Rejected. Variables alone are enough, and anything more makes the snippet text a program that has to be bounded.
- **`System.Net.WebUtility` for URL encoding.** Rejected to keep `System.Net` out of `src/` entirely.
- **Rewriting the existing `clips.is_pinned` column into the board.** Rejected. Pinned clips without a board stay a valid state.

## Consequences

- The popup grows a sidebar and a picker, which are new keyboard surfaces; the manual QA list covers keyboard use and a screen reader for both.
- Board membership and positions are visible metadata, like pin flags; the threat model says so.
- The `{clipboard}` rule means a snippet cannot paste something the user copied from a password manager. That is intended.
- A long regex over a large input can still take up to a second before it reports a timeout, and the paste waits for it.
- Dates are not in the user's language. A snippet that needs month names gets English ones. A setting can be added later if it is wanted.
- No new packages. The regex engine, JSON and Base64 are in the base library.
