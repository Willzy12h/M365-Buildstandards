using System.Diagnostics;
using BuildStandard.Core;
namespace BuildStandard.Engine.Scripts.Runner;
/// <summary>INT-128 sampled owned-host private memory, not an OS hard limit or whole-system/process-tree accounting.</summary>
internal sealed class ReadRunnerMemoryLimit(Func<Process,long>? sample=null)
{
    public const long MaximumPrivateBytes=1024L*1024*1024;
    public bool Exceeded {get;private set;}
    public bool Check(Process ownedProcess)
    {
        if (Exceeded) return true;
        long bytes;
        try
        {
            if (sample is not null) bytes = sample(ownedProcess);
            else { ownedProcess.Refresh(); bytes = ownedProcess.PrivateMemorySize64; }
        }
        catch (InvalidOperationException) when (ownedProcess.HasExited)
        {
            // The exact owned host can exit after the poll selected its timer but before memory sampling.
            // There is no running host left to measure; preserve any already-latched breach above. A failed
            // measurement of a still-running host continues to fail, never becoming a known-zero sample.
            return false;
        }
        if (bytes < 0) throw new ConfigurationException("The owned report host's private memory could not be measured; zero is not assumed.");
        Exceeded = bytes > MaximumPrivateBytes;
        return Exceeded;
    }
}
