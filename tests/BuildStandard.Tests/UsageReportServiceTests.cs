using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageReportServiceTests
{
    [Theory][InlineData("success")][InlineData("second-failure")][InlineData("second-cancel")][InlineData("malformed")]
    public async Task Selected_sources_keep_independent_reads_originals_and_unknown_privacy(string scenario)
    {
        using var cancellation=new CancellationTokenSource();var clock=new FixedClock{UtcNow=new(2026,10,20,12,0,0,TimeSpan.Zero)};var calls=0;
        var source=new Source((id,_)=>
        {
            calls++;if(calls==2&&scenario=="second-failure")throw new ToolkitException("Synthetic source failure.");
            if(calls==2&&scenario=="second-cancel"){cancellation.Cancel();throw new OperationCanceledException(cancellation.Token);}
            return Task.FromResult(scenario=="malformed"?Encoding.UTF8.GetBytes("not,the,required,headers"):Header(id));
        });
        var report=await new UsageReportService(clock,"0.0.0-synthetic").CaptureAsync(source,UsageReportTransportTests.Session(),["email-activity","teams-activity","onedrive-activity"],"D30",()=>{},cancellation.Token);
        var admitted=UsageReviewSchema.Read(UsageReviewSchema.Serialise(report),TestData.TenantA);
        Assert.Equal("live",admitted.SourceMode);Assert.Equal(TestData.Operator,admitted.AccountObjectId);Assert.All(admitted.Sources,item=>Assert.Equal("Unknown",item.PrivacyProvenance));
        if(scenario=="success"){Assert.Equal("Collected",admitted.Status);Assert.Equal(3,calls);Assert.All(admitted.Sources,item=>Assert.NotNull(item.OriginalCsvBase64));Assert.All(UsageReviewSchema.Project(admitted),item=>Assert.Empty(item.Rows));}
        if(scenario=="second-failure"){Assert.Equal("Partial",admitted.Status);Assert.Equal(new[]{"Collected","Failed","Collected"},admitted.Sources.Select(item=>item.Status));Assert.Null(admitted.Sources[1].OriginalCsvBase64);}
        if(scenario=="second-cancel"){Assert.Equal(new[]{"Collected","Cancelled","NotAttempted"},admitted.Sources.Select(item=>item.Status));Assert.Equal(2,calls);Assert.NotNull(admitted.Sources[0].OriginalCsvBase64);Assert.Null(admitted.Sources[1].OriginalCsvBase64);}
        if(scenario=="malformed"){Assert.Equal("Failed",admitted.Status);Assert.All(admitted.Sources,item=>{Assert.Equal("Failed",item.Status);Assert.Null(item.OriginalCsvBase64);});}
    }
    [Theory][InlineData("wrong-tenant")][InlineData("duplicate")][InlineData("unknown")][InlineData("period")][InlineData("context-after-read")]
    public async Task Wrong_selection_or_changed_context_never_becomes_current_evidence(string fault)
    {
        var current=true;var calls=0;var source=new Source((id,_)=>{calls++;if(fault=="context-after-read")current=false;return Task.FromResult(Header(id));});
        var session=UsageReportTransportTests.Session();if(fault=="wrong-tenant")session.TenantId=TestData.TenantB;
        string[] selected=fault=="duplicate"?["email-activity","email-activity"]:fault=="unknown"?["arbitrary"]:["email-activity"];
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>new UsageReportService(new SystemClock(),"synthetic").CaptureAsync(source,session,selected,fault=="period"?"D1":"D30",()=>{if(!current)throw new TenantMismatchException("Synthetic context changed.");},default));
        Assert.Equal(fault=="context-after-read"?1:0,calls);
    }
    internal static byte[] Header(string id)=>Encoding.UTF8.GetBytes(string.Join(',',UsageReportRegistry.Find(id).Columns)+"\r\n");
    private sealed class Source(Func<string,CancellationToken,Task<byte[]>> read):IUsageReportSource
    {public string TenantId=>TestData.TenantA;public Task<byte[]> DownloadAsync(string id,string period,CancellationToken ct)=>read(id,ct);}
}
