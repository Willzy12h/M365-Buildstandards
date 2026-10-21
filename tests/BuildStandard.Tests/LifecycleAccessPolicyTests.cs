using System.Reflection;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Graph;
using BuildStandard.Graph.Setup;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleAccessPolicyTests
{
    private static string[] Scopes(SessionMode mode,string? action)
    {
        var type=typeof(LifecycleActionRegistry).Assembly.GetType("BuildStandard.Core.Safety.LifecycleAccessPolicy");Assert.NotNull(type);
        var method=type.GetMethod("Scopes");Assert.NotNull(method);
        try{return ((IEnumerable<string>)method.Invoke(null,new object?[]{mode,action})!).ToArray();}
        catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    public static IEnumerable<object[]> Actions=>LifecycleActionRegistry.ActionIds.SelectMany(action=>new[]{new object[]{SessionMode.Assessment,action},new object[]{SessionMode.Deployment,action}});
    [Theory][MemberData(nameof(Actions))]
    public void Selected_purpose_matches_fixed_read_dependencies_and_only_its_one_write_scope(SessionMode mode,string action)
    {
        var operation=LifecycleActionRegistry.Create(action,TestData.Office,action.EndsWith("member",StringComparison.Ordinal)?TestData.Mam:null,action.EndsWith("license",StringComparison.Ordinal)?TestData.ClientId:null);
        var expected=LifecycleReadRegistry.For(operation).Select(source=>source.Scope).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(mode==SessionMode.Deployment)expected.Add(operation.WriteScope);
        var actual=Scopes(mode,action);Assert.Equal(expected.Order(),actual.Order());Assert.DoesNotContain("Directory.Read.All",actual);
        Assert.Equal(mode==SessionMode.Assessment?TokenCapability.ReadOnlyVerified:TokenCapability.BroaderPresent,TokenCapabilityClassifier.Classify(actual).Classification);
    }
    [Fact]public void Selection_is_default_off_and_arbitrary_access_purposes_are_refused()
    {Assert.Empty(Scopes(SessionMode.Assessment,null));Assert.Empty(Scopes(SessionMode.Deployment,""));Assert.ThrowsAny<ToolkitException>(()=>Scopes(SessionMode.Deployment,"Application.ReadWrite.All"));}
    [Theory][InlineData(SessionMode.Assessment)][InlineData(SessionMode.Deployment)]
    public void Connection_and_setup_use_the_same_exact_selected_scope_set_without_unrelated_policy_writes(SessionMode mode)
    {
        using var root=new TempRoot();using var http=new HttpClient();var standard=TestData.Standard();var before=ToolkitJson.Serialize(standard);
        var connection=new TenantConnectionService(new(),root.Paths,http,NullLog.Instance,"synthetic");
        var connect=typeof(TenantConnectionService).GetMethod("ScopesFor")!;var setup=typeof(ApplicationSetupService).GetMethod("RequiredScopes")!;
        Assert.Equal(4,connect.GetParameters().Length);Assert.Equal(4,setup.GetParameters().Length);
        var requested=((IEnumerable<string>)connect.Invoke(connection,new object[]{mode,standard,OptionalReportAccess.None,"disable-account"})!).ToArray();
        var registered=((IEnumerable<string>)setup.Invoke(null,new object[]{standard,mode,OptionalReportAccess.None,"disable-account"})!).ToArray();
        Assert.Equal(requested,registered);Assert.Empty(requested.Intersect(standard.WriteScopes(),StringComparer.OrdinalIgnoreCase));
        Assert.Equal(mode==SessionMode.Deployment,requested.Contains("User.EnableDisableAccount.All"));Assert.Equal(before,ToolkitJson.Serialize(standard));
    }
    [Theory][InlineData("GroupMember.ReadWrite.All")][InlineData("LicenseAssignment.ReadWrite.All")][InlineData("User.EnableDisableAccount.All")][InlineData("User.RevokeSessions.All")]
    public void Approved_returned_lifecycle_writes_are_broader_and_can_never_be_called_read_only(string scope)
    {var result=TokenCapabilityClassifier.Classify(new[]{"User.Read.All",scope});Assert.Equal(TokenCapability.BroaderPresent,result.Classification);Assert.Empty(result.UnclassifiedScopes);}
}
