# Contributing to ClipBench

Thank you for helping. ClipBench is a security-first clipboard manager, so a few
rules are stricter than in most projects. They exist to protect people's data.

## The three rules that never bend

1. **Tests first.** Every feature and every bug fix starts with a failing test.
2. **No network.** The app makes no network calls. An architecture test fails the
   build if any source assembly uses networking types.
3. **Nothing sensitive on disk in plain text.** Clip content, previews and
   source-app names are encrypted at rest, and logs never contain clip content.

## Setting up

You need Windows 11 (Windows 10 22H2 works on a best-effort basis), Git, and the
.NET SDK named in [global.json](global.json). Visual Studio is optional:
`dotnet build` builds the WinUI app.

```bash
git clone https://github.com/TheAlsaadi/ClipBench.git
cd ClipBench
git config core.hooksPath .githooks
```

The second command turns on the `commit-msg` hook. It is required.

The test runner and the .NET CLI send usage data to Microsoft unless you opt out.
Set both of these environment variables to `1` in your shell or user profile:

- `DOTNET_CLI_TELEMETRY_OPTOUT`
- `TESTINGPLATFORM_TELEMETRY_OPTOUT`

## Commands

```bash
dotnet restore --locked-mode
dotnet build -c Release -warnaserror
dotnet test --solution ClipBench.slnx -c Release --filter-not-trait "Category=Integration"
dotnet format --verify-no-changes
dotnet list package --vulnerable --include-transitive
```

`dotnet format` loads the Debug configuration by default. Set the environment
variable `Configuration` to `Release` when you run it after a Release build.
CI does this.

Tests that touch the real clipboard carry `[Trait("Category","Integration")]`.
Run them on their own with:

```bash
dotnet test --project tests/ClipBench.Platform.Windows.IntegrationTests -c Release --filter-trait "Category=Integration"
```

Coverage runs once per test project, measuring only that project's assembly:

```bash
dotnet test --project tests/ClipBench.Core.Tests -c Release --coverlet --coverlet-output-format cobertura --coverlet-include "[ClipBench.Core]*" --coverlet-threshold 90 --coverlet-threshold-type line
```

## How to work

1. Branch from `main`. Use `phase-NN-short-name` for planned phases of
   [PLAN.md](PLAN.md), and `feat/...` or `fix/...` for anything else.
2. **Red.** Write a test that describes the behavior. Run it and check that it
   fails for the reason you expect, not because of a typo.
3. **Green.** Write the least code that passes. Run the whole suite.
4. **Refactor** with everything green.
5. Commit small and often. Each commit leaves the build green.

Tests must be deterministic: inject `TimeProvider`, randomness and file paths, and
do not use `Thread.Sleep` in unit tests. Never delete, skip or weaken a test to
get a build through. If a test is wrong, say why and fix it in its own commit.

Coverage floors, enforced in CI: `ClipBench.Core` 90%, `ClipBench.Storage` 85%,
and `ClipBench.Presentation` 80% from Phase 4.

## Commits

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`,
`test:`, `refactor:`, `docs:`, `build:`, `ci:`, `chore:`.

The repository owner is the sole author of the history. Do not add commit
trailers or footers that credit a tool or service as an author or co-author. The
`commit-msg` hook removes such lines, and the `attribution-guard` check in CI
rejects any that remain, in commits and in pull request titles and bodies.
Crediting a human co-author is fine.

## Dependencies

Prefer the .NET base class library and Microsoft or .NET Foundation packages. A
new NuGet package needs a short decision record in `docs/adr/` (why, maintainer,
license, alternatives) and the owner's approval before it is added. Versions are
managed centrally in `Directory.Packages.props`, with lock files. `nuget.org` is
the only package source.

## Pull requests

- Keep each pull request focused, and fill in the template.
- `attribution-guard` and `build-test` must pass before merging.
- Describe what you tested first and how you saw it fail.
- Update `CHANGELOG.md` (Unreleased) and any affected docs.

## Reporting security problems

Do not open a public issue. Follow [SECURITY.md](SECURITY.md).

## Conduct and license

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md). By
contributing you agree that your work is released under the [MIT license](LICENSE).
