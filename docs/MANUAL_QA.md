# Manual QA checklist

What a person checks by hand, because no test can: how things look, how they feel, and what other programs do with a paste. It grows with every phase. Run it on a normal Windows 11 account, not as administrator, with the build you want to release.

How to start a build for checking: `dotnet build -c Release`, then run `src\ClipBench.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\ClipBench.App.exe`. The default shortcut is **Win+Alt+V**.

## Phase 4: the popup, the tray icon and pasting one clip

### Start and stop

- [ ] Starting the app shows an icon in the notification area (it may be in the overflow flyout). Nothing else appears on screen.
- [ ] Hovering over the icon shows "ClipBench · capturing".
- [ ] Starting the app a second time does not start a second copy: the first copy's popup opens instead.
- [ ] Left-click the icon: the popup opens near the pointer. Pick a clip with Enter while a text field in another program had focus before you clicked: the clip appears there.
- [ ] Right-click the icon: the menu shows **Open ClipBench** (bold), a line, **Pause capturing** and **Exit**. Click away: the menu closes.
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
- [ ] **Space** with an empty search box marks the highlighted clip with a tick (marks are for the multi-paste of a later phase). With text in the search box, Space types a space.
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

## Tests that need a person

These send real keys or use the real clipboard, so nobody may type while they run.

```
dotnet test --project tests/ClipBench.UiSmokeTests -c Release --filter-trait "Category=Integration"
dotnet test --project tests/ClipBench.Platform.Windows.IntegrationTests -c Release --filter-trait "Category=Integration"
```

The paste checks skip with a reason when Windows will not let a test window take focus: run them from a terminal that is in front.
