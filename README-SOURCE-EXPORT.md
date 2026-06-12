# OpenEdge source export

This folder is a clean source-code export prepared for sharing/zipping.

Base line/vocab files are source assets in Data/lines and are copied to runtime/local/app/lines by the project build.

Included:
- src/ editable OpenEdge source
- tests/ SettingsHarness
- docs/ project/recovery/modding docs
- vendor/ repo-owned build DLLs
- Data/resources, Data/audio, and Data/lines canonical assets
- OpenEdge.sln and project metadata

Excluded:
- runtime/local app state/output
- mods
- flags, debug logs, user media/tag state
- bin/ obj/ build outputs
- .git history
- previous share/export folders
