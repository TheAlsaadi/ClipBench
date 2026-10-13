---
status: accepted
---

# Project name, bootstrap and plan deviations

The project is named **ClipBench**. It started as ClipDeck, but two new Windows-capable clipboard managers called `clipdeck` already existed on GitHub in 2026 (`Talkdedsec/clipdeck` and `rajivhgpandya/clipdeck`), so the name was changed before any code was written. On 2026-10-13 ClipBench had no clipboard-manager collision on GitHub or NuGet; the closest was a dormant 2021 Python repository with no stars. A trademark search was not done and is left to the owner.

## Deviations from PLAN.md

- **No `git init` and no separate initial commit.** The owner created the GitHub repository with its first commit and set `core.hooksPath` before the first session. The bootstrap files arrive through the `phase-00-bootstrap` pull request, so `main` is never committed to directly. Rule 4 and PLAN Phase 0 step 3 were amended to say so.
- **`GLOSSARY.md` lives at the repository root,** the location the glossary tooling expects, instead of `docs/GLOSSARY.md`.
- **The attribution guard's logic is a tested script** (`scripts/attribution-guard.sh`) that the workflow calls, instead of inline workflow shell. It also checks commit author and committer identities, not only messages and pull request text.
- **The test stack differs from the first sketch** in two places (ADR 0006).
- **Coverage floors are 0 in Phase 0,** because the assemblies are empty. Phase 1 raises them.
