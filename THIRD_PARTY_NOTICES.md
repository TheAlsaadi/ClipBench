# Third-party notices

ClipBench is MIT-licensed (see `LICENSE`). The release also contains the software below, under its own licenses. The packages ClipBench needs only to be built or tested are not listed; they are not shipped.

## Application framework and storage

| Component | Version | License | Where it is |
|---|---|---|---|
| Windows App SDK (WinUI 3), by Microsoft | 2.5.1 | Microsoft Software License Terms for the Windows App SDK | beside the program |
| .NET runtime, by Microsoft and the .NET Foundation | 10 | MIT | beside the program (self-contained) |
| Microsoft.Data.Sqlite, by Microsoft and the .NET Foundation | 10.0.12 | MIT | beside the program |
| SQLitePCLRaw (`SQLitePCLRaw.core`, `bundle_e_sqlite3`, `provider.e_sqlite3`), by Eric Sink | 2.1.12 | Apache-2.0 | beside the program |
| SQLite (`e_sqlite3.dll`), by the SQLite developers | bundled with SQLitePCLRaw 2.1.12 | Public domain | beside the program |
| CommunityToolkit.Mvvm, by Microsoft and the .NET Foundation | 8.4.2 | MIT | beside the program |
| Microsoft.Extensions.DependencyInjection, by Microsoft | 10.0.13 | MIT | beside the program |

The Visual C++ runtime that the text-recognition libraries need is to ship beside the program from Phase 10, under Microsoft's redistribution terms, and gets its row then. The full MIT and Apache-2.0 texts ship with the installer and the portable zip (Phase 10 checklist).

## Text recognition (ADR 0019)

| Component | Version | License | Where it is |
|---|---|---|---|
| Tesseract OCR engine (`tesseract55.dll`), by the Tesseract OCR project and Google | 5.5.0 | Apache-2.0 | `x64/` beside the program |
| Leptonica (`leptonica-1.85.0.dll`), by Dan Bloomberg and contributors | 1.85.0 | Leptonica license (BSD-2-Clause style), below | `x64/` beside the program |
| TesseractOCR (.NET wrapper), by Kees van Spelde, based on the work of Charles Weld | 5.5.2 | Apache-2.0 | `TesseractOCR.dll` |
| `eng.traineddata` from `tessdata_fast`, by the Tesseract OCR project | not tagged upstream; SHA-256 `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2` | Apache-2.0 | `tessdata/` beside the program |
| Microsoft.Extensions.Logging.Abstractions and Microsoft.Extensions.DependencyInjection.Abstractions, by Microsoft | 10.0.3 | MIT | beside the program |

The full text of the Apache License 2.0 is at <https://www.apache.org/licenses/LICENSE-2.0>. The installer and the portable zip must carry that text next to this file (Phase 10 checklist).

The two native libraries are built with other open-source libraries inside them. From the strings in the files these include libjpeg-turbo, zlib 1.2.11, libpng, libtiff, giflib and an LZMA library, each under a permissive license (BSD, zlib, IJG or similar), and Tesseract's network library for fetching a picture from a web address. ClipBench never opens a picture file or a web address with them: it hands Tesseract raw pixels held in memory (`docs/security/THREAT_MODEL.md`, section 5).

### Leptonica license

```
Copyright (C) 2001-2020 Leptonica.  All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:
1. Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above
   copyright notice, this list of conditions and the following
   disclaimer in the documentation and/or other materials
   provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL ANY
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY
OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
