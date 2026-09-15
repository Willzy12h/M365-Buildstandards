using BDIT.TenantToolkit.Core.Diagnostics;

namespace BDIT.TenantToolkit.Engine.Evidence;

/// <summary>
/// Persists a run's terminal state without ever masking the failure that ended it.
///
/// Every write service records durable intent before the transport call, so a failed final save leaves the stored
/// record at its last (blocking) state rather than losing the attempt. What a failed final save would otherwise lose
/// is the terminal reason and the exception the caller sees: an "access denied" from storage replacing an ambiguous
/// tenant write. This helper records the save failure on the run, retries the save once, and swallows a second
/// failure so the object returned to the caller stays truthful even when storage is not - and any exception from the
/// try block keeps propagating unchanged.
/// </summary>
public static class EvidencePersistence
{
    public static void SaveTerminal(Action save, Action<string> recordSaveFailure)
    {
        try { save(); return; }
        catch (Exception ex)
        {
            recordSaveFailure("Final evidence could not be saved: " + SensitiveDataScrubber.Scrub(ex.Message));
        }
        try { save(); }
        catch (Exception) { /* The visible result must remain truthful even if storage is unavailable. */ }
    }

    /// <summary>Appends a save-failure message to an existing error string without discarding the original reason.</summary>
    public static string Append(string? existing, string message) => string.IsNullOrEmpty(existing) ? message : existing + " " + message;
}
