# AGENTS.md — SlopClean

Guidance for humans and coding agents working in this repository.

**License:** AGPL-3.0-or-later  
**Product goal:** CCleaner-like Windows cleanup/optimization — modular, preview-first, no ads, no telemetry, no upselling.

---

## Architecture decisions

### Stack

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Runtime | .NET 10 (LTS) | Current LTS; support through Nov 2028 |
| UI | WinUI 3 (Windows App SDK), Windows-only | Microsoft’s current desktop stack for Win10/11 |
| Packaging (MVP) | Unpackaged, self-contained, x64 | Simpler admin/debug story; no separate runtime install for end users |
| License | AGPL-3.0-or-later | Keep the tool free and share-alike for everyone |
| Pattern | MVVM (CommunityToolkit.Mvvm) | Thin views, testable ViewModels |

Do **not** introduce WPF, Avalonia, Electron, or cross-platform UI unless the product scope changes explicitly.

### Solution layout

```
src/
  SlopClean.App/                 # WinUI host (shell, pages, controls, DI)
  SlopClean.Core/                # UI-/Windows-free contracts, engine, safety
  SlopClean.Platform.Windows/    # OS implementations (FS, registry, processes, services, broker)
  SlopClean.Elevated/            # Short-lived UAC helper for privileged ops only
  SlopClean.Modules/             # DI aggregator for built-in modules
  SlopClean.Modules.*/           # One assembly per built-in module
tests/
  SlopClean.Core.Tests/
  SlopClean.Modules.TestSupport/ # Shared fakes for module tests
  SlopClean.Modules.Tests/       # Aggregator DI registration tests
  SlopClean.Modules.*.Tests/     # One test project per module (fakes only)
  SlopClean.Platform.Windows.Tests/
```

**Rules:**

- Modules and Core must not reference WinUI or `Microsoft.Win32` APIs directly for business logic that can be abstracted.
- All OS access goes through Core abstractions (`IFileSystem`, `IRegistryStore`, `IProcessInspector`, `IServiceManager`, `IRecycleBinService`, `IPrivilegeBroker`) implemented in `Platform.Windows`.
- Built-in modules are registered via DI at startup. Runtime plugin DLL discovery is **out of MVP scope**.

### Module capabilities (not one fat interface)

Use capability interfaces so modules do not implement meaningless methods:

- `IModule` — identity, category, parameters
- `IScannableModule` — `ScanAsync` (always read-only)
- `IApplicableModule` — apply only explicit `OptimizationAction`s
- `IReversibleModule` — real restore path (e.g. `.reg` export + import)

Findings and actions are **immutable**. Actions carry `AllowedRoot`, `OperationCode`, and `RequiredPrivilege`. Unknown operation codes are rejected.

**Module authoring skill:** Creating, renewing, or updating built-in modules follows [`.cursor/skills/slopclean-modules/SKILL.md`](.cursor/skills/slopclean-modules/SKILL.md). Whenever the module architecture, contracts, SafetyPolicy/operation-code rules, DI registration, elevation model, test layout, or related best practices change, update that skill (and its `reference.md`) in the same change set so agents stay aligned with the codebase.

### Engine and scheduling

- `OptimizationEngine` orchestrates scan/apply, cancellation, and safety checks.
- At most one I/O-heavy scan per drive (`DriveScanScheduler`).
- Apply is **sequential**, idempotent where possible, and fault-isolated (one locked file must not abort the whole batch).
- Elevated actions go through `IPrivilegeBroker` → `SlopClean.Elevated`, not by relaunching the whole UI as admin.

### Elevation model

