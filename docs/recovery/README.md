# OpenEdge recovery notes

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

## First milestone
- Project builds.
- App launches to `FrameWindow` and survives startup into the first-screen flow using the rebuilt `runtime/local/app/` layout.

## Phase 1 stabilization entrypoints
- Backlog and exit criteria: `docs/recovery/phase-1-baseline.md`
- Repeatable smoke check: `docs/recovery/smoke-check.ps1`

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

## Verified recovery decisions
- Remaining hand-built runtime paths in the current recovery scope were replaced with `RuntimePaths` helpers in `TalkBaseClass`, `MainWindow`, `Page1`, `ImageTagger`, and `WriteTask`.
- The rebuilt app no longer depends on stale loose files already sitting in `runtime/local/app/`; rebuilds repopulate `resources/` and `audio/` from `Data/`.
- Launch smoke testing is currently a controlled startup check: build, run `runtime/local/app/OpenEdge.exe`, verify the process stays alive, and confirm startup writes runtime markers such as `flags/temp/open.txt`.
- The first source-based media pipeline slice is live: shared media catalog, source enable/disable, image/video source toggles, and existing session/tag flows consuming the catalog instead of only hardcoded media folders.
- The first resilient tag-identity slice is live: tags still mirror to `tags.txt`, but the catalog now maintains identity-backed records in `media-tag-index.json` and uses those records to rebind tags when files move within configured sources.
