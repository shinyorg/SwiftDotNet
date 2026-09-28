using System.Runtime.Versioning;

// CarPlay is iPhone-only; the iOS TFM would otherwise be analyzed as reachable from Mac Catalyst.
[assembly: UnsupportedOSPlatform("maccatalyst")]
