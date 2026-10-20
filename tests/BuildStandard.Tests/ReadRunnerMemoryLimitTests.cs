using System.Diagnostics;
using BuildStandard.Core;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ReadRunnerMemoryLimitTests
{
    [Theory] [InlineData(0,false)] [InlineData(1073741824,false)] [InlineData(1073741825,true)]
    public void Exact_limit_is_fixed_and_sample_is_not_a_hard_allocation_cap(long bytes,bool expected)
    {using var process=Process.GetCurrentProcess();var calls=0;var limit=new ReadRunnerMemoryLimit(p=>{Assert.Same(process,p);calls++;return bytes;});Assert.Equal(1073741824,ReadRunnerMemoryLimit.MaximumPrivateBytes);Assert.Equal(expected,limit.Check(process));Assert.Equal(expected,limit.Exceeded);Assert.Equal(1,calls);}
    [Fact] public void Once_exceeded_the_run_cannot_become_healthy_when_a_later_sample_falls()
    {using var process=Process.GetCurrentProcess();var bytes=ReadRunnerMemoryLimit.MaximumPrivateBytes+1;var calls=0;var limit=new ReadRunnerMemoryLimit(_=>{calls++;return bytes;});Assert.True(limit.Check(process));bytes=0;Assert.True(limit.Check(process));Assert.True(limit.Exceeded);Assert.Equal(1,calls);}
    [Fact] public void Missing_measurement_is_not_a_known_zero_or_success()
    {using var process=Process.GetCurrentProcess();var limit=new ReadRunnerMemoryLimit(_=>-1);Assert.ThrowsAny<ToolkitException>(()=>limit.Check(process));}
}
