using Xunit;

namespace BDIT.TenantToolkit.Tests;

// Real-time cancellation deadlines must not compete with unrelated large-fixture/process tests.
// Isolation does not change any verification budget, outer deadline or safeguard assertion.
[CollectionDefinition("Executor deadlines", DisableParallelization = true)]
public sealed class ExecutorTimingCollection { }
