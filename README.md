## Working layout
- `src/OpenEdge/` contains the editable recovered source project.
- `runtime/sample/` contains a minimal committed runtime fixture.
- `runtime/local/` is the private working runtime used for local builds and launches.
- `Data/resources/` and `Data/audio/` are the canonical loose-asset sources copied into the rebuilt app output.
- `vendor/dotnet/` contains the repo-owned binary DLL dependencies used by the rebuilt project.
- The legacy decompiled project snapshot and duplicated recovery-era runtime trees have been removed from the active repo layout.

## Media source pipeline
- The app now has a shared media catalog layer (`MediaCatalogService`) that sits between folder scanning, tagging, and playback.
- Media sources are configured in `runtime/local/app/media-sources.json` at runtime.
- Media identity and tag persistence are now also tracked in `runtime/local/app/media-tag-index.json`.
- The current source UI is available from the main menu as `Media Sources`.
- Legacy app folders remain first-class sources through two built-in entries:
  - `Legacy Images` → `runtime/local/app/images/`
  - `Legacy Videos` → `runtime/local/app/videos/`
- The current tag system remains backward-compatible with `tags.txt`; the new source pipeline still mirrors tags there, but the catalog now keeps an internal identity record per media item so future moves can be matched more safely.

## Current launch contract
- Runtime file resolution is being centralized through `src/OpenEdge/OpenEdge/RuntimePaths.cs`.
- The executable is built to `runtime/local/app/`, and `RuntimePaths.RuntimeRoot` resolves inside that folder.
- Rebuilds now copy loose assets from `Data/resources/` to `runtime/local/app/resources/` and from `Data/audio/` to `runtime/local/app/audio/`.
- File-backed runtime state such as `flags/`, `lines/`, `images/`, `videos/`, `debug/`, `options.txt`, `tasks.txt`, and `tags.txt` is expected under `runtime/local/app/`.

## Canonical smoke-check command
From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File "docs/recovery/smoke-check.ps1"
```

This smoke check currently verifies the canonical recovery baseline:
- the project builds
- the rebuilt app output exists in `runtime/local/app/`
- loose `resources/` and `audio/` files are present in the rebuilt output
- the rebuilt executable stays alive through a controlled startup window
- startup writes `runtime/local/app/flags/temp/open.txt`

Current Phase 1 interpretation:
- the current repo should build with zero warnings and zero errors on the validated baseline
- smoke-check success means the build completes with zero errors and the launch/startup checks pass
