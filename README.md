# SlopClean

Free, open-source Windows cleanup and optimization tool — modular, preview-first, no ads, no telemetry.

**License:** [AGPL-3.0-or-later](LICENSE)

## Features (MVP)

- Temp Cleaner
- Browser Cleaner (Chrome / Edge / Firefox)
- Recycle Bin
- Startup Manager
- Disk Analyzer (read-only)
- Uninstall Cleanup (conservative leftover detection; AppData leftovers selectable)
- Review step before apply, Clean Tasks page with todo-style progress, local backups, in-app Restore
- Service Advisor (read-only recommendations)

## Requirements

- Windows 10 2004+ / Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build

## Build

```powershell
dotnet restore SlopClean.slnx
dotnet build src/SlopClean.Elevated/SlopClean.Elevated.csproj -c Release
dotnet build src/SlopClean.App/SlopClean.App.csproj -c Release -r win-x64
dotnet test SlopClean.slnx -c Release
```

## Run (unpackaged)

```powershell
dotnet run --project src/SlopClean.App/SlopClean.App.csproj -c Debug
```

## Publish self-contained ZIP

```powershell
dotnet publish src/SlopClean.App/SlopClean.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/slopclean-win-x64
```

The app is unpackaged and self-contained (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`).

## Architecture

- `SlopClean.Core` — module contracts, engine, safety policy
- `SlopClean.Platform.Windows` — filesystem, registry, services, elevated broker
- `SlopClean.Elevated` — short-lived UAC helper for privileged operations
- `SlopClean.Modules` — DI aggregator for built-in modules (`SlopClean.Modules.*`)
- `SlopClean.App` — WinUI 3 shell (control-first UI)

## Safety

- Scan is always read-only
- Apply only runs on explicitly selected findings
- Paths are re-validated immediately before changes
- Reparse points are not followed
- Registry changes create `.reg` backups with restore support
- No phone-home, no ads

## Development

Prefer **test-driven development**: write or update failing tests first, then implement the minimal change, then refactor. Destructive behavior must be covered by Core/Module tests against fakes before merging.

```powershell
dotnet test SlopClean.slnx -c Release
```

## Contributing

Issues and pull requests are welcome. Please keep destructive operations conservative and covered by tests.
