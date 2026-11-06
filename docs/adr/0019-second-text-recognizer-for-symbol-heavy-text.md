---
status: accepted
---

# A second text recognizer for symbol-heavy text (Tesseract)

**Accepted on 2026-11-05** (see "Decision" below). The trial this record asked for was run on 2026-11-05 with the owner's permission, in a folder outside the repository, and met every threshold. Facts about packages were read on 2026-11-05 from nuget.org, GitHub and Microsoft Learn, and from the package itself after it was downloaded.

## Why

Phase 8 reads text from the screen with the recognizer built into Windows (`Windows.Media.Ocr`, ADR 0018). It fails in one family of cases that margins, enlargement and merging of languages could not fix:

- **It drops or scrambles text that is mostly symbols.** In 252 trials `>` survived 65% of the time. `x > 0` came back empty, `C:\Users\me> dir` came back as `dir`, and `=> value` as `value`.
- **On the owner's VS Code terminal screenshot (a long path in Cascadia Mono, light on dark), both English engines returned nothing for the path.** Only the Arabic engine read it, and it returned the pieces of the path in right-to-left order inside one word, read the slashed zero as `ø`, and the bullet icon as `O`. That is its raw output, before any ClipBench code.
- **Adding the Arabic engine costs accuracy on plain English.** It rescues code (0.71 to 0.835), but on prose it lowers 0.975 (English engine alone) to 0.922, because it scrambles numbers and punctuation and the letter-count choice sometimes prefers it (`Order No. 5081-B6 Total $1,204.75` came back as `Order B6-No.SO81 Total S1,2O4.75`).

Tried and not enough: margins, 2x to 5x enlargement, bicubic filters, inversion, grayscale, thresholding, `en-GB`, and taking each word from the engine that reads it best (which did fix the Arabic-name and chevron case, ADR 0018 decision 16).

## What is proposed

Add a **second recognizer behind the existing seam** (`IWordRecognizer`, ADR 0018 decision 16): a Tesseract engine for the Latin script. Windows would keep reading Arabic and Hebrew, where Tesseract needs extra models and was seen to give junk (`ichalltax0`, confidence 0, for an Arabic name that Windows read exactly). The merge code already combines readings by word position, so the Arabic words come from Windows and everything else from Tesseract.

Nothing else about the design changes: only text is kept, never pixels, no elevation, nothing written to disk by the recognizer.

## The candidates

| Option | Maintainer and license | Verdict |
|---|---|---|
| **`TesseractOCR` 5.5.2** (NuGet, last updated 2026-03-05) | Kees van Spelde (`Sicos1977`), Apache-2.0; credits the original wrapper's author Charles Weld. About 752,000 downloads. | **The one the trial used, and it passed.** Single maintainer. Contains Tesseract 5.5.0 (what the native library reports) while upstream is at 5.5.4. **It has no ARM64 build**: the package holds only `x86` and `x64` native folders, whatever its README says. |
| `Tesseract` 5.2.0 (NuGet, `charlesw`) | Charles Weld, Apache-2.0. About 11 million downloads. | **Rejected.** Last updated 2022-11-30, wraps Tesseract 5.2.0, x86 and x64 only. |
| **Windows AI text recognition** (`Microsoft.Windows.AI.Imaging.TextRecognizer`) | Microsoft, part of the Windows App SDK the project already references, so no new package. | **Not usable as the answer.** Microsoft's documentation says it runs only on devices with a neural processing unit. The owner's machine (Intel Core i7-13700KF desktop, no NPU found) cannot run it. Its model is installed by Windows on first use, which would need a decision under ADR 0002. A possible later optional path. |
| Bundle `tesseract.exe` and start it as a process | Tesseract: Apache-2.0. | **Rejected.** An unsigned third-party program with screen content on its input; the picture would go through a pipe as an encoded image (a file-format parser reads it) or a temp file, which the security rules forbid. |
| ONNX Runtime with an open model (PaddleOCR family) | Microsoft (runtime, MIT) and the model authors (Apache-2.0). | **Not evaluated.** We would write the detection, recognition and decoding ourselves. |
| Keep Windows only | n/a | Costs nothing. The limits above stay. |

## Trial results (2026-11-05, Intel Core i7-13700KF, Windows 11 build 26300, x64)

Same drawn pictures and the same measure as the earlier Windows measurements, with ClipBench's real recognizer (English and Arabic engines, margin, 3x enlargement, merge) run in the same program as the baseline. The settings (3x enlargement, one uniform block of text, light-on-dark pictures inverted) were chosen on the code pictures only; across all 18 settings tried the code score stayed between 0.941 and 0.960.

