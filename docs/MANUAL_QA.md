# Manual QA checklist

What a person checks by hand, because no test can: how things look, how they feel, and what other programs do with a paste. It grows with every phase. Run it on a normal Windows 11 account, not as administrator, with the build you want to release.

How to start a build for checking: `dotnet build -c Release`, then run `src\ClipBench.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\ClipBench.App.exe`. The default shortcut is **Win+Alt+V**.

## Phase 4: the popup, the tray icon and pasting one clip

### Start and stop

- [ ] Starting the app shows an icon in the notification area (it may be in the overflow flyout). Nothing else appears on screen.
- [ ] Hovering over the icon shows "ClipBench · capturing".
- [ ] Starting the app a second time does not start a second copy: the first copy's popup opens instead.
- [ ] Left-click the icon: the popup opens near the pointer. Pick a clip with Enter while a text field in another program had focus before you clicked: the clip appears there.
- [ ] Right-click the icon: the menu shows **Open ClipBench** (bold), a line, **Start collecting**, **Restore clipboard after paste** (ticked), **Pause capturing** and **Exit**. Click away: the menu closes.
- [ ] **Exit** closes the app and removes the icon at once (no ghost icon is left when you move the pointer over it).
- [ ] Restart Explorer (Task Manager, right-click Windows Explorer, Restart): the ClipBench icon comes back by itself.

### Capturing

- [ ] Copy text in Notepad, Word, a browser and VS Code. Each copy appears at the top of the popup, with the program's name and "Just now".
- [ ] Copy an image (a screenshot, or "Copy image" in a browser). It appears with a small picture and its size.
- [ ] Copy files in Explorer (Ctrl+C on two or three files). The row shows their names, not their folders.
- [ ] Copy the same text twice: it is listed once and moves to the top.
- [ ] Copy a password from a password manager that marks its copy as private (KeePass, 1Password, Bitwarden): it never appears.
- [ ] Choose **Pause capturing**: copy something, then open the popup: it is not there. The tool tip says "paused" and the item now says **Resume capturing**. Resume, copy again: it appears.

### The popup

- [ ] **Win+Alt+V** opens the popup next to the text cursor in Notepad, and next to the mouse pointer in a browser. The search box has the cursor.
- [ ] It opens fully on screen when the cursor is near the right edge or the bottom of the screen, on a second monitor, and on a monitor with a different scale.
- [ ] Typing filters the list after a short pause. A search with no match says "No clips match your search."
- [ ] **Up** and **Down** move the highlight and wrap around at the ends; the highlighted row stays in view in a long list.
- [ ] **Esc** closes the popup. Opening it again shows an empty search box and the whole list.
- [ ] Clicking another window, or pressing Alt+Tab, closes the popup. It never shows in the taskbar or in Alt+Tab.
- [ ] **Ctrl+P** pins the highlighted clip (a pin mark appears) and again unpins it.
- [ ] **Delete** (with the cursor at the end of the search box) removes an ordinary clip at once. On a pinned clip the first press asks ("Press Delete again…"); **Esc** cancels the question without closing the popup.
- [ ] **Space** with an empty search box marks the highlighted clip with a number, its place in the paste order (see Phase 5). With text in the search box, Space types a space, and **Ctrl+Space** marks.
- [ ] Copy a token or a card number (for example a test card number such as 4111 1111 1111 1111): its row shows dots instead of the text; **Ctrl+R** reveals it and again hides it.
- [ ] Mouse: clicking a row pastes it.

### Pasting one clip

For each of **Notepad, Word, a browser text box, VS Code**: put the cursor in a text field, press Win+Alt+V, pick a clip with Enter.

