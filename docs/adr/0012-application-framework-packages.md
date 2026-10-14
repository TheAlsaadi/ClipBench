---
status: accepted
---

# CommunityToolkit.Mvvm and dependency injection; no generic host, no logging package

Phase 4 builds the app shell. It needs a way to write view models without boilerplate, a place to wire the services together, and logging. The owner approved three packages in chat on 2026-10-14 (CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection and Microsoft.Extensions.Logging) and decided against the generic host that PLAN.md names for the composition root. While building the log file, Microsoft.Extensions.Logging turned out to be unnecessary, so it was not added after all: only the other two are used. The versions and licenses were read from the nuspec files on nuget.org the same day, and no advisory is listed for any of them.

## What is added

| Package | Version | License | Publisher | Used by |
|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Microsoft (.NET Foundation) | `ClipBench.Presentation`, for observable properties and commands |
| Microsoft.Extensions.DependencyInjection | 10.0.13 | MIT | Microsoft | `ClipBench.App`, the composition root |

The restored lock files are the authority for the rest. Beyond the two, restore pulls in only `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.13, MIT, Microsoft). CommunityToolkit.Mvvm has no dependencies on .NET 10. The architecture tests allow exactly these references: Presentation may use the toolkit, App may use dependency injection, and Core, Storage and the Windows layer use no package of this ADR.

## Considered options

- **Microsoft.Extensions.Hosting** (what PLAN.md names). Rejected by the owner. It pulls in about twenty more packages: configuration from JSON files and environment variables, user secrets, file providers, and log writers for the console, the debugger and the Windows Event Log. A tray app needs none of them, each is surface to audit, and the default builder would write to places that this app's logging rules keep it out of. The start-up and shut-down order is a few lines in our own composition root, which a test can read.
- **No container: build the object graph by hand.** Rejected. It works at this size, but every later phase adds services (queue, collect, settings, OCR), and a hand-built graph turns into one long method that is hard to review.
- **A third-party container (Autofac, DryIoc, and so on).** Rejected. The built-in container does everything needed here, and the dependency rule prefers Microsoft or .NET Foundation packages.
- **Hand-written `INotifyPropertyChanged` and commands, or a framework such as Prism or ReactiveUI.** Rejected. Hand-written change notification is the boilerplate that hides mistakes. Prism and ReactiveUI are bigger than the problem and not Microsoft packages.
- **Microsoft.Extensions.Logging** (what PLAN.md names, and what the owner approved first). Not used. Every log call in this app goes through `IActivityLog` (ADR 0014), whose events carry only codes, sizes and types, and the file sink is a small class that writes those lines directly. `ILogger` would add free-text messages, which the design forbids, plus a package and its options and primitives dependencies, for no benefit. It can be added later if a part of the app ever needs it.

## Consequences

- **View models stay free of UI types.** CommunityToolkit.Mvvm's source generator writes the property and command code at build time, so `ClipBench.Presentation` still references no `Microsoft.UI.*` assembly and its tests need no UI thread.
- **The container is built in one place.** Only the composition root resolves services. View models and Core types receive what they need through their constructors, so tests build them directly.
- **Logging stays content-free by construction, not by convention.** Core defines its own small log interface with typed, content-free events (ADR 0014), and the Windows layer writes it to a size-capped file. A test still checks the written files for a marker string.
- **The packages follow the .NET servicing line.** Dependabot tracks them, and the Phase 9 dependency audit re-checks them.
