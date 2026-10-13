---
status: accepted
---

# Test stack: xUnit v3 on Microsoft.Testing.Platform, coverlet.MTP and ArchUnitNET

Tests use xUnit v3 (`xunit.v3` 4.x) on Microsoft.Testing.Platform (MTP), switched on for `dotnet test` by the `test.runner` setting in `global.json`, which the .NET 10 SDK supports. Coverage uses `coverlet.MTP`. Architecture rules use the core `TngTech.ArchUnitNET` package with Shouldly assertions. The commands were run against the real runner and recorded in the project rules.

## Considered options

- **VSTest with `coverlet.collector`** (`--collect:"XPlat Code Coverage"`). Rejected. xUnit v3 is MTP-native, and its documentation says the standard Coverlet experience is not supported on MTP.
- **`Microsoft.Testing.Extensions.CodeCoverage`.** Rejected, though it is the extension xUnit's guide shows. It ships under a Microsoft license that requires acceptance and is not an OSI license, and the Phase 9 license audit expects MIT, Apache or BSD-compatible dependencies. Revisit it only if `coverlet.MTP` stops working.
- **`NetArchTest.Rules`.** Rejected, as the project rules allow when it is unmaintained. Its last release (1.3.2) is dated 2021-05-23 and its last commit 2024-07-29. ArchUnitNET was updated on 2026-10-12.
- **`TngTech.ArchUnitNET.xUnitV3` (the xUnit adapter).** Rejected. It pulls in the legacy `System.Net.Http` 4.3.4 package, and the core package plus Shouldly gives the same assertions.

## Consequences

- `--collect` and `--filter` do not apply. Use `--coverlet ...` and `--filter-trait` / `--filter-not-trait`. A run in which no test matches the filter exits with code 8.
- **Run coverage once per test project** with `--coverlet-include "[ClipBench.X]*"`, so each floor measures only its own assembly. `--coverlet-threshold` does enforce a floor despite a contradictory note in the coverlet.MTP docs (checked: 90% on an uncovered class failed with exit 14, 0% passed).
- **An assembly with no code reports 100% line coverage,** so a floor passes vacuously, and so does a mistyped include filter. From Phase 1 the coverage step must also assert that `lines-valid` is greater than zero.
- **ArchUnitNET needs care.** `NotDependOnAny(provider)` silently misses framework types that are referenced but not loaded; use `NotDependOnAnyTypesThat()`. The string overload of `ResideInAssembly` matches the full display name, so pass the loaded `Assembly`. Fluent builders share state, so start each rule from its own `Types()` call. Every rule is checked by a mutation run that introduces a real violation.
- **`xunit.v3` depends on `Microsoft.Testing.Extensions.Telemetry`,** which sends usage data to Microsoft when tests run. It lives only in test projects. CI sets `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`, and the contributing guide asks contributors to do the same.
- **`dotnet format` loads the Debug configuration,** so run it with the environment variable `Configuration=Release` after a Release build. CI does.