- [ ] The popup closes and the clip appears at the cursor, once.
- [ ] What you had copied before is back on the clipboard afterwards (paste it somewhere).
- [ ] Pasting an image into Word or Paint inserts the picture; pasting a copied file list into Explorer copies the files.
- [ ] Pasting into a program that runs **as administrator**: nothing is typed, a notice says Windows does not allow it, and the clip is on the clipboard so Ctrl+V works.
- [ ] Pressing the shortcut while another program holds the same shortcut (start a second program that registers Win+Alt+V first): a notice says the shortcut is taken and the tray icon still opens the popup.

### Looks and access

- [ ] Dark mode and light mode (Settings, Personalization, Colors): the popup follows the system and every text is readable. (The tray menu is a classic Windows menu and follows the theme less closely: known, see ADR 0014.)
- [ ] High contrast (Settings, Accessibility, Contrast themes): borders and text are visible; the highlighted row is clear.
- [ ] Scale 100%, 125% and 150% (Settings, Display): the popup keeps its proportions and nothing is cut off.
- [ ] With Narrator on, moving through the list reads each row: kind, text, source, age and "pinned" or "selected". A hidden clip reads "hidden sensitive content" and never its text.
- [ ] Everything can be done without the mouse.
- [ ] Turn on screen sharing (Teams or Zoom) or take a screenshot with Win+Shift+S while the popup is open: the popup is not in the picture.

### Safety

- [ ] After a session with copies from a password manager and of long texts, open `%LOCALAPPDATA%\ClipBench\logs\clipbench.log`: it holds only event names, sizes, kinds and codes, never any text you copied.
- [ ] `%LOCALAPPDATA%\ClipBench\clipbench.db` opened in a text editor shows nothing readable.
- [ ] Task Manager shows no network activity attributed to ClipBench, and the firewall never asks about it.

## Phase 5: batch, queue, collect and table paste

### The four demo scenarios (these must pass before v0.1.0 is tagged)

- [ ] **Five email addresses into a "To" field.** Copy five email addresses. Click into the To field of a mail program (Outlook, or Gmail in a browser), open the popup, mark the five with **Space** in the order you want them (**Ctrl+Space** marks while you search), and press **Ctrl+S** until the footer shows "Comma and space". Press **Enter**: the field gets the five addresses separated by ", ", in the order you marked them.
- [ ] **A four-field web form with queue mode.** Copy a name, a street, a city and a postcode. Open the popup, mark the four in that order and press **Ctrl+Q**. The popup closes and a small window at the bottom-right of the screen says "Queue 1/4 · Esc to stop". Click into the first field and press **Ctrl+V**, press Tab, Ctrl+V, and so on. Each Ctrl+V pastes the next value, the small window counts up, and after the fourth it goes away. Ctrl+V then pastes what you had copied before, as usual.
- [ ] **Ten quotes from an article.** Press **Win+Alt+C**: a notice says ClipBench is collecting. Copy ten passages from an article in a browser; the tray tool tip shows "collecting 10". Press **Win+Alt+C** again: a notice says ten copies were collected into one clip. Paste into Word: the ten passages, one per line, in the order you copied them. The popup shows that one clip at the top, and not the ten single copies.
- [ ] **Six values as a column in Excel.** Copy six numbers. Click a cell in Excel, open the popup, mark the six and press **Ctrl+T**: they fill one column downwards. **Ctrl+Shift+T** fills a row instead. A value with a line break or a tab inside stays in one cell.

### Batch and table paste

- [ ] Each mark shows a number: the clip's place in the paste order. Unmarking a clip renumbers the others. Marks stay while you search for more clips, and are gone when the popup closes.
- [ ] With three clips marked, the footer says "Enter paste 3 · Ctrl+S New line · …" and the Paste button says "Paste 3".
- [ ] **Ctrl+S** moves through New line, Blank line, Comma and space, Comma, Semicolon, Tab, Space and Nothing. The choice is still there after you quit and restart ClipBench.
- [ ] The separator button lists the same choices and **Custom…**. Type ` | ` or `\t` and press Enter: the button shows the custom separator in quotes and the next batch paste uses it. An unknown escape such as `\q` shows a message and changes nothing.
- [ ] A picture marked together with text: Enter keeps the popup open and says "Images cannot be joined into text…". One marked picture on its own pastes as a picture.
- [ ] One marked clip pastes exactly as Enter on that clip would: rich text stays rich in Word.