| Measure | Threshold | ClipBench today | Tesseract |
|---|---|---|---|
| Code-like text, mean similarity (120 pictures) | at least 0.85 | 0.835 | **0.958** |
| `>` kept in the 252 cases | at least 90% | 65% | **93%** |
| Code pictures read empty | at most 5 of 120 | 0 | **0** |
| Owner's terminal screenshot, characters right | at least 90% | 43.9% | **96.3%** (every slashed zero, read as `@`, counts as wrong) |
| Prose, mean similarity (72 pictures) | not below 0.95 | 0.922 | **0.994** |
| Time for a 1000 x 100 pixel selection | at most 1 s | 71 ms median | **74 ms median**, 76 ms slowest |

All six thresholds are met. Two things in the table differ from this record's first version: the first version quoted 0.72 for code from the English engine alone, while the real current figure with both engines is 0.835, and it quoted 0.97 for prose, which is true of the English engine alone and not of today's two-engine reading (0.922).

What else was measured:

- **Raw pixels work.** A Leptonica picture is created with `Image.Create` and filled word by word; no file or image-format decoder runs (`LoadFromFile` and `LoadFromMemory` were never called). Words come back with text, confidence and boxes.
- **No network and no files in use.** While 25 reads ran, no TCP or UDP endpoint appeared for the process (sampled every 50 ms), and no file was created or changed in the user temp folder or beside the program. The model is read from the install folder.
- **But the native library contains a network client.** `tesseract55.dll` imports `socket`, `connect`, `send`, `recv`, `select`, `getaddrinfo`, `WSAStartup`, `WSACleanup` and `closesocket` from `ws2_32.dll`, and contains the text `http://`. This is almost certainly the libcurl that Tesseract uses to open an image from a URL. ClipBench would never pass it a URL, and the runtime check above saw nothing. It is still shipped code that can make connections, and ADR 0002 and the privacy document promise there are none. See "Decisions".
- **Size.** x64 native files 7.1 MB (`leptonica-1.85.0.dll` 4.3 MB, `tesseract55.dll` 2.8 MB), `eng.traineddata` 4,113,088 bytes (SHA-256 `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`), managed wrapper 0.16 MB, logging abstractions 0.07 MB: about 11.3 MB uncompressed. The x86 files and `tesseract.exe` that come in the package would be left out.
- **Memory.** About 35 MB more working set once the engine has read one picture; a 1900 x 600 pixel selection took the process peak to 211 MB (the enlarged copy and Tesseract's own working buffers). Start-up of the engine was 66 ms.
- **Confidence is not a general filter.** Dropping words under confidence 30 lowered code from 0.958 to 0.877 and `>` kept from 93% to 86%. It is only useful for the zero-confidence junk Tesseract gives for scripts it has no model for, and the merge already takes those words from Windows.
- **What it still gets wrong.** Slashed zeros come back as `@`, a bullet icon as `®`, and on the owner's breadcrumb one chevron gained a stray `»`. These are font and icon shapes.

## What would be added (if approved)

Restore on .NET 10 pulled exactly three packages for the trial:

| Item | Version | License | Publisher | Role |
|---|---|---|---|---|
| TesseractOCR | 5.5.2 | Apache-2.0 | Kees van Spelde | wrapper and the native `tesseract55.dll` and `leptonica-1.85.0.dll` |
| Microsoft.Extensions.Logging.Abstractions | 10.0.3 | MIT | Microsoft | dependency of the wrapper; ADR 0012 decided against logging packages, so no ClipBench code would use it |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.3 | MIT | Microsoft | dependency of the one above (the app already uses this package, ADR 0012) |
| `eng.traineddata` from `tessdata_fast` | not tagged upstream | Apache-2.0 | Tesseract OCR project | the English model, a data file we would ship, pinned by the SHA-256 above |

The `System.Reflection.Emit` dependency the package lists is not restored on .NET 10.

## Risks, and what the trial showed

- **Native code reads screen pixels in our process.** Tesseract and Leptonica are C and C++. The input is a pixel buffer we build ourselves, never a file from outside, and the trial shows that works. A memory error in the engine would still be a memory error in the app.
- **Pixel copies we cannot zero.** Like Windows' recognizer, the native engine keeps its own copies and frees them without zeroing. The threat model already says so for Windows.
- **A network client inside the DLL** (above). Not exercised, but present.
- **Supply chain and one maintainer.** The package and the model are pinned; the upstream model repository publishes no tags or checksums, so the build would check our recorded SHA-256. The engine sits behind one interface and can be removed.
- **No ARM64 build in the package.** ClipBench builds for both x64 and ARM64. An ARM64 install would keep working with the Windows engines only.
- **The Visual C++ runtime is not in ClipBench's self-contained output.** The two native files import `vcruntime140.dll`, `vcruntime140_1.dll` and `msvcp140.dll`. They are present on the owner's machine, but a clean machine may lack them, so they would have to be shipped with the app.
- **Behind upstream.** Tesseract 5.5.0 inside the package against 5.5.4 upstream.
- **Large selections.** Peak memory rises by about 150 MB for a 1900 x 600 selection.

## Decisions for the owner

1. **The network client in the DLL.** Options: (a) accept it, state it plainly in the privacy document and threat model ("a shipped library contains an unused network client; ClipBench never gives it a URL, and a test watches the process for sockets"), and add a check that fails if the process ever opens a connection; (b) build Tesseract from source without libcurl for x64 and ARM64, which removes it and gives an ARM64 build but makes us own a native build (effort not yet estimated); (c) reject Tesseract.
2. **ARM64.** Ship the Tesseract engine on x64 only, with ARM64 falling back to the Windows engines (recommended unless option 1(b) is chosen), or hold the whole change until there is an ARM64 build.
3. **The Visual C++ runtime.** Ship the three DLLs app-local in the installer and the zip (Microsoft's license allows it), or require the redistributable.
4. **Whether to fix today's prose regression first,** independently of this record: trust the English engine's reading when no line has a right-to-left letter and use the Arabic engine only for words in its own script. Not measured as a fix yet. It would raise prose from 0.922 back toward 0.975 and give back some of the code gain (0.835 toward 0.71), which Tesseract makes unnecessary.

**Recommendation:** accept Tesseract, with 1(a) and 2 (x64 only) and 3 (ship the runtime app-local), because it is the only option measured that fixes paths, symbols, code and prose together (0.958, 93%, 96.3%, 0.994) at the same speed. Choose 1(b) instead if the owner wants the "no network code shipped" statement to stay literally true; that is more work and is not researched here.

## Decision

On 2026-11-05 the owner, asked to choose, left the choice to the assistant ("choose the best you think"). The recommendation above was taken as written:

1. **Network client:** accepted and stated plainly in `docs/security/PRIVACY.md` and `docs/security/THREAT_MODEL.md`. ClipBench never gives the engine a URL. A test reads text with the engine while it watches the process's connections.
2. **ARM64:** the Tesseract engine ships for x64 only. Where it cannot start (ARM64, a missing model or runtime) ClipBench reads with the Windows engines alone, as before, and nothing fails.
3. **Visual C++ runtime:** shipped with the app (app-local) in the installer and the zip when Phase 10 builds them. Until then the "For Phase 10" list at the end of this record holds it.
4. **Prose regression of the Arabic engine:** not fixed separately. On x64 Tesseract replaces the Windows English reading, which removes it. On ARM64 it stays and is recorded as a known limit.
5. **Choosing between readings** (`ReadingMerge`, tested in Core): the Tesseract reading is used for everything that is not Arabic or Hebrew, unless a Windows reading has at least two more letters the English model cannot read: letters of another alphabet or Latin letters with an accent (not the o-slash, see below). Arabic and Hebrew words come from the Windows engine for that script, except where Tesseract read the same place with confidence 30 or more (the Arabic engine makes up two Arabic letters for tiny Latin text such as `x > 0`). A symbols-only Tesseract word with confidence under 60 gives way to a symbols-only word a Windows engine read in the same place (the English model reads a chevron icon as `>>`). The model is `eng` only. Tesseract reads its own twofold copy of the picture (`PreferredScale`), not the threefold one the Windows engines read, and its word places are moved to the shared picture; it writes typographic quotes where code has straight ones, so quotes are always put back as straight ones.
6. **The model file** is committed under `third_party/tessdata/` with its SHA-256, and a test fails if the file changes.

This is still the owner's to overrule: reverting is one package reference, one factory method and the model file.

### Measured on the shipped pipeline (2026-11-05)

The same pictures, run through the code in the repository (Tesseract, the Windows English and Arabic engines, and the merge above), against the thresholds:

| Measure | Threshold | Windows only | Shipped pipeline |
|---|---|---|---|
| Code-like text, mean similarity | at least 0.85 | 0.835 | **0.970** |
| `>` kept in 252 cases | at least 90% | 65% | **95%** |
| Code pictures read empty | at most 5 of 120 | 0 | **0** |
| Terminal screenshot, characters right | at least 90% | 43.9% | **96.3%** |
| Prose, mean similarity | at least 0.95 | 0.922 | **0.994** |
| 1000 x 100 px selection, median | at most 1 s | 71 ms | **119 ms** (Tesseract and two Windows engines) |

The owner's File Explorer breadcrumb comes out as `> محمد الصاعدي > Minecraft-mod-porting >`, exactly.

The first run of the merge failed two of these (`>` kept 89.7%, terminal 43.9%) and found why. A "more non-ASCII letters wins" rule counted the `ø` the Arabic engine makes of a slashed zero and gave the terminal back to the engine that misread it; and the Arabic engine's made-up Arabic letters replaced `x > 0`. Both are now fixed by the rules in decision 5, each with a unit test. One of 252 chevron cases still loses text (`if (a > b) {` in one font came back as `if(a`).

### From the owner's second round of real screenshots (2026-11-06)

A chat window (prose with inline code, light on dark) and a dark JSON block with dashed rounded borders were read with threefold enlargement and gave: lost underscores (`MANUAL_QA`, `A_paste_reaches_a_real_window`), curly quotes in place of straight ones, `[80` as `(88`, `],` as `1 F`, a bullet as `e`, `v0.2.0` as `v@.2.@`. Tesseract alone at twofold, on the same pictures, kept every underscore, and on drawn pictures twofold was as good or better than threefold (code 0.960 against 0.958, `>` 95% against 93%, prose 0.994 for both; the terminal and the breadcrumb were unchanged). So Tesseract now reads its own twofold copy, and curly quotes are straightened (tests for both). After that change the underscores and quotes are right on those two pictures. What remains is the font and the box: a list bullet read as `e`, the slashed zero of a monospace font read as `@`, the dashed borders of the JSON boxes read as stray `'`, `}` and `;` at line ends, and a few bracket confusions at 11 pixels (`[` as `(`). These are limits of the recognizer on that artwork, not of the pipeline.

### Facts found while building it

- The two native libraries bundle image decoders (libjpeg-turbo, zlib 1.2.11, libpng, libtiff, giflib, an LZMA library) and a network client. ClipBench never calls them: it gives Tesseract raw pixels only. They are listed in `THIRD_PARTY_NOTICES.md`; zlib 1.2.11 is old and only matters if a compressed stream ever reaches it, which no code path allows.
- The package copies `x86` files and `tesseract.exe` into the output; the Windows project removes both, and a test checks the output.
- On ARM64 the x64 files would still be copied by a build for that platform and cannot load. The engine then returns "not started" and Windows reads alone. Phase 10 must leave them out of the ARM64 installer.

### For Phase 10 (installer and zip)

Ship the three Visual C++ runtime DLLs app-local for x64; leave out `x86`, `tesseract.exe` and (on ARM64) the x64 Tesseract files; ship the Apache-2.0 text beside `THIRD_PARTY_NOTICES.md`.

## If accepted

- A new package group in `Directory.Packages.props` with the exact versions, lock files, the model in the repository or fetched by a build step that checks the SHA-256, a third-party notices file (the repository has none yet) with the Apache-2.0 and MIT texts, and architecture tests that allow exactly this reference from the Windows platform assembly and no other. The installer and the zip exclude `x86`, `arm64` (absent) and `tesseract.exe`.
- A `TesseractEngine` implementing `IWordRecognizer`, created once and kept (66 ms start-up), used for words without right-to-left letters; the merge takes Arabic and Hebrew words from the Windows engines. Tests render the trial's pictures and fail if the engine returns nothing, skipped when the model is absent. A test fails if the process opens a network connection while reading.
- `docs/security/THREAT_MODEL.md` section 5 and `docs/security/PRIVACY.md` gain the native-code, in-memory-copy and network-client statements. ADR 0018 stays as the record of the Windows engines.
- The installer and zip grow by about 11 MB uncompressed for x64, plus the runtime DLLs.

## If rejected

The limits stay documented in ADR 0018 decision 14 and in the manual QA list; copying from a terminal remains better done with the terminal's own selection. Decision 4 above would be the next thing to do.
