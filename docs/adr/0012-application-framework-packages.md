---
status: accepted
---

# CommunityToolkit.Mvvm, dependency injection and logging; no generic host

Phase 4 builds the app shell. It needs a way to write view models without boilerplate, a place to wire the services together, and logging. The owner approved these three packages in chat on 2026-10-14, and decided against the generic host that PLAN.md names for the composition root. The versions and licenses were read from the nuspec files on nuget.org the same day, and no advisory is listed for any of them.

## What is added

| Package | Version | License | Publisher | Used by |
|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Microsoft (.NET Foundation) | `ClipBench.Presentation`, for observable properties and commands |
| Microsoft.Extensions.DependencyInjection | 10.0.13 | MIT | Microsoft | `ClipBench.App`, the composition root |
| Microsoft.Extensions.Logging | 10.0.13 | MIT | Microsoft | `ClipBench.App`, the log pipeline |

The restored lock files are the authority for the rest. Beyond the three, restore pulls in only `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.Primitives`, all 10.0.13, MIT, Microsoft. CommunityToolkit.Mvvm has no dependencies on .NET 10. The architecture tests allow exactly these references: Presentation may use the toolkit, App may use the other two, and Core, Storage and the Windows layer use none.

## Considered options

- **Microsoft.Extensions.Hosting** (what PLAN.md names). Rejected by the owner. It pulls in about twenty more packages: configuration from JSON files and environment variables, user secrets, file providers, and log writers for the console, the debugger and the Windows Event Log. A tray app needs none of them, each is surface to audit, and the default builder would write to places that this app's logging rules keep it out of. The start-up and shut-down order is a few lines in our own composition root, which a test can read.
- **No container: build the object graph by hand.** Rejected. It works at this size, but every later phase adds services (queue, collect, settings, OCR), and a hand-built graph turns into one long method that is hard to review.
- **A third-party container (Autofac, DryIoc, and so on).** Rejected. The built-in container does everything needed here, and the dependency rule prefers Microsoft or .NET Foundation packages.
- **Hand-written `INotifyPropertyChanged` and commands, or a framework such as Prism or ReactiveUI.** Rejected. Hand-written change notification is the boilerplate that hides mistakes. Prism and ReactiveUI are bigger than the problem and not Microsoft packages.
- **Logging with only our own sink and no `Microsoft.Extensions.Logging`.** Rejected. The plan asks for it, the abstraction is what later phases and any reader of the code expect, and the file sink is one small provider on top of it.

## Consequences

- **View models stay free of UI types.** CommunityToolkit.Mvvm's source generator writes the property and command code at build time, so `ClipBench.Presentation` still references no `Microsoft.UI.*` assembly and its tests need no UI thread.
- **The container is built in one place.** Only the composition root resolves services. View models and Core types receive what they need through their constructors, so tests build them directly.
- **Logging stays content-free by construction, not by convention.** Core defines its own small log interface with typed, content-free events (ADR 0014), and the App adapts it to `ILogger`. The packages above carry the events to a size-capped file; they never see clip content. A test still checks the written file for a marker string.
- **The packages follow the .NET servicing line.** Dependabot tracks them, and the Phase 9 dependency audit re-checks them.
