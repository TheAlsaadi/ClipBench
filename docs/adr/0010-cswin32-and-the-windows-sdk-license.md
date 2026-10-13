---
status: accepted
---

# CsWin32 for Win32 calls, and a second license exception for its metadata

Phase 3 talks to the clipboard, the hotkey table, the input queue and the data protection API. The project rules (ADR 0001) say those calls come from the Microsoft.Windows.CsWin32 source generator and not from hand-written `DllImport` declarations. The owner approved CsWin32 in chat on 2026-10-13 and accepted that its build-time metadata packages are not under an OSI license. This record is the part the rules ask for: why, who maintains it, what the licenses are and what else was considered.

## What is added

Versions and licenses were read from the nuspec files on nuget.org on 2026-10-13. The Platform library references only the first row. The other three arrive because CsWin32 depends on them (`include="buildTransitive"`), and they are used while the project is built.

| Package | Version | License | Publisher | Role |
|---|---|---|---|---|
| Microsoft.Windows.CsWin32 | 0.3.360 | MIT | Microsoft | source generator; a development dependency |
| Microsoft.Windows.SDK.Win32Metadata | 71.0.30-preview | Windows SDK license (`sdk_license.txt`) | Microsoft | the description of the Win32 API the generator reads |
| Microsoft.Windows.WDK.Win32Metadata | 0.13.25-experimental | Windows SDK license (`sdk_license.txt`) | Microsoft | the same for driver-kit APIs; pulled in by CsWin32 |
| Microsoft.Windows.SDK.Win32Docs | 0.1.42-alpha | Windows SDK license (link only) | Microsoft | API documentation copied into the generated code as comments |

## The license exception

CsWin32 itself is MIT. The three metadata packages are Microsoft's, are not under an OSI license, and are labeled preview, experimental and alpha. ADR 0001 listed CsWin32 as MIT and noted the labels, but not the license. This record corrects that, and the Phase 9 license audit gets a second known exception next to the Windows App SDK.

The text of `sdk_license.txt` was read from the restored package. It lets the software be used "to design, develop and test your programs that run on a Microsoft operating system" and on build machines "solely for the purpose of compiling, building, verifying and archiving your programs", which covers the owner's machine and the GitHub Actions runner. It forbids publishing the software for others to copy. ClipBench therefore never commits or releases the metadata: it is restored from nuget.org at build time and is not part of the repository, the installer or the zip. Its terms about "Distributable Code" apply to files that ClipBench does not distribute.

What keeps the exception small:

- **They are build-time only.** The reference to CsWin32 is `PrivateAssets="all"`, so nothing flows to the projects that use the Platform library, and no metadata file is copied to the output. An architecture test checks the attribute, and the Phase 9 audit checks the published output for any of the four packages.
- **No third-party code ships from them.** The generator writes C# into the Platform assembly. That code is ours, built from a list of API names (`NativeMethods.txt`) that names only what ClipBench calls. The files in the output are the ones the project compiled.
- **The lock files pin them.** A new version of any of the four changes a lock file and needs a review, like any package.
- **Their labels do not reach users.** A preview metadata package can be wrong about a signature. The integration tests call each API against the real operating system, so a wrong signature fails there and not in the field.

## Considered options

- **Hand-written `DllImport` or `LibraryImport`.** Rejected by the project rules, and for a security reason: a wrong signature is a memory-safety bug in the one component that reads other programs' data. The generator takes signatures from the Windows metadata.
- **`System.Security.Cryptography.ProtectedData`.** Rejected in ADR 0009. It is not part of the .NET 10 Windows framework, so it is also a new package, and it covers only DPAPI.
- **Vanara or another community Win32 library.** Rejected. It is not published by Microsoft, it ships a very large runtime assembly instead of a few generated methods, and it would add its own maintainer to the trust list.
- **The WinRT projections that the Windows target framework includes.** Used where they fit (Phase 8 OCR). They cover no clipboard-format enumeration, no `RegisterHotKey`, no `SendInput` and no `CryptProtectData` with entropy.

## Consequences

- **The generated code is internal.** Nothing from CsWin32 is part of the Platform library's public surface. Callers use the Core interfaces.
- **Unsafe code is allowed in the Platform project only.** `AllowUnsafeBlocks` is needed for the generated pointers. Core, Storage and Presentation stay free of it.
- **`NativeMethods.txt` is reviewed like code.** An architecture test (Phase 3) fails the build if it names a hook, a shell-execute or a process-creation API.
- **A wrong or removed signature is caught at build or in an integration test, not at run time.** The generator stops the build if a name in the list does not exist.
