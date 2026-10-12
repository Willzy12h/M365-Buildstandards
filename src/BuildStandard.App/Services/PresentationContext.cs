using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
namespace BuildStandard.App.Services;

/// <summary>Transient display/copy/export identity; never authorises a tenant operation.</summary>
public sealed record PresentationContextKey(object? ProfileInstance, object? ConnectionInstance, object? SessionInstance,
    string ProfileIdentity, string AccountContext, string StandardDigest, string EvidenceIdentity, long Generation = 0,
    object? SelectedOutcome = null, string OutcomeIdentity = "")
{
    public static PresentationContextKey Capture(TenantProfile? profile, TenantSession? session,
        object? connection, StandardCatalogue? standard, TenantSnapshot? snapshot, bool live, TenantSnapshot? exchange,
        AssessmentResult? assessment = null, DeploymentPlan? plan = null, DeploymentRun? run = null)
        => new(profile, connection, session, ToolkitJson.Serialize(profile), ToolkitJson.Serialize(new { Session = session, Resource = session is null ? "offline" : "Microsoft Graph" }),
            standard?.IntegrityDigest ?? "", ToolkitJson.Serialize(new { Snapshot = snapshot?.Id,
                Digest = snapshot?.IntegrityDigest, Live = live, Exchange = exchange?.Id, ExchangeDigest = exchange?.IntegrityDigest }),
            SelectedOutcome: run, OutcomeIdentity: ToolkitJson.Serialize(new { Assessment = assessment?.Id, Plan = plan?.PlanDigest,
                Run = run?.Id, Status = run?.Status, Ended = run?.EndedAt, Before = run?.BeforeSnapshotId, After = run?.AfterSnapshotId }));
}
