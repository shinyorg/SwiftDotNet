; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
SDN1001 | SwiftDotNet.Injection | Error | View with [Inject] members must be partial
SDN1002 | SwiftDotNet.Injection | Error | [Inject] property must be settable
SDN1003 | SwiftDotNet.Injection | Warning | [Inject] members will never be filled on this view