### Queue paste

- [ ] While a queue runs, the small window never keeps the focus: the text cursor stays in the form. Clicking the small window gives the focus straight back to the form.
- [ ] **Esc** stops the queue at once, whichever program is in front. The small window goes away, and Esc and Ctrl+V work as usual again in every program.
- [ ] While a queue runs, the tray tool tip says "paste queue running" and the tray menu has **Stop paste queue**, which stops it.
- [ ] Pasting from the popup while a queue runs ends the queue first and pastes what you chose.
- [ ] Known limit (ADR 0015): pressing Ctrl+V twice within about a quarter of a second pastes the same clip twice, and the queue counts only one. At a normal pace (Ctrl+V, Tab, Ctrl+V) each press takes the next clip.
- [ ] A queue paste into a program that runs as administrator stops the queue with a notice.
- [ ] Untick **Restore clipboard after paste** in the tray menu: after a queue paste the last queued clip is still on the clipboard. Tick it again: your earlier copy is back after each paste. The tick is remembered after a restart.
- [ ] The small window is not in screenshots or screen sharing (Win+Shift+S, Teams or Zoom).

### Collecting

- [ ] The tray menu's **Start collecting** starts a session. The menu then says **Finish collecting (n)** with the count, and offers **Cancel collecting**.
- [ ] The popup's **Collect** button, or **Ctrl+L**, starts a session and closes the popup. While collecting, the footer says "Collecting n · Ctrl+L finish". Ctrl+L again finishes, and the popup shows the new clip at the top.
- [ ] **Cancel collecting** throws the session away: nothing new appears in the history.
- [ ] A picture copied during a session is saved as a clip of its own and is not added to the collected text.
- [ ] A password copied from a password manager during a session is neither collected nor saved.
- [ ] If another program already uses Win+Alt+C, a notice at start-up says so, and the tray menu still collects.

### Looks and access

- [ ] Dark mode, light mode and high contrast: the footer buttons, the mark numbers and the small queue window are readable.
- [ ] With Narrator on: a marked row reads "selected, number 2 in paste order"; the footer buttons read Paste, Separator (with the current choice), Queue, Table and Collect; the queue window reads "Paste queue, next clip 1 of 4. Press Esc to stop."
- [ ] Everything above can be done without the mouse.

### The settings file

- [ ] `%LOCALAPPDATA%\ClipBench\settings.json` holds only the separator choice, the custom separator and two switches, never any copied text.
- [ ] Replace its contents with nonsense and restart: ClipBench starts with the defaults and keeps the bad file beside it as `settings.json.bad`.

## Phase 6: transforms, boards and snippets

### Transforms (Shift+Enter)

