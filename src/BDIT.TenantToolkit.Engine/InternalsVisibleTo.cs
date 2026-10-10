using System.Runtime.CompilerServices;

// The runner's host (runtime location, environment, staging folder) is fixed in production. Only the engine tests may
// substitute it, to run the synthetic module and refused-host regressions.
[assembly: InternalsVisibleTo("BDIT.TenantToolkit.Tests")]
