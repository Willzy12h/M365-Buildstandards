using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Graph;
using BuildStandard.Graph.Setup;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OptionalUsageAccessTests
{
    [Theory][InlineData(SessionMode.Assessment)][InlineData(SessionMode.Deployment)]
    public void Deliberate_optional_access_adds_only_the_approved_read_without_changing_the_standard(SessionMode mode)
    {
        using var root=new TempRoot();using var http=new HttpClient();var standard=TestData.Standard();var original=ToolkitJson.Serialize(standard);
        var service=new TenantConnectionService(new ToolkitSettings(),root.Paths,http,NullLog.Instance,"test");
        var normal=service.ScopesFor(mode,standard);var requested=service.ScopesFor(mode,standard,OptionalReportAccess.UsageReports);
        Assert.DoesNotContain("Reports.Read.All",normal);Assert.Equal(new[]{"Reports.Read.All"},requested.Except(normal));
        Assert.Equal(requested,ApplicationSetupService.RequiredScopes(standard,mode,OptionalReportAccess.UsageReports));
        Assert.Equal(normal,ApplicationSetupService.RequiredScopes(standard,mode));Assert.Equal(original,ToolkitJson.Serialize(standard));
    }
    [Fact] public void An_unrecognised_access_intent_is_refused_before_any_authentication_or_setup()
    {
        using var root=new TempRoot();using var http=new HttpClient();var service=new TenantConnectionService(new ToolkitSettings(),root.Paths,http,NullLog.Instance,"test");
        Assert.Throws<ConfigurationException>(()=>service.ScopesFor(SessionMode.Assessment,TestData.Standard(),(OptionalReportAccess)99));
        Assert.Throws<ConfigurationException>(()=>ApplicationSetupService.RequiredScopes(TestData.Standard(),SessionMode.Assessment,(OptionalReportAccess)99));
    }
}