- [ ] Copy `  hello world  `. Open the popup, press **Shift+Enter**: a list of transforms covers the list, with a preview under it. Type `case` to narrow the list, move to **UPPER CASE** and press **Enter**: `  HELLO WORLD  ` is pasted. The clip in the history is unchanged.
- [ ] Press **Shift+Enter** again, **Ctrl+Space** on **Trim**, **Ctrl+Space** on **Title Case**: the line above the list says "Trim → Title Case" and the preview shows `Hello World`. **Backspace** (with the filter empty) takes Title Case off. **Enter** pastes the chain.
- [ ] Mark three clips (Space) and press **Shift+Enter**, choose **Remove line breaks**: the three are joined by the separator, then put on one line.
- [ ] Copy rich text from Word, open the popup, **Shift+Enter**, **Plain text**: it pastes without the formatting.
- [ ] Copy `{"a":1,"b":[1,2]}`: **JSON format** pastes it over several lines with two-space indents, **JSON minify** puts it on one line again. Copy `not json`: the preview says "JSON format: The text is not valid JSON (line 1)." and **Enter** pastes nothing.
- [ ] **URL encode** on `a b&c` gives `a%20b%26c`; **URL decode** reverses it. **Base64 encode** on `hello` gives `aGVsbG8=`; **Base64 decode** reverses it, with or without the `=`. A broken escape such as `100%` is refused with a message.
- [ ] **Find and replace…**: Enter asks for the text to find and the text to put in its place. With "Use a pattern" ticked, `item (\d+)` and `#$1` turn `item 12` into `#12`. An invalid pattern such as `(` shows a message and keeps asking. Esc goes back to the list.
- [ ] A pattern that would hang a simpler engine, such as `(a+)+$` on a long line of `a`s, finishes at once. Nothing you typed in the find and replace boxes is there the next time.
- [ ] A copied token or card number shows dots in the preview, and the transform still works on Enter.
- [ ] A picture cannot be transformed: the popup stays open and says so.
- [ ] Narrator reads the list, the chain and the preview; everything can be done without the mouse.

### Boards and the sidebar

- [ ] **Ctrl+B** shows a sidebar with **History**, **Pinned** and **Snippets**; the popup grows to the right and moves left if it would leave the screen. Ctrl+B again hides it and the popup shrinks.
- [ ] **Ctrl+N** opens a name box: type `Work`, **Enter**: the board appears and is chosen. A blank name, a name that is already used (in other letters too) and a name over 64 characters each show a reason and keep the box open. **Esc** gives up.
- [ ] Select a clip and press **Ctrl+D**: a menu lists the boards and "New board…". Choose `Work`: the clip is pinned, the count in the sidebar goes up, and **Ctrl+3** shows the board with the clip.
- [ ] Put three clips on the board, then **Ctrl+Up** and **Ctrl+Down** move the highlighted clip, and the highlight goes with it. **Ctrl+Shift+D** takes the highlighted clip off the board; it stays pinned.
- [ ] **Ctrl+2** shows only pinned clips; unpinning one there (Ctrl+P) makes it leave, and leave its board.
- [ ] **F2** renames the board. **Ctrl+Shift+Delete** asks "Delete the board … Its clips stay pinned" and a second press deletes it: the clips are still pinned, on no board.
- [ ] Quit and start ClipBench again: the boards, their order and the order of the clips on them are as you left them.
- [ ] High DPI and a narrow screen: the footer buttons all fit; with the sidebar open nothing is cut off.
- [ ] Narrator reads the sidebar entries with their counts ("Board Work, 3 clips").

### Snippets

