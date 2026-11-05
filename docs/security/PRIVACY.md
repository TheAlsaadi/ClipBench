# Privacy

This page says, in plain words, what ClipBench records, what it hides, how long it keeps things, and how you delete them. Everything here can be checked in the Settings window (tray icon, **Settings…**, or **Ctrl+E** in the popup) and in the [threat model](THREAT_MODEL.md).

## The promise

ClipBench makes **no network connections**: no telemetry, no analytics, no update check, no cloud sync. Your history never leaves this PC. The code is tested to reference no networking library. The one thing that touches the internet is a link you click yourself: **Settings → About → the project's page** asks Windows to open it in your own browser.

## What is recorded

Text, rich text, images and lists of files you copy, with the time and the name of the program you copied from. Each is stored **encrypted** (AES-256-GCM) in a database in `%LOCALAPPDATA%\ClipBench`. The key is protected by Windows for your account only. Clip content, previews and program names are never written to disk unencrypted, and never to the log, which holds only kinds of events, sizes and numbers.

## What is never recorded

- **Anything that asks not to be.** Copies that carry the markers `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore` or `CanIncludeInClipboardHistory = 0` (password managers use them) are skipped.
- **Programs on your exclusion list** (Settings → Privacy). It starts with common password managers and the Windows credential prompt. A program matches by its file name, such as `KeePass.exe`, in any case and from any folder. You can add programs, remove any entry (even all of them) and restore the default list.
- **Everything, while incognito is on.** Turn it on from the tray menu (**Turn on incognito**) or Settings → Privacy. The tray icon changes to a greyed icon with a slash and the tool tip says "incognito". It is never saved: it is off again the next time ClipBench starts.
- Anything over the size limits (2 MiB of text, 20 MiB of picture).

## Secrets

ClipBench looks for private keys, web tokens, card numbers, IBANs and long random-looking tokens. It is a heuristic and can miss a secret or flag something harmless.

- A clip that looks like a secret is **hidden** in the popup (shown as dots) until you reveal it with **Ctrl+R**, and its preview in the transform picker is hidden too. You can turn hiding off in Settings → Privacy.
- It is **deleted after two minutes** by default, from memory and from disk. Choose 15 seconds to an hour. **Pinning** a clip, or putting it on a board, cancels this: you chose to keep it. Copying the same secret again starts the time again.
- The popup and the Settings window are **kept out of screenshots and screen sharing** while "Keep ClipBench's windows out of screenshots" is on, which is the default. Turning it off applies at once.

## How long things are kept

Settings → History: at most 1,000 unpinned clips (10 to 10,000), for 30 days (1 to 365), within 256 MB (16 to 2,048). Pinned clips are never removed by these limits. Lowering a limit removes the clips that no longer fit straight away.

## Delete everything

Settings → Privacy → **Delete everything…**, then confirm. Every clip (pinned ones too), board and snippet is deleted, and the database file is rebuilt and its log emptied so the old values do not stay in the files. A collect session or paste queue in progress is ended. If the disk cannot be wiped, ClipBench says so and still shows what is still there. To remove the key and the database files as well, delete the ClipBench folder, or choose to start fresh when ClipBench offers it after a history it cannot read.

## Copying text from the screen

Press **Win+Alt+O** (or **Copy text from the screen** in the tray menu), drag a rectangle over the text, and let go. The text is read on your PC by the text recognition that is part of Windows, put on your clipboard and added to your history like any copy. **Esc** or a right click cancels.

- Only the rectangle you drag is read, into memory. The picture is **never saved**, not even temporarily, and it is cleared from ClipBench's memory the moment the text has been read. (Windows' recognizer keeps its own copy for a moment while it works, which ClipBench cannot clear.) Because only text is kept, there is no picture to pin.
- The dimmed overlay and its border are never part of what is read, and are kept out of screenshots.
- Text that looks like a secret is hidden and expires, like any copy. With **incognito** on, the text goes on your clipboard but is not added to your history, and the notice says so.
- ClipBench tells you how many characters were copied, never which.
- It needs a text-recognition language for the languages in your Windows profile. If there is none, ClipBench says where to add one: Settings, Time and language, Language and region, Language options, Text recognition.

## What the popup knows about a clip

The popup can say what a clip is: a **color** (hex, `rgb()` or `hsl()`, with a small swatch), a **link**, an **email address**, a **phone number** or **code** (with a guess of the language). It only looks at the text of the clip, and only when the whole text is exactly that one thing. It never fetches or looks up anything. A link is only opened when you choose **Open the link in your browser** from the right-click menu, and then Windows opens your own browser; ClipBench makes no connection. A secret that is hidden shows nothing about what it is.

## Starting with Windows

It is **off by default** and ClipBench asks once, the first time it runs. Turning it on adds one value named `ClipBench` to your own `Run` key in the registry (the path of the program, nothing else). It needs no administrator rights. Turning it off removes only that value.

## What the settings file holds

`%LOCALAPPDATA%\ClipBench\settings.json` is plain text. It holds your choices: the separator, the history limits, the secret time, the switches, your two shortcuts and the **names of the programs on your exclusion list**. It never holds clip content. Note that the exclusion list shows which password manager you use; anyone who can read your files can read it. Everything else you copy is encrypted.

## What is not defended

Software running as you can read what you paste, read the settings file and change it, and read ClipBench's memory. ClipBench cannot protect a clip from the program you paste it into, or from a program that watches the clipboard while the clip is on it. See the threat model's list of assumptions.
