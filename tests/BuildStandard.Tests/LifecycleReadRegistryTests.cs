using System.Collections;
using System.Reflection;
using BuildStandard.Core;
using BuildStandard.Core.Safety;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleReadRegistryTests
{
    private const string User = "11111111-1111-4111-8111-111111111111", Group = "22222222-2222-4222-8222-222222222222", Sku = "33333333-3333-4333-8333-333333333333";
    private static IReadOnlyList<object> Reads(LifecycleOperation operation)
    {
        var type = typeof(LifecycleActionRegistry).Assembly.GetType("BuildStandard.Core.Safety.LifecycleReadRegistry"); Assert.NotNull(type);
        try { return ((IEnumerable)type.GetMethod("For")!.Invoke(null,new object[] {operation})!).Cast<object>().ToArray(); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static string Text(object value,string property) => (string)value.GetType().GetProperty(property)!.GetValue(value)!;
    [Theory][InlineData("disable-account",4)][InlineData("revoke-sessions",4)][InlineData("assign-license",6)][InlineData("remove-member",8)]
    public void Read_dependencies_are_fixed_to_exact_action_target_and_required_authority_inventory(string action,int count)
    {
        var operation = LifecycleActionRegistry.Create(action,User,action.EndsWith("member",StringComparison.Ordinal) ? Group : null,action.EndsWith("license",StringComparison.Ordinal) ? Sku : null);
        var reads = Reads(operation); Assert.Equal(count,reads.Count);
        Assert.Equal(reads.Count,reads.Select(r=>Text(r,"Id")).Distinct().Count());
        Assert.All(reads,r=>Assert.StartsWith("/",Text(r,"Path")));
        Assert.Contains(reads,r=>Text(r,"Id")=="user" && Text(r,"Path").StartsWith("/users/"+User+"?",StringComparison.Ordinal));
        Assert.Contains(reads,r=>Text(r,"Id")=="eligibleRoles" && Uri.UnescapeDataString(Text(r,"Path")).Contains("principalId eq '"+User+"'",StringComparison.Ordinal));
        Assert.Contains(reads,r=>Text(r,"Id")=="roleAssignableGroups" && Uri.UnescapeDataString(Text(r,"Path")).Contains("isAssignableToRole eq true",StringComparison.Ordinal));
        Assert.All(reads,r=>Assert.DoesNotContain("Directory.Read.All",Text(r,"Scope")));
        if (action=="remove-member") Assert.Contains(reads,r=>Text(r,"Id")=="members" && Text(r,"Path").StartsWith("/groups/"+Group+"/members?",StringComparison.Ordinal));
        else Assert.DoesNotContain(reads,r=>Text(r,"Path").Contains("/groups/"+Group,StringComparison.Ordinal));
    }
    [Fact] public void Modified_operation_cannot_register_a_different_read_target()
    {
        var operation = LifecycleActionRegistry.Create("disable-account",User) with { UserId = Group };
        Assert.ThrowsAny<ToolkitException>(()=>Reads(operation));
    }
}
