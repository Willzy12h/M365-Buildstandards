using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using System.Text.Json;
using BuildStandard.Graph;
using BuildStandard.Graph.Setup;
using BuildStandard.Engine.Standards;
using Xunit;

namespace BuildStandard.Tests;

public sealed class TokenCapabilityTests
{
    [Theory]
    [InlineData("Directory.AccessAsUser.All")]
    [InlineData("User.Invite.All")]
    [InlineData("Mail.Send")]
    [InlineData("https://graph.microsoft.com/Mail.Send")]
    public void Returned_broader_delegated_authority_is_never_reported_as_no_write_scope(string scope)
    {
        Assert.True(new TenantSession { Scopes = new() { "User.Read", scope } }.HasWriteScopes);
    }

    [Theory]
    [InlineData("User.Invite.All")]
    [InlineData("Mail.Send")]
    [InlineData("https://other.example/User.Read")]
    [InlineData("Future.Read.All")]
    [InlineData("openid")]
    public async Task Quick_connect_refuses_unreviewed_requested_capabilities_before_acquisition(string scope)
    {
        using var http = new HttpClient();
        var calls = 0;
        await Assert.ThrowsAsync<ConfigurationException>(() => TenantDiscoveryService.DiscoverRetainedAsync(http,
            new ToolkitSettings(), TestData.Standard(), new[] { scope }, NullLog.Instance, CancellationToken.None,
            (_, _) => { calls++; throw new InvalidOperationException("Synthetic acquisition must not run"); }));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("User.Read", TokenCapability.ReadOnlyVerified)]
    [InlineData("https://graph.microsoft.com/user.read,Organization.Read.All,openid,profile,offline_access,email", TokenCapability.ReadOnlyVerified)]
    [InlineData("Directory.Read.All,AuditLog.Read.All", TokenCapability.ReadOnlyVerified)]
    [InlineData("User.Read,User.Invite.All", TokenCapability.BroaderPresent)]
    [InlineData("https://graph.microsoft.com/Mail.Send", TokenCapability.BroaderPresent)]
    [InlineData("Directory.AccessAsUser.All", TokenCapability.BroaderPresent)]
    [InlineData("User.Read,Future.Read.All", TokenCapability.Unclassified)]
    [InlineData("https://other.example/User.Read", TokenCapability.Unclassified)]
    [InlineData("https://graph.microsoft.com.evil.invalid/User.Read", TokenCapability.Unclassified)]
    [InlineData("http://graph.microsoft.com/User.Read", TokenCapability.Unclassified)]
    [InlineData("https://graph.microsoft.com/openid", TokenCapability.Unclassified)]
    [InlineData("openid,profile,offline_access", TokenCapability.Unclassified)]
    [InlineData("", TokenCapability.Unclassified)]
    [InlineData("User.Read,", TokenCapability.Unclassified)]
    [InlineData("User.Read, User.Read", TokenCapability.Unclassified)]
    [InlineData("User.Read,Future.Write.All", TokenCapability.Unclassified)]
    public void Explicit_classification_never_infers_unknown_access_from_spelling(string scopes, TokenCapability expected)
    {
        var result = TokenCapabilityClassifier.Classify(scopes.Split(','));
        Assert.Equal(expected, result.Classification);
        Assert.Equal(scopes.Split(',').Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x),
            result.ClassifiedScopes.Concat(result.UnclassifiedScopes).OrderBy(x => x));
    }

    [Theory]
    [InlineData("User.Read", true)]
    [InlineData("Directory.AccessAsUser.All", false)]
    [InlineData("User.Invite.All", false)]
    [InlineData("Mail.Send", false)]
    [InlineData("Future.Read.All", false)]
    [InlineData("openid", false)]
    [InlineData("", false)]
    public void Dedicated_assessment_requires_verified_read_capabilities_shared_fallback_warns(string scope, bool accepted)
    {
        var dedicated = new TenantSession { Scopes = new() { scope } };
        if (accepted) TenantConnectionService.ApplyAssessmentCapabilityPolicy(dedicated, false);
        else Assert.Throws<ConfigurationException>(() => TenantConnectionService.ApplyAssessmentCapabilityPolicy(dedicated, false));
        var shared = new TenantSession { Scopes = new() { scope } };
        TenantConnectionService.ApplyAssessmentCapabilityPolicy(shared, true);
        Assert.Equal(accepted ? 0 : 1, shared.Notices.Count);
        if (!accepted) Assert.Contains("toolkit changes blocked", shared.Notices[0], StringComparison.Ordinal);
        // Classification/presentation cannot make a deployment permission request or change the chosen mode.
        Assert.Equal(SessionMode.Assessment, shared.Mode); Assert.Equal(new[] { scope }, shared.Scopes);
    }

    [Fact]
    public void Mixed_broader_and_unknown_scopes_keep_both_facts_and_missing_scopes_are_not_read_only()
    {
        var mixed = TokenCapabilityClassifier.Classify(new[] { "Mail.Send", "https://outlook.office365.com/User.Read" });
        Assert.Equal(TokenCapability.BroaderPresent, mixed.Classification);
        Assert.Equal(new[] { "Mail.Send" }, mixed.ClassifiedScopes);
        Assert.Equal(new[] { "https://outlook.office365.com/User.Read" }, mixed.UnclassifiedScopes);
        Assert.Contains("not classified", mixed.Description, StringComparison.Ordinal);
        Assert.Equal(TokenCapability.Unclassified, TokenCapabilityClassifier.Classify(null).Classification);
        Assert.Equal(TokenCapability.Unclassified, TokenCapabilityClassifier.Classify(Array.Empty<string>()).Classification);
        Assert.Equal(TokenCapability.Unclassified, TokenCapabilityClassifier.Classify(new[] { "User.Read", null! }).Classification);
        Assert.DoesNotContain("TokenCapabilities", JsonSerializer.Serialize(new TenantSession { Scopes = new() { "User.Read" } }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("User.Invite.All")]
    [InlineData("Mail.Send")]
    [InlineData("Future.Read.All")]
    public void Assessment_registration_requests_use_the_same_conservative_classification(string scope)
    {
        var standard = TestData.Standard(); standard.Collections.Values.First().Scope = scope;
        Assert.Throws<ConfigurationException>(() => ApplicationSetupService.RequiredScopes(standard, SessionMode.Assessment));
    }

    [Fact]
    public void Every_existing_catalogue_request_is_classified_without_adding_or_removing_permissions()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards"));
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Where(f => Path.GetFileName(f) != "manifest.json"))
        {
            var standard = StandardsLoader.Parse(File.ReadAllText(file), Path.GetFileName(file));
            var assessment = ApplicationSetupService.RequiredScopes(standard, SessionMode.Assessment);
            var deployment = ApplicationSetupService.RequiredScopes(standard, SessionMode.Deployment);
            Assert.Equal(TokenCapability.ReadOnlyVerified, TokenCapabilityClassifier.Classify(assessment).Classification);
            Assert.Empty(TokenCapabilityClassifier.Classify(deployment).UnclassifiedScopes);
            Assert.Equal(TenantConnectionService.DiagnosticScopes.Concat(standard.ReadScopes()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase), assessment);
            Assert.Equal(TenantConnectionService.DiagnosticScopes.Concat(standard.ReadScopes()).Concat(standard.WriteScopes()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase), deployment);
        }
    }

    [Theory]
    [InlineData("User.Invite.All")]
    [InlineData("Mail.Send")]
    [InlineData("Future.Read.All")]
    public async Task Explicit_assessment_connection_refuses_broader_or_unknown_requests_before_acquisition(string scope)
    {
        using var root = new TempRoot(); using var http = new HttpClient(); var calls = 0;
        var standard = TestData.Standard(); standard.Collections.Values.First().Scope = scope;
        var service = new TenantConnectionService(new ToolkitSettings(), root.Paths, http, NullLog.Instance, "synthetic");
        await Assert.ThrowsAsync<ConfigurationException>(() => service.ConnectAsync(TestData.Profile(), SessionMode.Assessment,
            standard, null, CancellationToken.None, null, null,
            (_, _, _) => { calls++; throw new InvalidOperationException("Synthetic acquisition must not run"); }));
        Assert.Equal(0, calls);
    }
}