- [ ] Tray menu → **Snippets…** (or **Ctrl+E** in the popup's Snippets view) opens the Snippets window. **New snippet**: name `Reply`, text `Dear {input:Name}, today is {date:dddd}.` The warnings list stays empty, and the preview shows `Dear <Name>, today is Saturday.` (the weekday of the day you test). **Ctrl+S** saves.
- [ ] Type `{nope} and {date` in a snippet: two warnings with their line numbers appear, and the snippet still saves.
- [ ] Change the text without saving, then click another snippet: it refuses and says to save or discard. **Esc** discards. Delete asks first.
- [ ] Open the popup in Word, press **Ctrl+B** and choose **Snippets** in the sidebar (Ctrl+PageDown walks the entries), highlight `Reply`, **Enter**: a small form asks for Name. Type `Sam`, **Enter**: `Dear Sam, today is …` is pasted into Word. **Esc** in the form pastes nothing.
- [ ] `{clipboard}` pastes the text you copied last. A password copied from a password manager is not pasted by a snippet.
- [ ] The Snippets window and the popup's snippet list are not in screenshots or screen sharing.
- [ ] Quit and start again: the snippets are there.

### The mouse

- [ ] Open the popup and use only the mouse for this whole list. The button beside the search box (three lines) shows and hides the sidebar; the **X** closes the popup.
- [ ] Move the pointer over a clip: four small buttons appear on the right of its row (select, pin, board, delete). **Pin** pins it (its icon turns into an unpin icon); **board** opens the list of boards; **delete** deletes an unpinned clip, and on a pinned one asks you to press it again.
- [ ] Right-click a clip: a menu lists Paste, Change the text then paste, Select, Pin, Add to a board, Delete (and Take off its board, Move up and Move down inside a board, Show or Hide for a secret), each with its shortcut. Each does what its shortcut does.
- [ ] Highlight a clip with the arrow keys and press the **Menu** key (or **Shift+F10**): the same menu opens, and **Esc** closes only the menu.
- [ ] Right-click an entry in the sidebar: a board offers Rename and Delete, and any entry offers New board. Delete shows **Delete** and **Keep** buttons in the sidebar. Making a board shows **Save** and **Cancel** buttons beside its name.
- [ ] Click an entry in the sidebar, then type: the text goes to the search box, and the arrow keys move in the list.
- [ ] Open the transform picker from the right-click menu: the **Paste**, **Add to chain**, **Remove last** and **Back** buttons do what Enter, Ctrl+Space, Backspace and Esc do.
- [ ] **Separator** → **Custom…**, type `|`, **Enter**: the box closes and nothing is pasted.

### Lock and move

- [ ] Open the popup over another program and press the **lock** button next to the **X** (or **Ctrl+K**): the icon closes and the button is highlighted. Click the other program: the popup stays on top of it.
- [ ] With the popup locked, click a clip: it is pasted into the program you were in, which keeps the cursor where it was, and the popup stays. Click another clip: it is pasted too.
- [ ] Switch to a different program, then click a clip in the locked popup: it is pasted into that program. Try it as the owner did: type in program A, open the popup there and lock it, click the search box in program B (a browser), then click clips: they go into B, not A, and B keeps the cursor.
- [ ] Drag the handle quickly, in big flicks, across the screen: the popup stays under the pointer with no jumps.
- [ ] Click the search box of a locked popup and type: the text goes into the search box. **Esc** gives the cursor back to the program; the popup stays.
- [ ] Press the popup's shortcut while a locked popup is behind another program: it comes to the front with the cursor in the search box. Press it again: the cursor goes back to the program.
- [ ] Press the lock button again: the popup is an ordinary popup, and clicking another program puts it away. A locked popup is closed only by its **X**, and opens unlocked next time.
- [ ] Drag the handle (the dotted grip left of the lock) across the screen, locked and unlocked, onto the other monitor and back: the popup follows the pointer, keeps its size relative to the screen on each monitor and stays in front. Let go of it half over the edge of a screen: it slides back inside.

### Looks on every screen

- [ ] Open the popup with the sidebar shown, on every monitor you use, at its display scale (try 100% and 125%). Nothing is cut off: the last footer button (Collect) and the right edge of the list show with an even margin, and the corners are rounded with a thin border and no bright outline.
- [ ] With monitors at different scales, the text is the same size relative to the screen on each. (Seen while testing on a second monitor at 125%: the popup's contents looked drawn at 100% inside a larger window. That is older than this phase and is not fixed here.)

### The database

- [ ] After making boards and snippets, open `%LOCALAPPDATA%\ClipBench\clipbench.db` in a hex editor: neither a board name nor any snippet text can be found in it, or in the `-wal` file.

## Tests that need a person

These send real keys or use the real clipboard, so nobody may type while they run. The smoke tests also run batch, table, queue and collect paste; the collect test saves what is on your clipboard first and puts it back afterwards.

```
dotnet test --project tests/ClipBench.UiSmokeTests -c Release --filter-trait "Category=Integration"
dotnet test --project tests/ClipBench.Platform.Windows.IntegrationTests -c Release --filter-trait "Category=Integration"
```

The paste checks skip with a reason when Windows will not let a test window take focus: run them from a terminal that is in front.
