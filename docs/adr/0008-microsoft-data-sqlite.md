---
status: accepted
---

# Microsoft.Data.Sqlite for storage

Phase 2 stores the history in SQLite through `Microsoft.Data.Sqlite` 10.0.12, the ADO.NET provider the .NET team builds and the one Entity Framework Core uses. The owner approved this package in chat on 2026-10-13 on the condition that its license be MIT or Apache. Both were checked against the published package metadata on nuget.org the same day, and the check passed. It is the only package Phase 2 adds to the storage library.

## What is added

All versions were read from the nuspec files on nuget.org on 2026-10-13. No advisory is listed for any of them. The restore audit (`NuGetAudit`) and the CI vulnerability check watch them from now on.

| Package | Version | License | Publisher | Role |
|---|---|---|---|---|
| Microsoft.Data.Sqlite | 10.0.12 | MIT | Microsoft | the provider; the project references this one |
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | Microsoft | the provider code |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 | Eric Sink | picks the native library for the platform |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 | Eric Sink | the native-call layer |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 | Eric Sink | binds that layer to the library |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 | Eric Sink | `e_sqlite3.dll` for win-x64 and win-arm64 |

The native library is SQLite 3.53.3 according to the SQLitePCL.raw release notes for 2.1.12. SQLite itself is in the public domain. The restored lock files are the authority for what is actually pulled in, and the architecture tests fail if the storage library references any other package.

## Considered options

- **SQLCipher or another whole-database encryption build.** Rejected in ADR 0003. It needs a different native build to maintain and audit.
- **`System.Data.SQLite`.** Rejected. It is not published by Microsoft or the .NET Foundation, which the dependency rule prefers.
- **Windows' own `winsqlite3.dll` through `SQLitePCLRaw.bundle_winsqlite3`.** Rejected. It ships no native file, but the SQLite version would depend on the Windows build. Behavior ClipBench relies on (secure delete, WAL, window functions) could not be pinned or tested in CI, and security fixes would arrive on Microsoft's schedule, not ours.
- **A document store such as LiteDB, or encrypted flat files.** Rejected. SQL gives transactions, crash safety and the retention queries for free, and a hand-made file format would be code to audit instead.

## Consequences

- **The native library has one maintainer.** `SQLitePCLRaw` is maintained by Eric Sink, not Microsoft, and the provider depends on it. A compromise or abandonment there would reach ClipBench. Dependabot tracks it, the Phase 9 dependency audit re-checks the maintainers, and the `winsqlite3` bundle above is the fallback because it needs no native file from this author.
- **A native library ships with the app** for x64 and ARM64. The Phase 10 notices file lists it with its Apache-2.0 license, and the Phase 9 license audit expects it.
- **The provider's 3.0 line exists but is not used.** Version 10.0.12 depends on `SQLitePCLRaw` 2.1.12. Move only when Microsoft's provider does.
- **SQL is parameterized only.** Statements are constants, and a test and an analyzer rule enforce it.
