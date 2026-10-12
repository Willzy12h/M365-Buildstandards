using System.Runtime.CompilerServices;

// Only the shared engine constructs admitted catalogue descriptors; native/CLI consumers read them.
[assembly: InternalsVisibleTo("BuildStandard.Engine")]
