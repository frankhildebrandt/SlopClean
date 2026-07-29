---
name: slopclean-modules
description: Create, renew, or update SlopClean built-in optimization modules following Core contracts, SafetyPolicy, DI registration, TDD, and UI discovery conventions. Use when adding a new SlopClean.Modules.* assembly, changing scan/apply behavior, parameters, operation codes, elevation paths, module illustrations, ModuleRegistrationTests, or module localization.
---

# SlopClean Modules

Workflow for creating new built-in modules and updating existing ones. Keep this skill aligned with the real codebase; see [reference.md](reference.md) for contracts and patterns.

## Hard rules

- Modules reference **only** `SlopClean.Core`. No WinUI, no `Microsoft.Win32`, no App project refs.
- OS access only via Core abstractions (`IFileSystem`, `IRegistryStore`, `IProcessInspector`, `IServiceManager`, `IRecycleBinService`, `IPrivilegeBroker`, plus specialized stores like `IDriverStore` when needed).
- Preview-first: `ScanAsync` is read-only. Never mutate during scan.
- Never weaken `SafetyPolicy`. Unknown `OperationCode`s are rejected.
- Destructive apply paths require TDD (failing test first).
- No per-module UI pages — generic `ModuleDetailPage` + DI registration is enough.
- Runtime plugin DLL discovery is out of MVP scope.

## Decide capability set

| Kind | Interfaces | Apply UI |
|------|------------|----------|
| Analysis / advice | `IScannableModule` + `IModuleIllustration` | `CanReview` false automatically |
| Cleanup (unelevated and/or elevated findings) | `IScannableModule` + `IApplicableModule` + `IModuleIllustration` | Review → plan → clean tasks |
| Elevated-only ops (driver packages, etc.) | Same as cleanup; module `ApplyAsync` **refuses** elevated-only codes; engine + `IPrivilegeBroker` / `SlopClean.Elevated` execute | Fail-closed |

Also implement `IModuleIllustration` (required — CI asserts PNG magic bytes).

Pick an existing `ModuleCategory` when possible. Extend the enum only for a genuinely new category.

## Create a new module

Copy this checklist and track progress:

```
New module:
- [ ] Design (id, category, scan-only vs applicable, op codes, privilege)
- [ ] New op codes? → PrivilegedOperationCodes + SafetyPolicy validation (+ tests)
- [ ] Red: tests/SlopClean.Modules.<Name>.Tests (fakes only)
- [ ] src/SlopClean.Modules.<Name>/ project + Assets/illustration.png
- [ ] <Name>Module.cs (Scan / Apply / illustration)
- [ ] Wire: SlopClean.slnx, aggregator csproj, ServiceCollectionExtensions
- [ ] ModuleRegistrationTests count + Assert.Contains
- [ ] Localization (.resw + ModuleLocalization) preferred
- [ ] AGENTS.md MVP table (+ README if product-facing)
- [ ] About credits if new runtime OSS dependency
- [ ] make test / dotnet test SlopClean.slnx -c Release
```

### 1. Design

- `ModuleId`: kebab-case constant, e.g. `"temp-cleaner"`.
- Parameters: use existing `BoolParameter` / `IntParameter` / `EnumParameter` / `PathListParameter`. New parameter types need Core + App `ParameterItemViewModel`/XAML support.
- Operation codes: reuse `PrivilegedOperationCodes` when possible. New destructive codes need:
  1. Constant + entry in `PrivilegedOperationCodes.All`
  2. Explicit branches in `SafetyPolicy.ValidateAction` (and registry allowlist if registry)
  3. Core tests for the safety rules **before** module use
  4. Elevated helper support if the op must run elevated

### 2. TDD (red)

Create `tests/SlopClean.Modules.<Name>.Tests/` mirroring TempCleaner tests:

- Project refs: module project + `SlopClean.Modules.TestSupport`
- xUnit; prefer fakes (`FakeFileSystem`, etc.) over mocks
- Cover scan findings, apply success/skip/fail, and for destructive modules an `OptimizationEngine` backup/restore round-trip when relevant
- Never touch real user files, autostart, services, or production registry

