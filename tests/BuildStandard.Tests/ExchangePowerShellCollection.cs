using Xunit;
namespace BuildStandard.Tests;

// Cold PowerShell hosts share the Windows runner's CPU/start-up resources. Keep these process suites serial with
// each other while pure managed tests remain parallel. No test, deadline, runtime or safeguard is skipped/relaxed.
[CollectionDefinition("Exchange PowerShell fixtures")]
public sealed class ExchangePowerShellCollection { }
