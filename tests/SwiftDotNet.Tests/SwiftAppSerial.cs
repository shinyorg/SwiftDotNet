using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Marker collection for tests that drive <see cref="SwiftDotNet.SwiftApp"/>'s shared static state.
/// Membership disables parallel execution between them so render counts stay deterministic.
/// </summary>
[CollectionDefinition(nameof(SwiftAppSerial), DisableParallelization = true)]
public sealed class SwiftAppSerial;
