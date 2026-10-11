using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>Existing in-memory state only; projecting Home never loads evidence or evaluates write authority.</summary>
public sealed record HomeTaskContext
{
    public TenantProfile? Profile { get; init; }
    public TenantSession? Session { get; init; }
    public StandardCatalogue? Standard { get; init; }
    public TenantSnapshot? Snapshot { get; init; }
    public bool SnapshotIsLive { get; init; }
    public TenantSnapshot? ExchangeSnapshot { get; init; }
    public bool ExchangeCapturedByTool { get; init; }
    public string? DisplayedTenantId { get; init; }
    public bool ApplicationSetupActive { get; init; }
    public bool Busy { get; init; }
    public string BusyMessage { get; init; } = "";
    public string InterruptedNotice { get; init; } = "";
}

/// <summary>Presentation guidance, never an assessment, completion claim or permission to execute.</summary>
public sealed record HomeTaskProjection(string Headline, string Guidance, string NextPageKey, string NextActionLabel,
    string ClientText, string ConnectionText, string EvidenceText, string StandardText)
{
    public string ContextSummary => string.Join(" · ", ClientText.Split('\n')[0], ConnectionText.Split('\n')[0],
        EvidenceText.Split('\n')[0], StandardText.Split('\n')[0]);
    public const string BoundaryText = "Scoped checks cover their selected requirements only. Job completion needs reviewed evidence and engineer checks. "
        + "Manual steps remain engineer actions. Experimental tenant changes need session-specific opt-in and exact-action approval on their existing pages.";

    public static HomeTaskProjection Create(HomeTaskContext context)
    {
        var profile = context.Profile;
        var session = context.Session;
        var standard = context.Standard;
        var snapshot = context.Snapshot;
        var capture = snapshot ?? context.ExchangeSnapshot;
        var client = profile is null ? "No client selected · choose a saved client or create one on Connect."
            : $"{Name(profile.Company, "Saved client")}\n{Name(profile.Domain, "No reference domain saved")} · saved reference";
        var connection = context.ApplicationSetupActive ? "Application setup · write-capable access. Review the setup identity in the pinned header."
            : session is null ? "Offline · no active connection. Saved client details are local references."
            : !SameTenant(session.TenantId, profile?.TenantId) ? "Connection belongs to another client · return to Connect and verify the target."
            : !session.TenantVerified || !session.OperatorVerified ? "Connection identity needs verification · return to Connect."
            : (session.Mode == SessionMode.Deployment || session.HasWriteScopes ? "Write-capable access" : "Read-only assessment access")
                + "\nReview the signed-in identity and exact access in the pinned header.";
        var standardText = standard is null ? "No valid Build Standard loaded · review Settings and diagnostics."
            : $"Build Standard {standard.Release}\nLocal catalogue · exact version details are in the pinned header.";
        var captureMatches = capture is not null && SameTenant(capture.TenantId, profile?.TenantId)
            && (session is null || SameTenant(capture.TenantId, session.TenantId));
        var complete = snapshot is not null && snapshot.Complete && snapshot.Collections.Values.All(c => c.Usable && c.Count == c.Items.Count)
            && (standard is null || standard.Collections.Keys.All(snapshot.Collections.ContainsKey));
        var liveCapture = context.SnapshotIsLive && session is { TenantVerified: true, OperatorVerified: true }
            && snapshot is not null && SameTenant(snapshot.TenantId, session.TenantId);
        // Use the pinned header's source labels, including separate Exchange origin/completeness and tenant refusal.
        // Collection review below is deliberately stricter than the capture's recorded Complete flag; neither grants authority.
        var displayedTenant = context.DisplayedTenantId ?? session?.TenantId ?? profile?.TenantId ?? "";
        var sourceMatches = captureMatches && SameTenant(capture?.TenantId, displayedTenant);
        var source = EvidenceSourcePresentation.Create(snapshot, context.SnapshotIsLive, context.ExchangeSnapshot,
            context.ExchangeCapturedByTool, profile?.TenantId, displayedTenant);
        var evidence = source.Summary + (source.Details.Length > 0 ? "\n" + source.Details : "");
        if (capture is null)
            evidence += "\nNo full capture open · use Configuration to capture or open saved evidence.";
        else if (sourceMatches && snapshot is null)
            evidence += "\nSupplemental observations only · full Graph evidence is not open.";
        else if (sourceMatches)
        {
            evidence += "\nCollection review: " + (complete ? "collections reported complete; review findings"
                : "incomplete or partial; unread settings remain unknown");
            if (context.SnapshotIsLive && !liveCapture)
                evidence += "\nPreviously collected evidence · no verified current connection.";
        }
        if (sourceMatches && snapshot is not null && standard is not null && snapshot.StandardRelease != standard.Release)
            evidence += $"\nCaptured against standard {Name(snapshot.StandardRelease, "unknown")}; current standard {standard.Release}.";

        var headline = profile is null ? "What would you like to work on?" : $"Your next steps for {Name(profile.Company, "this client")}";
        var next = (Page: "connect", Label: "Open Connect", Reason: "Choose a saved client for local evidence review, or connect read-only to confirm the tenant and collect its configuration.");
        if (context.Busy)
            next = ("overview", "Open client overview", $"An operation is in progress. {context.BusyMessage} Review its pinned status and wait before starting another operation.");
        else if (standard is null)
            next = ("settings", "Open Settings and diagnostics", "Load a valid Build Standard before assessing client evidence. Your saved work remains available in its existing pages.");
        else if (context.ApplicationSetupActive)
            next = ("setup", "Review application setup", "Finish reviewing the setup identity and validation, then deliberately connect with assessment access.");
        else if (session is not null && (!SameTenant(session.TenantId, profile?.TenantId) || !session.TenantVerified || !session.OperatorVerified))
            next = ("connect", "Review connection identity", "Confirm the selected client, tenant and signed-in engineer on Connect before collecting or interpreting evidence.");
        else if (profile is not null)
        {
            if (context.InterruptedNotice.Length > 0)
                next = ("recovery", "Review interrupted work", context.InterruptedNotice + " Review the local change register and recorded outcomes before repeating an action.");
            else if (capture is not null && !captureMatches)
                next = ("configuration", "Open this client's evidence", "The open capture belongs to a different client. Reopen this client's saved evidence or collect a fresh read-only capture.");
            else if (snapshot is null)
                next = ("configuration", "Open Configuration", session is null
                    ? "Open a saved capture for historical review. To collect current settings, deliberately connect read-only first."
                    : "Review Configuration and capture the tenant read-only. Supplemental or scoped observations do not establish full before-change evidence.");
            else if (!liveCapture)
                next = ("assessment", "Review original findings", (complete ? "Review the original evidence and its limitations. "
                    : "This capture is incomplete or partial. Unread settings remain unknown; review the original findings and collection gaps. ")
                    + "Historical or disconnected evidence cannot authorise deployment; reconnect and collect fresh evidence before planning a change.");
            else if (!complete)
                next = ("configuration", "Review collection gaps", "Review failed, partial and unattempted collections. Unread settings remain unknown; collect complete fresh evidence before planning changes.");
            else if (snapshot.StandardRelease != standard.Release)
                next = ("history", "Review evidence and standard changes", "This capture refers to a different Build Standard. Review version differences and recapture against the current catalogue before planning.");
            else if (session is null)
                next = ("configuration", "Review saved evidence", "No connection is active. Review original evidence locally and reconnect read-only for a fresh capture before planning changes.");
            else
                next = ("assessment", "Review assessment findings", "Inspect findings, unknowns, prerequisites and manual checks. Complete collection is an evidence status; every planned change still needs separate review and approval.");
        }
        return new(headline, next.Reason, next.Page, next.Label, client, connection, evidence, standardText);
    }

    private static bool SameTenant(string? left, string? right) => !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Name(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
