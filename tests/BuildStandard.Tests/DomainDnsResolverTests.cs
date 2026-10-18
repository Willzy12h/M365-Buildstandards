using System.Diagnostics;
using System.Text;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;

namespace BuildStandard.Tests;

public sealed class DomainDnsResolverTests
{
    [Theory]
    [InlineData(OwnedProcessEnd.Cancelled,DomainDnsState.Cancelled)]
    [InlineData(OwnedProcessEnd.TimedOut,DomainDnsState.Failed)]
    [InlineData(OwnedProcessEnd.LimitExceeded,DomainDnsState.Limited)]
    [InlineData(OwnedProcessEnd.NotStarted,DomainDnsState.Failed)]
    [InlineData(OwnedProcessEnd.OutputHeldOpen,DomainDnsState.Failed)]
    public async Task Process_failure_is_never_an_empty_success(OwnedProcessEnd end,DomainDnsState expected)
    {
        var calls=0;var resolver=new WindowsDomainReadinessResolver((_,_)=>{calls++;return Task.FromResult(new DomainDnsHelperResult(new(end,null,false),""));});
        var result=await resolver.QueryAsync(new("example.invalid",DomainDnsType.TXT),default);
        Assert.Equal(expected,result.State);Assert.Equal(1,calls);Assert.NotEmpty(result.Reason!);Assert.Empty(result.Answers);
    }
    [Fact] public async Task Failed_termination_blocks_any_replacement_or_later_query_on_this_resolver()
    {
        var calls=0;var resolver=new WindowsDomainReadinessResolver((_,_)=>{calls++;return Task.FromResult(new DomainDnsHelperResult(new(OwnedProcessEnd.NotStopped,null,false),""));});
        var first=await resolver.QueryAsync(new("example.invalid",DomainDnsType.TXT),default);
        var second=await resolver.QueryAsync(new("other.invalid",DomainDnsType.MX),default);
        Assert.Equal(1,calls);Assert.Equal(DomainDnsState.Failed,first.State);Assert.Equal(DomainDnsState.Failed,second.State);
        Assert.Contains("may still be running",first.Reason!,StringComparison.OrdinalIgnoreCase);Assert.Contains("not started",second.Reason!,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task Pre_cancelled_question_starts_no_helper()
    {
        var calls=0;var resolver=new WindowsDomainReadinessResolver((_,_)=>{calls++;throw new InvalidOperationException();});
        using var stop=new CancellationTokenSource();stop.Cancel();
        var result=await resolver.QueryAsync(new("example.invalid",DomainDnsType.TXT),stop.Token);
        Assert.Equal(DomainDnsState.Cancelled,result.State);Assert.Equal(0,calls);
    }
    [Theory] [InlineData("")][InlineData("null")][InlineData("{}")][InlineData("unreadable")]
    public async Task Exit_zero_with_unreadable_output_is_failed_not_absence(string output)
    {
        var resolver=new WindowsDomainReadinessResolver((_,_)=>Task.FromResult(new DomainDnsHelperResult(new(OwnedProcessEnd.Exited,0,false),output)));
        Assert.Equal(DomainDnsState.Failed,(await resolver.QueryAsync(new("example.invalid",DomainDnsType.TXT),default)).State);
    }
    [Fact] public async Task A_concurrent_question_is_refused_without_starting_another_helper()
    {
        var held=new TaskCompletionSource<DomainDnsHelperResult>(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
        var resolver=new WindowsDomainReadinessResolver((_,_)=>{calls++;return held.Task;});
        var first=resolver.QueryAsync(new("example.invalid",DomainDnsType.TXT),default);
        var second=await resolver.QueryAsync(new("other.invalid",DomainDnsType.MX),default);
        Assert.Equal(1,calls);Assert.Equal(DomainDnsState.Failed,second.State);Assert.Contains("not started",second.Reason!);
        held.SetResult(new(new(OwnedProcessEnd.Exited,1,false),""));Assert.Equal(DomainDnsState.Failed,(await first).State);
    }
    [Theory,MemberData(nameof(MailboxSettingsRunnerTests.Runtimes),MemberType=typeof(MailboxSettingsRunnerTests))]
    public async Task Fixed_script_runs_against_in_memory_stub_and_preserves_chunks_negative_codes_and_unknowns(string runtime,string path)
    {
        _=runtime;
        foreach(var scenario in new[]{"txt","null","nxdomain","nodata","unrelated"})
        {
            var question=new DomainDnsQuestion("_spf.example.invalid",DomainDnsType.TXT);
            var stub="""
                $ErrorActionPreference='Stop'
                New-Module -Name DnsClient -ScriptBlock {
                    function Resolve-DnsName {
                        param($Name,$Type,[switch]$DnsOnly,[switch]$NoHostsFile,[switch]$QuickTimeout)
                        if ($Name -cne '_spf.example.invalid' -or $Type -cne 'TXT' -or -not $DnsOnly -or -not $NoHostsFile) { throw 'Unexpected synthetic query' }
                """ + (scenario switch {
                    "txt"=>"[pscustomobject]@{Name=$Name;Type='TXT';Section='Answer';TTL=300;Strings=@('v=spf1 ','-all')}",
                    "null"=>"[pscustomobject]@{Name=$Name;Type='TXT';Section='Answer';Strings=$null}",
                    "unrelated"=>"[pscustomobject]@{Name='unrelated.invalid';Type='TXT';Section='Answer';Strings=@('v=spf1 -all')}",
                    "nxdomain"=>"throw [ComponentModel.Win32Exception]::new(9003)",
                    _=>"throw [ComponentModel.Win32Exception]::new(9501)"
                }) + "\n}; Export-ModuleMember -Function Resolve-DnsName } | Import-Module -Force\n"
                + "if ((Get-Command DnsClient\\Resolve-DnsName).CommandType -ne 'Function') { throw 'Synthetic stub not installed' }\n";
            var result=await WindowsDomainReadinessResolver.RunHelperAsync(Start(path,stub+WindowsDomainReadinessResolver.QueryScript(question)),TimeSpan.FromSeconds(20),default);
            Assert.Equal(OwnedProcessEnd.Exited,result.Process.End);Assert.Equal(0,result.Process.ExitCode);
            var resolver=new WindowsDomainReadinessResolver((_,_)=>Task.FromResult(result));var observation=await resolver.QueryAsync(question,default);
            Assert.Equal(scenario switch { "txt"=>DomainDnsState.Observed,"nxdomain"=>DomainDnsState.NameError,"nodata"=>DomainDnsState.NoData,_=>DomainDnsState.Failed },observation.State);
            if(scenario=="txt")Assert.Equal("v=spf1 -all",Assert.Single(observation.Answers).Text);
            if(scenario is "nxdomain" or "nodata")Assert.Null(observation.NegativeOwner);
        }
    }
    [Theory,MemberData(nameof(MailboxSettingsRunnerTests.Runtimes),MemberType=typeof(MailboxSettingsRunnerTests))]
    public async Task Bounded_owned_pipes_enforce_both_streams_and_timeout(string runtime,string path)
    {
        _=runtime;
        foreach(var script in new[]{"[Console]::Out.Write(('x'*70000)); Start-Sleep -Seconds 10","[Console]::Error.Write(('x'*70000)); Start-Sleep -Seconds 10"})
        {
            var result=await WindowsDomainReadinessResolver.RunHelperAsync(Start(path,script),TimeSpan.FromSeconds(20),default);
            Assert.Equal(OwnedProcessEnd.LimitExceeded,result.Process.End);Assert.True(result.Output.Length<=65536);
        }
        var timed=await WindowsDomainReadinessResolver.RunHelperAsync(Start(path,"Start-Sleep -Seconds 10"),TimeSpan.FromMilliseconds(300),default);
        Assert.Equal(OwnedProcessEnd.TimedOut,timed.Process.End);
    }
    private static ProcessStartInfo Start(string path,string script)
    {
        var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(arg);
        return start;
    }
}
