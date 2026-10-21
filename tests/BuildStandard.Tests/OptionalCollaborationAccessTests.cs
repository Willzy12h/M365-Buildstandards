using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Graph;
using BuildStandard.Graph.Setup;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OptionalCollaborationAccessTests
{
    [Theory][InlineData(8,SessionMode.Assessment)][InlineData(8,SessionMode.Deployment)]
    [InlineData(16,SessionMode.Assessment)][InlineData(16,SessionMode.Deployment)]
    [InlineData(24,SessionMode.Assessment)][InlineData(24,SessionMode.Deployment)]
    public void Selected_metadata_and_direct_member_purposes_reuse_existing_setup_without_changing_standard(int bits,SessionMode mode)
    {
        using var root=new TempRoot();using var http=new HttpClient();var standard=TestData.Standard();var original=ToolkitJson.Serialize(standard);
        var service=new TenantConnectionService(new ToolkitSettings(),root.Paths,http,NullLog.Instance,"synthetic");
        var normal=service.ScopesFor(mode,standard);var selected=service.ScopesFor(mode,standard,(OptionalReportAccess)bits);
        var expected=new List<string>();if((bits&8)!=0)expected.Add("Sites.Read.All");if((bits&16)!=0)expected.Add("TeamMember.Read.All");
        Assert.Equal(expected,selected.Except(normal));Assert.Equal(selected,ApplicationSetupService.RequiredScopes(standard,mode,(OptionalReportAccess)bits));
        Assert.DoesNotContain("Sites.Read.All",normal);Assert.DoesNotContain("TeamMember.Read.All",normal);
        Assert.DoesNotContain("Sites.FullControl.All",selected);Assert.DoesNotContain("Directory.Read.All",selected.Except(normal));Assert.Equal(original,ToolkitJson.Serialize(standard));
    }
    [Theory][InlineData("Sites.Read.All")][InlineData("TeamMember.Read.All")]
    [InlineData("https://graph.microsoft.com/Sites.Read.All")][InlineData("https://graph.microsoft.com/TeamMember.Read.All")]
    public void Approved_read_scope_is_classified_from_actual_token_without_requesting_access(string scope)
    {Assert.Equal(TokenCapability.ReadOnlyVerified,TokenCapabilityClassifier.Classify(["User.Read",scope]).Classification);}
    [Fact]public void Reuse_requires_every_selected_scope_and_preserves_already_configured_optional_scopes()
    {
        Assert.False(OptionalReportAccessPolicy.IsSatisfied((OptionalReportAccess)24,["Sites.Read.All"]));
        Assert.True(OptionalReportAccessPolicy.IsSatisfied((OptionalReportAccess)24,["Sites.Read.All","https://graph.microsoft.com/TeamMember.Read.All"]));
        Assert.Contains("Sites.Read.All",OptionalReportAccessPolicy.PreservedOptionalScopes);Assert.Contains("TeamMember.Read.All",OptionalReportAccessPolicy.PreservedOptionalScopes);
    }
}