### 3. Module project

```
src/SlopClean.Modules.<Name>/
  SlopClean.Modules.<Name>.csproj
  <Name>Module.cs
  Assets/illustration.png          # EmbeddedResource; PNG required
  (optional helpers / Data/*.json as EmbeddedResource)
```

csproj pattern: `net10.0`, `ProjectReference` to Core only, embed `Assets\illustration.png`.

### 4. Implement the module class

Conventions (mirror `TempCleanerModule`):

- `public sealed class …Module : …`
- `public const string ModuleId = "…";`
- Constructor injects Core abstractions + `SafetyPolicy` when mutating
- `ScanAsync`: `async IAsyncEnumerable` + `[EnumeratorCancellation]`; report `ScanProgress`; `ThrowIfCancellationRequested`; periodic `await Task.Yield()`
- Pre-filter unsafe targets with `SafetyPolicy` (e.g. `ValidateDeletePath`) so they never appear as selectable
- Actionable findings set `Metadata[OptimizationAction.OperationCodeMetadataKey]`, correct `RequiredPrivilege`, and `AllowedRoot` for path ops
- `ApplyAsync`: re-validate with `ValidateAction`; `Skipped` if gone/blocked; try/catch → `Failed`; success → `Succeeded`
- `OpenIllustration()` → `EmbeddedResourceStreams.OpenModuleIllustration(typeof(…Module))`
- Read-only modules: `IsActionable: false`, no operation-code metadata

Elevated-only operations: refuse in module `ApplyAsync` (see CoreIsolationDrivers). Do not call `IPrivilegeBroker` from the module for apply — `OptimizationEngine` opens the elevated session.

### 5. Wire DI and solution

1. Add projects to `SlopClean.slnx` (src + tests)
2. `ProjectReference` in `src/SlopClean.Modules/SlopClean.Modules.csproj`
3. `services.AddSingleton<IModule, YourModule>();` in `ServiceCollectionExtensions.cs`
4. Update `ModuleRegistrationTests`: bump exact count, add `Assert.Contains` for type + `ModuleId`
5. If new Core abstraction: implement in `Platform.Windows`, register in App platform DI, and add a fake in `ModuleRegistrationTests.RegisterPlatformFakes` (and TestSupport if reusable)

App does **not** reference individual module projects — only the aggregator.

### 6. Localization and docs

Prefer localizing new modules (do not copy the hard-coded English gap on older modules):

- Keys in `Strings/en-US/Resources.resw` and `de-DE/Resources.resw` (`Module<Name>Name` / `Module<Name>Description`)
- Branch in `ModuleLocalization.Resolve` for the module id
- Update AGENTS.md MVP modules table; README features if product-facing
- New runtime OSS dependency → About credits (both locales)

### 7. Verify

```powershell
make test
# or
dotnet test SlopClean.slnx -c Release
```

Build the App if UI/localization/platform DI changed.

## Update an existing module

```
Update module:
- [ ] Red: failing test for new behavior or bug
- [ ] Minimal production change
- [ ] SafetyPolicy still respected (no convenience weaken)
- [ ] Parameters / op codes / privilege / AllowedRoot still correct
- [ ] Localization / AGENTS.md if user-facing text or MVP intent changed
- [ ] ModuleRegistrationTests only if Id/type/registration changed
- [ ] make test
```

Reference implementations:

| Goal | Start from |
|------|------------|
| File cleanup | `TempCleanerModule`, `BrowserCleanerModule` |
| Recycle bin | `RecycleBinModule` |
| Registry / startup | `StartupManagerModule`, `UninstallCleanupModule` |
| Scan-only | `DiskAnalyzerModule`, `ServiceAdvisorModule` |
| Elevated gated | `CoreIsolationDriversModule` + Elevated helper |

## Additional resources

- Contracts, findings, elevation, registration details: [reference.md](reference.md)
- Product architecture and safety: `AGENTS.md`
