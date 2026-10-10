using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExperimentalOperationGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private const string Tenant = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private static (object Connection, TenantSession Session, StandardCatalogue Standard, TenantProfile Profile) Context() =>
        (new object(), new TenantSession { TenantId = Tenant, AccountObjectId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            OperatorObjectId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", ClientId = "cccccccc-cccc-cccc-cccc-cccccccccccc",
            TenantVerified = true, OperatorVerified = true, Mode = SessionMode.Deployment,
            TokenExpiresAt = Now.AddHours(1).ToString("O"), Scopes = new() { "Policy.ReadWrite.ConditionalAccess", "User.Read" } },
            new StandardCatalogue { IntegrityDigest = "reviewed-catalogue" }, new TenantProfile { TenantId = Tenant });

    [Theory]
    [InlineData(ExperimentalOperation.Deploy)] [InlineData(ExperimentalOperation.ReviewedChange)]
    [InlineData(ExperimentalOperation.EntraLaps)] [InlineData(ExperimentalOperation.PublishPackage)] [InlineData(ExperimentalOperation.Recovery)]
    public void Every_operation_refuses_by_default_and_deliberate_approval_only_reaches_existing_review(ExperimentalOperation operation)
    {
        var c = Context(); var g = new ExperimentalOperationGuard(() => Now);
        var error = Assert.Throws<SafetyViolationException>(() => g.Require(operation, c.Connection, c.Session, c.Standard, c.Profile));
        Assert.Equal(ExperimentalOperationGuard.ClosedReason, error.Message);
        Assert.Throws<SafetyViolationException>(() => g.Enable(c.Connection, c.Session, c.Standard, c.Profile, false));
        g.Enable(c.Connection, c.Session, c.Standard, c.Profile, true);
        g.Require(operation, c.Connection, c.Session, c.Standard, c.Profile);
    }

    [Theory]
    [InlineData("tenant")] [InlineData("account")] [InlineData("operator")] [InlineData("client")]
    [InlineData("mode")] [InlineData("scope")] [InlineData("catalogue")] [InlineData("profile")]
    [InlineData("verified")] [InlineData("reconnect")] [InlineData("expiry")]
    public void Context_drift_permanently_closes_authorisation(string field)
    {
        var c = Context(); var time = Now; var g = new ExperimentalOperationGuard(() => time);
        g.Enable(c.Connection, c.Session, c.Standard, c.Profile, true);
        var connection = c.Connection;
        switch (field)
        {
            case "tenant": c.Session.TenantId = "dddddddd-dddd-dddd-dddd-dddddddddddd"; break;
            case "account": c.Session.AccountObjectId = "dddddddd-dddd-dddd-dddd-dddddddddddd"; break;
            case "operator": c.Session.OperatorObjectId = "dddddddd-dddd-dddd-dddd-dddddddddddd"; break;
            case "client": c.Session.ClientId = "dddddddd-dddd-dddd-dddd-dddddddddddd"; break;
            case "mode": c.Session.Mode = SessionMode.Assessment; break;
            case "scope": c.Session.Scopes.Add("Directory.ReadWrite.All"); break;
            case "catalogue": c.Standard.IntegrityDigest = "changed"; break;
            case "profile": c.Profile.Domain = "changed.example"; break;
            case "verified": c.Session.OperatorVerified = false; break;
            case "reconnect": connection = new object(); break;
            case "expiry": time = Now.AddHours(1); break;
        }
        Assert.False(g.IsEnabled(connection, c.Session, c.Standard, c.Profile));
        Assert.Throws<SafetyViolationException>(() => g.Require(ExperimentalOperation.Deploy, c.Connection, c.Session, c.Standard, c.Profile));
    }

    [Fact]
    public void Reordering_scopes_is_not_drift_and_explicit_invalidation_never_restores_a_grant()
    {
        var c = Context(); var g = new ExperimentalOperationGuard(() => Now);
        g.Enable(c.Connection, c.Session, c.Standard, c.Profile, true); c.Session.Scopes.Reverse();
        Assert.True(g.IsEnabled(c.Connection, c.Session, c.Standard, c.Profile));
        g.Invalidate(); Assert.False(g.IsEnabled(c.Connection, c.Session, c.Standard, c.Profile));
        Assert.False(new ExperimentalOperationGuard(() => Now).IsEnabled(c.Connection, c.Session, c.Standard, c.Profile));
    }

    [Fact]
    public void Setup_cannot_borrow_a_deployment_opt_in()
    {
        var c = Context(); var g = new ExperimentalOperationGuard(() => Now);
        g.Enable(c.Connection, c.Session, c.Standard, c.Profile, true);
        Assert.Throws<SafetyViolationException>(() => ExperimentalOperationGuard.RequireManualSetup(false, Tenant, Tenant, c.Session.AccountObjectId));
        Assert.Throws<SafetyViolationException>(() => ExperimentalOperationGuard.RequireManualSetup(true, "dddddddd-dddd-dddd-dddd-dddddddddddd", Tenant, c.Session.AccountObjectId));
        ExperimentalOperationGuard.RequireManualSetup(true, Tenant, Tenant, c.Session.AccountObjectId);
    }
}
