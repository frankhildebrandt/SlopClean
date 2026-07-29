# SlopClean modules — reference

Read this when implementing or changing module contracts, safety, elevation, or registration.

## Capability interfaces

| Interface | Path | Role |
|-----------|------|------|
| `IModule` | `src/SlopClean.Core/Modules/IModule.cs` | `Id`, `Name`, `Description`, `Category`, `Parameters` |
| `IScannableModule` | `…/IScannableModule.cs` | `ScanAsync` → `IAsyncEnumerable<ScanFinding>` |
| `IApplicableModule` | `…/IApplicableModule.cs` | `ApplyAsync(OptimizationAction)` → `ApplyResult` |
| `IReversibleModule` | `…/IReversibleModule.cs` | Module-local restore; built-ins usually rely on central `IBackupService` / restore store instead |
| `IModuleIllustration` | `…/IModuleIllustration.cs` | `OpenIllustration()` stream |

Illustration lookup: `EmbeddedResourceStreams.OpenModuleIllustration(Type)` expects an embedded resource ending in `.Assets.illustration.png`.

## Models

`ScanFinding` / `OptimizationAction` live under `src/SlopClean.Core/Models/`.

- Findings and actions are immutable records.
- Actionable findings must set `Metadata[OptimizationAction.OperationCodeMetadataKey]`.
- `OptimizationAction.FromFinding(finding)` reads that metadata key.
- Path deletes need `AllowedRoot` + a code in `PrivilegedOperationCodes`.
- `RequiredPrivilege`: `None` or `Elevated` per finding.

## Operation codes and safety

- Codes: `src/SlopClean.Core/Abstractions/PrivilegedOperationCodes.cs` (`All` set is authoritative).
- Validation: `src/SlopClean.Core/Safety/SafetyPolicy.cs`
  - Path ops: canonicalize, no reparse points, never drive/Windows/System32/profile roots; under `%windir%` only `Windows\Temp`
  - Registry ops: allowlisted Uninstall / Run / StartupApproved prefixes only
- Engine re-validates before apply (`OptimizationEngine.ApplyOneAsync`).
- Elevated helper re-validates independently.

## DI and discovery

- Aggregator: `src/SlopClean.Modules/ServiceCollectionExtensions.cs` → `AddSingleton<IModule, T>()`
- Registry: `ModuleRegistry` from `IEnumerable<IModule>`, keyed by `Id` (case-insensitive)
- App: `AddSlopCleanModules()` + `AddSlopCleanWindowsPlatform()` — no per-module App wiring
- UI lists: `DashboardViewModel`, `ModulesPage` map `ModuleRegistry.All` through `ModuleLocalization` + `ModuleImagery`
- Detail: `ModuleDetailViewModel` — `CanReview = module is IApplicableModule`

## Apply orchestration (engine)

For each selected action roughly:

1. `SafetyPolicy.ValidateAction`
2. Optional backup via `IBackupService` when supported
3. If `RequiredPrivilege == Elevated` → `IPrivilegeBroker.BeginElevatedSessionAsync` / `ExecuteAsync`
4. Else → `IApplicableModule.ApplyAsync`
5. Commit or discard restore point

Driver package delete/restore must be elevated; engine fail-closes if marked otherwise.

## Elevation pattern (CoreIsolationDrivers)

- Findings use elevated op codes + `RequiredPrivilege.Elevated`
- Module `ApplyAsync` returns `Failed` for those codes (“must run through the elevated helper”)
- Implementation: `SlopClean.Elevated` + named-pipe broker; fixed op codes only
- UI process stays `asInvoker` — never `requireAdministrator` on the WinUI app

## Parameters

`src/SlopClean.Core/Parameters/`: `BoolParameter`, `IntParameter`, `EnumParameter`, `PathListParameter`.

Use `parameter.Resolve(parametersDictionary)` in `ScanAsync`. Saved presets are restored generically in the App.

## Localization

- Prefer `.resw` keys + `ModuleLocalization.Resolve` branch (pattern used by Core Isolation Drivers).
- Older modules still hard-code English `Name`/`Description` — improve when touching them; do not expand the gap for new modules.

## Tests to touch

| Change | Tests |
|--------|--------|
| New module | New `SlopClean.Modules.<Name>.Tests` + update `ModuleRegistrationTests` count/assertions |
| New op code / safety rule | `SlopClean.Core.Tests` SafetyPolicy (+ elevated helper negatives if elevated) |
| Destructive apply | Module tests with fakes; engine backup/restore when backup applies |
| New platform abstraction | Platform tests + fake in `ModuleRegistrationTests.RegisterPlatformFakes` |

Shared fakes: `tests/SlopClean.Modules.TestSupport/`.

## Existing modules (reference)

| ModuleId | Project | Applicable |
|----------|---------|------------|
| `temp-cleaner` | `SlopClean.Modules.TempCleaner` | Yes |
| `recycle-bin` | `SlopClean.Modules.RecycleBin` | Yes |
| `browser-cleaner` | `SlopClean.Modules.BrowserCleaner` | Yes |
| `startup-manager` | `SlopClean.Modules.StartupManager` | Yes |
| `disk-analyzer` | `SlopClean.Modules.DiskAnalyzer` | No |
| `uninstall-cleanup` | `SlopClean.Modules.UninstallCleanup` | Yes |
| `service-advisor` | `SlopClean.Modules.ServiceAdvisor` | No |
| `core-isolation-drivers` | `SlopClean.Modules.CoreIsolationDrivers` | Yes (gated/elevated) |
