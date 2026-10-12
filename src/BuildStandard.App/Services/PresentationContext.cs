using System.Runtime.CompilerServices;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
namespace BuildStandard.App.Services;

/// <summary>Transient display/copy/export identity; never authorises a tenant operation.</summary>
public sealed record PresentationContextKey(Guid? ProfileInstance, Guid? ConnectionInstance, Guid? SessionInstance,
    string ProfileIdentity, string AccountContext, string StandardDigest, string EvidenceIdentity, long Generation = 0,
    Guid? SelectedOutcome = null, string OutcomeIdentity = "", Guid? SetupInstance = null, string PrivilegedIdentity = "")
{
    private sealed class Identity { public Guid Id { get; } = Guid.NewGuid(); }
    private static readonly ConditionalWeakTable<object, Identity> Identities = new();
    internal static Guid? IdentityFor(object? value) => value is null ? null : Identities.GetValue(value, static _ => new Identity()).Id;
    public static PresentationContextKey Capture(TenantProfile? profile, TenantSession? session,
        object? connection, StandardCatalogue? standard, TenantSnapshot? snapshot, bool live, TenantSnapshot? exchange,
        AssessmentResult? assessment = null, DeploymentPlan? plan = null, DeploymentRun? run = null)
        => new(IdentityFor(profile), IdentityFor(connection), IdentityFor(session), ToolkitJson.Serialize(profile), ToolkitJson.Serialize(new { Session = session, Resource = session is null ? "offline" : "Microsoft Graph" }),
            standard?.IntegrityDigest ?? "", ToolkitJson.Serialize(new { Snapshot = snapshot?.Id,
                Digest = snapshot?.IntegrityDigest, Live = live, Exchange = exchange?.Id, ExchangeDigest = exchange?.IntegrityDigest }),
            SelectedOutcome: IdentityFor(run), OutcomeIdentity: ToolkitJson.Serialize(new { Assessment = assessment?.Id, Plan = plan?.PlanDigest,
                Run = run?.Id, Status = run?.Status, Ended = run?.EndedAt, Before = run?.BeforeSnapshotId, After = run?.AfterSnapshotId }));
}
