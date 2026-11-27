; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
EMBA001 | HotReload | Warning | Static member of a generic type will not survive hot reload
EMBA002 | HotReload | Warning | Field type cannot be migrated by hot reload
EMBA003 | HotReload | Error | [ReloadInitializer] target is not usable
EMBA005 | HotReload | Warning | [ReloadIgnore] type implements reload hooks

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
PROWLHR001 | HotReload | Warning | Replaced by EMBA001
