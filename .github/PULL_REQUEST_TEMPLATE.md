## What and why

<!-- What does this change, and why? Link the plan phase or issue. -->

## How it was tested

<!-- Which test did you write first, and how did you see it fail? -->

## Checklist

- [ ] A failing test came first (or this change needs none, and I say why above)
- [ ] `dotnet build -c Release -warnaserror` is clean
- [ ] Tests pass, and coverage floors still hold
- [ ] `dotnet format --verify-no-changes` is clean
- [ ] No network calls, no new elevation, no low-level keyboard or mouse hooks
- [ ] Nothing sensitive is written to disk, logs or crash data
- [ ] Any new package has a decision record in `docs/adr/` and the owner's approval
- [ ] `CHANGELOG.md` (Unreleased) and affected docs are updated
- [ ] Commits follow Conventional Commits
