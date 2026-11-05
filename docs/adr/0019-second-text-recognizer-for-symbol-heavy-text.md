---
status: proposed
---

# A second text recognizer for symbol-heavy text (Tesseract), pending a measured trial

**Not approved.** No package is added by this record. It asks the owner for two things: permission to run a trial that downloads two files (below), and, if the trial passes its thresholds, approval to add one package. Facts about packages and versions were read on 2026-11-05 from nuget.org, GitHub and Microsoft Learn; where a fact could not be confirmed it is listed under "Not known yet".

## Why

Phase 8 reads text from the screen with the recognizer built into Windows (`Windows.Media.Ocr`, ADR 0018). Measured on the owner's own screenshots and on drawn test pictures, it fails in one family of cases that margins, enlargement and merging of languages could not fix:

- **It drops text that is mostly symbols.** In 252 trials `>` survived 65% of the time. `x > 0` came back empty, `C:\Users\me> dir` came back as `dir`, and `=> value` as `value`. A thin chevron is read as `(` by the Arabic engine.
- **On a VS Code terminal screenshot (a long path in Cascadia Mono, light on dark), both English engines returned nothing for the path.** Only the Arabic engine read it, and it returned the pieces of the path in right-to-left order inside a single word, read the slashed zero as `ø`, and the bullet icon as `O`. This is its raw output, before any ClipBench code.
- Code-like text cut tightly reads at about 0.72 similarity at best (prose: 0.97), with 16 of 120 lines read empty.

What was tried and did not help: margins, 2x to 5x enlargement, bicubic filters, inversion, grayscale, thresholding, `en-GB`, and taking each word from the engine that reads it best (which fixed the Arabic and chevron case, ADR 0018 decision 16). The remaining errors come from the engine, so they need a second engine.

## What is proposed

Add a **second recognizer behind the existing seam** (`IWordRecognizer`, ADR 0018 decision 16): a Tesseract engine for the Latin script, used next to the Windows engines. Windows would keep reading Arabic and Hebrew, where it is good and Tesseract would need extra models; Tesseract would read the left-to-right text, symbols and paths, with its own word confidence used to drop doubtful words. The merge code already combines readings by word position.

Nothing else about the design changes: only text is kept, never pixels, no network, no elevation, nothing written to disk by the recognizer.

## The candidates

| Option | Maintainer and license | Verdict |
|---|---|---|
| **`TesseractOCR` 5.5.2** (NuGet, last updated 2026-03-05) | Kees van Spelde (`Sicos1977`), Apache-2.0; credits the original wrapper's author Charles Weld. About 752,000 downloads. | **Proposed, after the trial.** Newest of the wrappers, wraps Tesseract 5.5 and Leptonica 1.85, ships native Windows binaries for x86, x64 and **arm64** (the project builds for both x64 and ARM64). Single maintainer. |
| `Tesseract` 5.2.0 (NuGet, `charlesw`) | Charles Weld, Apache-2.0. About 11 million downloads. | **Rejected.** Last updated 2022-11-30, wraps Tesseract 5.2.0 while the engine is at 5.5.x, and the README names only x86 and x64 runtimes, so there would be no ARM64 build. |
| **Windows AI text recognition** (`Microsoft.Windows.AI.Imaging.TextRecognizer`) | Microsoft, part of the Windows App SDK the project already references, so no new package. | **Not usable as the answer, but worth a later optional path.** Microsoft's documentation says it runs only on devices with a neural processing unit and is described as more accurate than `Windows.Media.Ocr`. The owner's machine (Intel Core i7-13700KF desktop, no NPU found) cannot run it, and most Windows 11 PCs cannot. Its model is installed by Windows on first use, which needs a decision under ADR 0002 before it is used. |
| Bundle `tesseract.exe` and start it as a process | Tesseract: Apache-2.0. | **Rejected.** A third-party unsigned program started with screen content on its input, the picture would have to go through a pipe as an encoded image (so a file-format parser reads it) or through a temp file, which the security rules forbid, and the process cost on every capture. |
| ONNX Runtime with an open model (PaddleOCR family) | Microsoft (runtime, MIT) and the model authors (Apache-2.0). | **Not evaluated.** We would write the detection, recognition and decoding steps ourselves and own their bugs. Named here so the choice is not hidden, not researched. |
| Keep Windows only | n/a | Zero cost. The limits above stay, and the manual QA list says so. |

## What would be added (if approved after the trial)

