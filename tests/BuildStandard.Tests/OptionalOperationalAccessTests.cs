using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Graph;
using BuildStandard.Graph.Setup;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OptionalOperationalAccessTests
{
    [Theory]
    [InlineData(2, SessionMode.Assessment)][InlineData(2, SessionMode.Deployment)]
    [InlineData(4, SessionMode.Assessment)][InlineData(4, SessionMode.Deployment)]
    [InlineData(3, SessionMode.Assessment)][InlineData(3, SessionMode.Deployment)]
    [InlineData(5, SessionMode.Assessment)][InlineData(5, SessionMode.Deployment)]
    [InlineData(6, SessionMode.Assessment)][InlineData(6, SessionMode.Deployment)]
    [InlineData(7, SessionMode.Assessment)][InlineData(7, SessionMode.Deployment)]
    public void Only_deliberately_selected_fixed_read_purposes_enter_existing_connection_and_setup_scopes(int bits, SessionMode mode)
    {
        using var root = new TempRoot(); using var http = new HttpClient(); var standard = TestData.Standard(); var before = ToolkitJson.Serialize(standard);
        var service = new TenantConnectionService(new ToolkitSettings(), root.Paths, http, NullLog.Instance, "synthetic");
        var normal = service.ScopesFor(mode, standard); var choice = (OptionalReportAccess)bits;
        var expected = new List<string>();
        if ((bits & 1) != 0) expected.Add("Reports.Read.All");
        if ((bits & 2) != 0) expected.Add("ServiceHealth.Read.All");
        if ((bits & 4) != 0) expected.Add("DelegatedAdminRelationship.Read.All");
        var requested = service.ScopesFor(mode, standard, choice);
        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), requested.Except(normal).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(requested, ApplicationSetupService.RequiredScopes(standard, mode, choice)); Assert.Equal(before, ToolkitJson.Serialize(standard));
        Assert.DoesNotContain("ServiceHealth.Read.All", normal); Assert.DoesNotContain("DelegatedAdminRelationship.Read.All", normal);
    }
    [Theory][InlineData("ServiceHealth.Read.All")][InlineData("DelegatedAdminRelationship.Read.All")]
    [InlineData("https://graph.microsoft.com/ServiceHealth.Read.All")][InlineData("https://graph.microsoft.com/DelegatedAdminRelationship.Read.All")]
    public void Classification_recognises_only_the_exact_approved_optional_read_scope_without_granting_it(string scope)
    { Assert.Equal(TokenCapability.ReadOnlyVerified, TokenCapabilityClassifier.Classify(["User.Read", scope]).Classification); }
    [Fact]
    public void Combined_intent_requires_all_actually_returned_scopes_including_prefixed_forms()
    {
        Assert.False(OptionalReportAccessPolicy.IsSatisfied((OptionalReportAccess)7, ["Reports.Read.All", "ServiceHealth.Read.All"]));
        Assert.True(OptionalReportAccessPolicy.IsSatisfied((OptionalReportAccess)7, ["Reports.Read.All", "https://graph.microsoft.com/ServiceHealth.Read.All", "DelegatedAdminRelationship.Read.All"]));
    }
    [Theory][InlineData(8)][InlineData(99)][InlineData(-1)]
    public void Unknown_purpose_bits_refuse_before_any_authentication_or_registration(int bits)
    { Assert.Throws<ConfigurationException>(() => OptionalReportAccessPolicy.Scopes((OptionalReportAccess)bits).ToArray()); }
}
