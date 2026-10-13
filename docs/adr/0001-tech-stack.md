---
status: accepted
---

# Tech stack: C# on .NET 10, WinUI 3, SQLite

ClipBench is a C# 14 / .NET 10 (LTS) desktop app with a WinUI 3 interface on Windows App SDK 2.5.1 (unpackaged and self-contained), CommunityToolkit.Mvvm for view models, Microsoft.Windows.CsWin32 for Win32 interop and SQLite through Microsoft.Data.Sqlite for storage. It targets Windows 11 on x64 and ARM64. The choice is partly about security: native code with no bundled web engine, a small attack surface, and UI Automation for accessibility.

## Considered options

- **Electron or Tauri.** Rejected. They bring a web engine and its own networking and update paths into the one program that sees every secret on the clipboard.
- **WPF or WinForms.** Rejected. Neither gives the Fluent look (Mica, rounded corners) without extra work, and WinUI 3 is Microsoft's recommended UI platform for new desktop apps.
- **C++ and raw Win32.** Rejected. Memory-safety bugs are the wrong risk to take in this component.

## Packages approved by this record

Versions and licenses were checked against nuget.org on 2026-10-13. A package that is not yet in use is re-checked (version, license, advisories) when it is first added.

| Package | Role | License |
|---|---|---|
| xunit.v3 4.0.2 | tests | Apache-2.0 |
| coverlet.MTP 10.1.0 | coverage | MIT |
| NSubstitute 6.2.0 | test doubles | BSD-3-Clause |
| Shouldly 4.3.0 | assertions | BSD-3-Clause |
| TngTech.ArchUnitNET 0.13.4 | architecture tests | Apache-2.0 |
| Microsoft.WindowsAppSDK 2.5.1 | UI platform | Microsoft Software License Terms |
| Microsoft.Windows.CsWin32 0.3.360 (Phase 3) | Win32 interop generator | MIT, with build-time metadata under the Windows SDK license (ADR 0010) |
| CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Microsoft.Extensions.*, FlaUI (Phases 2 to 4) | view models, storage, hosting, UI tests | expected MIT |

CsWin32's generator inputs (`Microsoft.Windows.SDK.Win32Metadata` and related) carry preview, experimental and alpha labels, and they are under the Windows SDK license, not MIT. They are used at build time only. ADR 0010 records the approval and this second license exception.

## Consequences

- **Windows App SDK is the one dependency that is not under an OSI license.** Its terms allow redistribution inside an app under conditions, and they say the software may collect usage data and check for updates. ClipBench's own code makes no network calls, but that statement cannot cover Microsoft's runtime. Phase 9 adds a test that the running process opens no network connections, and the Phase 10 installer shows the Microsoft terms and ships a notices file. Until the test exists the README describes "no network" as a commitment about ClipBench's code.
- **No single-file publish.** Microsoft documents that `dotnet publish` cannot produce a single-file executable for WinUI 3 because the native runtime files stay separate. Phase 10 ships the publish folder inside the installer and in the portable zip.
- **The Phase 9 license audit** expects MIT, Apache or BSD-compatible dependencies and lists two known exceptions: the Windows App SDK, and CsWin32's build-time metadata packages (ADR 0010), which must not appear in any release output.
