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

## Tests that need a person

These send real keys or use the real clipboard, so nobody may type while they run. The smoke tests also run batch, table, queue and collect paste; the collect test saves what is on your clipboard first and puts it back afterwards.

```
dotnet test --project tests/ClipBench.UiSmokeTests -c Release --filter-trait "Category=Integration"
dotnet test --project tests/ClipBench.Platform.Windows.IntegrationTests -c Release --filter-trait "Category=Integration"
```

The paste checks skip with a reason when Windows will not let a test window take focus: run them from a terminal that is in front.