| Item | Version | License | Publisher | Role |
|---|---|---|---|---|
| TesseractOCR | 5.5.2 | Apache-2.0 | Kees van Spelde | wrapper and native `Tesseract` and `Leptonica` DLLs |
| Microsoft.Extensions.Logging.Abstractions | 10.0.3 or newer | MIT | Microsoft | dependency of the wrapper; ADR 0012 decided against logging packages, so this arrives only because of the wrapper and no ClipBench code uses it |
| System.Reflection.Emit | 4.7.0 or newer | MIT | Microsoft | dependency of the wrapper |
| `eng.traineddata` from `tessdata_fast` | not tagged upstream | Apache-2.0 | Tesseract OCR project | the English model, a data file we would ship |

The lock files are the authority for what restore really pulls in. The Visual C++ runtime that the wrapper's native files need (built with Visual Studio 2022) has to be shipped app-local or checked present, which the trial must settle for a self-contained app.

## Risks, and how each is handled

- **Native code reads screen pixels in our process.** Tesseract and Leptonica are C and C++, so a malformed input could in principle corrupt memory. The input is a pixel buffer we build ourselves, never a file from outside; the trial must show that an image can be handed over as raw pixels without a file-format decoder running. If it cannot, this option is rejected.
- **Pixel copies we cannot zero.** Like Windows' recognizer, the native engine keeps its own copies while it works and frees them without zeroing. The threat model already says so for Windows; it would say so again.
- **No network.** The architecture test that forbids `System.Net.Http` and `System.Net.Sockets` in `src/` assemblies must still pass with the new package, and the trial checks the native DLLs' imports for networking libraries.
- **Supply chain and one maintainer.** The package and the model are pinned by version and lock file; the model's SHA-256 would be recorded in the repository and checked by the build, because the upstream repository publishes no tags or checksums. If the package goes stale, the engine sits behind one interface and can be removed or replaced.
- **Size.** The native DLLs and the English model add to the installer and portable zip, for each architecture. The trial measures it.
- **It is not free of errors.** Tesseract works best on text around 30 pixels tall, and screen text is smaller; the enlargement already in place helps but is not proof. Slashed zeros, `l` against `1` and `I` against `l` are font problems that no engine solves for every font.

## Trial before a decision (needs the owner's yes to two downloads)

Run in a scratch folder outside the repository, with nothing added to the project:

1. `TesseractOCR` 5.5.2 from nuget.org, with its dependencies.
2. `eng.traineddata` from the `tessdata_fast` repository (a few megabytes; the exact size is measured and reported).

The trial reads the same pictures the Windows engines were measured on and nothing new, so the numbers compare: the code-like tight crops (3 fonts, 4 sizes, light and dark), the 252 `>` cases, the prose lines, the owner's terminal screenshot and the Explorer breadcrumb. Proposed thresholds, which the owner may change:

| Measure | Windows only today | Tesseract must reach |
|---|---|---|
| Code-like text, mean similarity | 0.72 | at least 0.85 |
| `>` kept in the 252 cases | 65% | at least 90% |
| Code lines read empty | 16 of 120 | at most 5 of 120 |
| The terminal screenshot, characters right | unreadable | at least 90% (a slashed zero read as `0` or `O` counts as right) |
| Prose, mean similarity | 0.97 | not below 0.95 |
| Time for a 1000 x 100 pixel selection on the owner's machine | about 0.1 s per language | at most 1 s |

The trial also answers the "Not known yet" list below. If a threshold fails, this record is closed as rejected and nothing is added.

## Not known yet

- Whether the wrapper can take a raw pixel buffer, or only decodes files and encoded images. Its README examples all load from a file; the trial reads the API.
- The wrapper's real update history (the NuGet page gives only the last date), and which Tesseract version it contains (the README says 5.5.0 or 5.5.2 in different places).
- The size of the native files and of `eng.traineddata`.
- Whether the Visual C++ runtime is already present in the self-contained layout.
- Whether ARM64 works: this project has no ARM64 machine to test on. The package claims it; ClipBench would ship it untested on that architecture, as it ships the ARM64 build today.
- The date and contents of Tesseract 5.5.4, the current upstream release. The wrapper lags upstream by at least one release.

## If accepted

- The package, its dependencies and the model are listed with their licenses in a new third-party notices file (the repository has none yet, and shipping Apache-2.0 code asks for the license text and notices to travel with the installer), the central package file gets one labelled group, and the architecture tests allow exactly this reference from the Windows platform assembly and no other.
- A test renders the trial's pictures and fails if the engine returns nothing, as the Windows engine tests do today, skipped when the model is absent.
- `docs/security/THREAT_MODEL.md` section 5 and `docs/security/PRIVACY.md` gain the native-code and in-memory-copy statements above. ADR 0018 stays as the record of the Windows engines and points here.
- The installer and the portable zip grow by the measured size.

## If rejected

The limits stay documented in ADR 0018 decision 14 and in the manual QA list, and copying from a terminal remains better done with the terminal's own selection.
