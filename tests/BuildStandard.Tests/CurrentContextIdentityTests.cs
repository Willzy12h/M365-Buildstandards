using BuildStandard.App.Services;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CurrentContextIdentityTests
{
    [Fact]
    public void Frozen_context_changes_for_same_tenant_account_connection_and_evidence_changes()
    {
        var profile = TestData.Profile(); var session = TestData.Session(); var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard); var connection = new object();
        PresentationContextKey Capture(object? c = null) => PresentationContextKey.Capture(profile, session, c ?? connection, standard, snapshot, true, null);
        var original = Capture(); Assert.Equal(original, Capture());
        session.AccountObjectId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        Assert.NotEqual(original, Capture());
        var changed = Capture(); Assert.NotEqual(changed, Capture(new object()));
        snapshot.Id = "different-evidence"; Assert.NotEqual(changed, Capture());
        Assert.Equal("different-evidence", snapshot.Id); // identity admission never mutates historical content
    }

    [Fact]
    public void Replaced_or_completed_outcome_is_not_an_old_export_context()
    {
        var run = new BuildStandard.Core.Models.DeploymentRun { Id = "original-run" };
        var profile = TestData.Profile(); var session = TestData.Session();
        PresentationContextKey Stable(BuildStandard.Core.Models.DeploymentRun? selected = null) => PresentationContextKey.Capture(profile, session, null, null, null, false, null, run: selected ?? run);
        var before = Stable(); run.Status = BuildStandard.Core.Models.RunStatus.Completed;
        Assert.NotEqual(before, Stable());
        Assert.NotEqual(Stable(), Stable(new() { Id = run.Id, Status = run.Status }));
    }
}
