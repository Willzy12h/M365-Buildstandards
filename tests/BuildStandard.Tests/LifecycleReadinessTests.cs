using System.Reflection;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Execution;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleReadinessTests
{
    private static async Task<object> Read(LifecyclePreparationTests.Fixture f)
    {
        var type=typeof(ReviewedChangeService).Assembly.GetType("BuildStandard.Engine.Execution.LifecycleReadinessService");Assert.NotNull(type);
        var service=Activator.CreateInstance(type,f.Evidence,f.Clock)!;var method=type.GetMethod("CaptureAsync");Assert.NotNull(method);
        var task=(Task)method.Invoke(service,new object[]{f.Source,f.Session,f.Profile,f.Source.Operation,(Action)(()=>{}),CancellationToken.None})!;
        await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    private static T Get<T>(object result,string name)=>(T)result.GetType().GetProperty(name)!.GetValue(result)!;
    [Fact]public async Task Target_readiness_can_be_checked_in_assessment_mode_without_requesting_write_access()
    {
        using var f=new LifecyclePreparationTests.Fixture();f.Session.Mode=SessionMode.Assessment;f.Source.Mode=SessionMode.Assessment;f.Session.Scopes.Remove("User.EnableDisableAccount.All");
        var result=await Read(f);Assert.True(Get<bool>(result,"Ready"));Assert.Contains("not approval",Get<string>(result,"Guidance"),StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4,f.Source.RequestCount);Assert.Empty(Directory.GetFiles(Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle"),"plan-*.json"));
    }
    [Fact]public async Task Incomplete_privilege_read_retains_explicit_source_failure_and_cannot_establish_readiness()
    {
        using var f=new LifecyclePreparationTests.Fixture();f.Source.Before=id=>{if(id=="eligibleRoles")throw new GraphRequestException(403,"GET","/synthetic",null,"never-persist-body");};
        var result=await Read(f);Assert.False(Get<bool>(result,"Ready"));Assert.Contains("incomplete",Get<string>(result,"Guidance"),StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle"),"before-*.json"));
    }
    [Fact]public async Task Protected_target_is_blocked_even_when_all_required_read_collections_are_complete()
    {using var f=new LifecyclePreparationTests.Fixture();f.Profile.Parameters.EmergencyAccountIds.Add(TestData.Office);var result=await Read(f);Assert.False(Get<bool>(result,"Ready"));Assert.Contains("protected",Get<string>(result,"Guidance"),StringComparison.OrdinalIgnoreCase);}
}
