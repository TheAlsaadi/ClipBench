# Roadmap

The phased build plan is in [PLAN.md](../PLAN.md). This file records ideas that are
**out of scope for v1**, so they are not forgotten and not built by accident.

## Out of scope for v1

- **Cloud sync.** It needs the network and puts encrypted history on someone else's
  server. ClipBench promises no network calls.
- **Plugins.** An extension point is an attack surface on a program that sees every
  clipboard.
- **Update checks.** Same reason as cloud sync. Updates come from the installer or
  the portable download that the user fetches deliberately.
- **Replacing Win+V.** ClipBench adds a second, separate hotkey and leaves the
  built-in clipboard history alone.

## Ideas to revisit after v1.0

Nothing yet. Add ideas here with a line on why they are not in v1.