| Rule | Detail |
|------|--------|
| UI process | Always `asInvoker` |
| Privileged work | Separate short-lived WinUI helper via UAC (`runas`); shows current job while elevated work runs |
| IPC | Named pipe, same-user ACL, one-time nonce |
| Helper contract | Fixed operation codes only; re-validates `SafetyPolicy` independently; packaged under `elevated\` (self-contained) |

Never set `requireAdministrator` on the WinUI app.

### Safety policy (non-negotiable)

- Preview-first: no apply without scan + explicit selection.
- Scan never mutates system state.
- Canonicalize paths; re-validate immediately before apply.
- Do not follow reparse points (junctions/symlinks).
- Never delete drive roots, Windows roots, System32/SysWOW64 trees, or profile/AppData roots.
- Under `%windir%`, only `Windows\Temp` is a deletable subtree.
- Registry mutations only in allowlisted uninstall/run locations; backup + restore required for reversible registry modules.
- Logging is local, rotating, and path-redacted (`LogRedactor` / `RedactingEnricher`). No phone-home.

### UI (control-first)

- Shell and Pages stay thin (composition + navigation).
- Prefer domain-sized reusable controls (`ParameterForm`, `FindingList`, `ModuleCard`, `ScanProgressControl`, `PlannedChangeList`, `RestorePointList`).
- One generic `ModuleDetailPage` driven by module contracts — not seven giant per-module pages.
- Apply is never on the module page: Scan → select → **Review selected** → `ReviewPlanPage` → **Start cleanup** → `CleanTasksPage` (todo-list progress).
- `Clean Tasks` nav shows live/queued cleanup progress; `Restore` lists committed backups; Settings configures the backup directory.
- Avoid micro-controls without a clear responsibility.
- Large lists: virtualization / incremental results + cancellation.
- UI strings: `.resw` (de-DE / en-US), not hard-coded sprawl in XAML.

### MVP modules (intent)

| Module | Apply? | Notes |
|--------|--------|-------|
| TempCleaner | Yes | User + Windows Temp; **no Prefetch** |
| BrowserCleaner | Yes | Cache default; detect running browsers |
| RecycleBin | Yes | Confirm before empty |
| StartupManager | Yes | Disable/enable with restore; no hard delete in MVP |
| DiskAnalyzer | No | Analysis only — largest files or duplicates (size then SHA-1) |
| UninstallCleanup | Yes (conservative) | Orphaned uninstall/run entries; matching AppData leftovers are sized and deletable only when explicitly selected; backups via central restore store |
| ServiceAdvisor | No | Read-only curated JSON recommendations |
| CoreIsolationDrivers | Yes (gated) | Default scope = CI/Memory Integrity incompatible OEM packages (not all orphans); local PE HVCI heuristic (WX sections) + CI events; optional orphan OEM cleanup opt-in; in-use CI blockers need explicit allow + warning; elevated export/delete/restore; fail-closed incl. disconnected devices |

### Out of MVP

- External plugin DLLs
- Cloud sync, accounts, telemetry, update ads
- Aggressive registry/driver tweaks (beyond the gated Core Isolation Drivers module)
- Auto-deleting guessed program leftovers without an orphaned uninstall match and explicit selection
- Changing Windows service start types
- MSIX Store publishing (may follow)
- Enabling Memory Integrity itself; bundling `hvciscan`; hard-deleting `.sys` under System32

---

## Test-driven development (TDD)

TDD applies to **new behavior and bug fixes**. Bug fixes must land with a failing regression test first so the same defect cannot return unnoticed.

**Required workflow:**

1. **Red** — Write or update a failing test that describes the desired behavior (for bugs: a regression test that reproduces the failure).
2. **Green** — Implement the minimal production change to pass.
3. **Refactor** — Clean up with tests still green.

### Where tests live

| Layer | Project | Style |
|-------|---------|--------|
| Contracts, Safety, Engine | `SlopClean.Core.Tests` | Pure unit tests + fakes |
| Modules | `SlopClean.Modules.*.Tests` (+ `Modules.Tests` for DI registration) | Fakes only — never touch real user files, autostart, services, or production registry |
| Windows I/O / broker contracts | `SlopClean.Platform.Windows.Tests` | Temp directories / dedicated test keys; negative tests for elevated helper contracts |

### Hard TDD rules

- Destructive behavior (delete file/dir, registry mutate, empty recycle bin) **must** have tests before merge.
- SafetyPolicy rules are tested first; modules must not bypass the policy.
- Prefer fakes implementing Core abstractions over mocking frameworks for filesystem/registry.
- Do not “fix” by manually deleting real system paths in CI.
- **Bug fixes are TDD too:** when a bug is found (e.g. System32 delete incorrectly allowed), add a regression test that fails **before** fixing; do not ship a fix without that guard.

### Commands

```powershell
make ci          # restore, build, test
make test        # build + test
dotnet test SlopClean.slnx -c Release
```

GitHub Actions: `CI` (push), `PR` (pull requests), `Release` (tags `v*.*.*` / manual). Shared steps live in `.github/workflows/reusable-build.yml`.

---

## Agent working agreements

- Prefer `make` targets (`run`, `build`, `ci`, `release`) for local parity with CI.
- Keep PRs focused; do not expand scope into out-of-MVP features.
- Do not weaken SafetyPolicy for convenience.
- Do not add ads, telemetry, or remote calls.
- Match existing naming, DI registration patterns, and control-first UI structure.
- When adding, replacing, or removing a product-facing runtime open-source dependency, update the About page credits (name, official project URL, license id, short excerpt in `de-DE` and `en-US` `.resw`). Do not list test- or build-only packages.
- After substantive changes: run tests (and App build if UI/platform touched).
- Commit only when the user asks; never force-push `main`.
- **Plan mode language:** Plans produced in plan mode must always be written in English (titles, sections, todos, trade-off notes). Follow this even when the user requests changes or asks questions in another language. Chat replies outside the plan artifact may still match the user's language.
