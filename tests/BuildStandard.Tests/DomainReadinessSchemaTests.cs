using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Exchange;
using Xunit;

namespace BuildStandard.Tests;

public sealed class DomainReadinessSchemaTests
{
    internal static DomainReadinessReport Valid()
    {
        var r=new DomainReadinessReport{Id=Guid.NewGuid().ToString(),RunId=Guid.NewGuid().ToString(),Domain="example.invalid",
            StartedAt="2026-10-18T12:00:00.000Z",CompletedAt="2026-10-18T12:00:01.000Z",ToolkitVersion="1.1.0-preview.19",
            RequestedChecks=[DomainReadinessCheck.Mx],Status=DomainReadinessState.Complete,
            Observations=[new("example.invalid",DomainDnsType.MX,DomainDnsState.Observed,"2026-10-18T12:00:00.000Z","2026-10-18T12:00:01.000Z",DomainDnsResponse.Resolver,"1",
                [new("example.invalid",DomainDnsType.MX,"Answer",null,null,"gateway.invalid",10,null)])],
            Results=[new(DomainReadinessCheck.Mx,DomainReadinessMeaning.Observed,"Observed; expected gateway not configured.",[],"Ask the DNS owner.")]};
        r.IntegrityDigest=EvidenceIntegrity.Compute(r);return r;
    }
    private static byte[] Bytes(DomainReadinessReport r)=>Encoding.UTF8.GetBytes(ToolkitJson.Serialize(r));
    [Fact] public void A_valid_bounded_adapter_response_is_not_rejected_just_for_report_indentation()
    {
        var r=Valid();r.RequestedChecks=[DomainReadinessCheck.Spf];
        r.Observations[0]=r.Observations[0] with{Type=DomainDnsType.TXT,Answers=[new("example.invalid",DomainDnsType.TXT,"Answer",null,Enumerable.Repeat("x",6000).ToArray())]};
        r.Results[0]=r.Results[0] with{Check=DomainReadinessCheck.Spf};r.IntegrityDigest=EvidenceIntegrity.Compute(r);
        var compact=System.Text.Json.JsonSerializer.Serialize(r.Observations[0],ToolkitJson.Compact);
        Assert.Equal(DomainDnsState.Observed,DomainDnsResponse.Parse(compact,new("example.invalid",DomainDnsType.TXT)).State);
        Assert.Equal(r.IntegrityDigest,DomainReadinessSchema.Read(Bytes(r)).IntegrityDigest);
    }
    [Fact] public void Separate_schema_roundtrips_unknown_ttl_and_optional_absent_tenant_binding()
    {
        var original=Valid();var read=DomainReadinessSchema.Read(Bytes(original));
        Assert.Null(read.TenantId);Assert.Null(read.AccountObjectId);Assert.Null(read.Observations[0].Answers[0].Ttl);Assert.Equal(original.IntegrityDigest,read.IntegrityDigest);
    }
    [Fact] public void Claimed_tenant_binding_must_match_a_requested_client_without_inventing_one()
    {
        var r=Valid();Assert.Throws<TenantMismatchException>(()=>DomainReadinessSchema.Read(Bytes(r),TestData.TenantA));
        r.TenantId=TestData.TenantA;r.IntegrityDigest=EvidenceIntegrity.Compute(r);
        Assert.Equal(TestData.TenantA,DomainReadinessSchema.Read(Bytes(r),TestData.TenantA).TenantId);
        Assert.Throws<TenantMismatchException>(()=>DomainReadinessSchema.Read(Bytes(r),TestData.TenantB));
    }
    [Theory]
    [InlineData("kind")][InlineData("version")][InlineData("blank-id")][InlineData("wrong-domain")][InlineData("failed-complete")]
    [InlineData("no-observations")][InlineData("missing-result")][InlineData("missing-expected-source")][InlineData("duplicate-question")]
    [InlineData("unrelated-question")][InlineData("account-without-tenant")][InlineData("bad-time")][InlineData("future-observation")]
    [InlineData("unrequested-result")][InlineData("unrequested-question")][InlineData("foreign-reference")]
    public void Resealed_inconsistent_reports_are_refused(string defect)
    {
        var r=Valid();
        switch(defect)
        {
            case "kind":r.Kind="tenantSnapshot";break;case "version":r.SchemaVersion=2;break;case "blank-id":r.Id="";break;
            case "wrong-domain":r.Domain="https://example.invalid";break;
            case "failed-complete":r.Observations[0]=r.Observations[0] with{State=DomainDnsState.Failed,Reason="Unknown"};break;
            case "no-observations":r.Observations.Clear();break;
            case "missing-result":r.Results.Clear();break;
            case "missing-expected-source":r.ExpectedMx=["gateway.invalid"];break;
            case "duplicate-question":r.Observations.Add(r.Observations[0]);break;
            case "unrelated-question":r.Observations[0]=r.Observations[0] with{Name="unrelated.invalid"};break;
            case "account-without-tenant":r.AccountObjectId=TestData.Operator;break;
            case "bad-time":r.CompletedAt="yesterday";break;
            case "future-observation":r.Observations[0]=r.Observations[0] with{CompletedAt="2026-10-18T12:00:02.000Z"};break;
            case "unrequested-result":r.Results.Add(new(DomainReadinessCheck.Spf,DomainReadinessMeaning.Observed,"Unrequested",[],"Unknown"));break;
            case "unrequested-question":r.Observations.Add(new("example.invalid",DomainDnsType.TXT,DomainDnsState.NoData,r.StartedAt,r.CompletedAt,DomainDnsResponse.Resolver,"1",[],9501));break;
            case "foreign-reference":r.References.Add(new("unrelated.invalid","include:other.invalid","other.invalid",1,r.StartedAt,"Reference"));break;
        }
        r.IntegrityDigest=EvidenceIntegrity.Compute(r);Assert.Throws<ConfigurationException>(()=>DomainReadinessSchema.Read(Bytes(r)));
    }
    [Theory] [InlineData("extra")][InlineData("nested-extra")][InlineData("status-number")][InlineData("version-string")]
    [InlineData("null-list")][InlineData("null-row")][InlineData("case-state")][InlineData("missing-field")]
    [InlineData("case-check")][InlineData("case-meaning")]
    public void Unknown_fields_nulls_and_coerced_types_do_not_become_the_original_valid_report(string defect)
    {
        var json=ToolkitJson.ParseObject(ToolkitJson.Serialize(Valid()));
        switch(defect)
        {
            case "extra":json["unknown"]=true;break;case "nested-extra":json["observations"]![0]!["unknown"]=true;break;
            case "status-number":json["status"]=0;break;case "version-string":json["schemaVersion"]="1";break;
            case "null-list":json["observations"]=null;break;case "null-row":json["observations"]![0]=null;break;
            case "case-state":json["status"]="complete";break;case "missing-field":json.Remove("requestedChecks");break;
            case "case-check":json["requestedChecks"]![0]="mx";break;case "case-meaning":json["results"]![0]!["meaning"]="observed";break;
        }
        Assert.Throws<ConfigurationException>(()=>DomainReadinessSchema.Read(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
    private sealed class PolicyResolver(Func<string,string> policy):IDomainReadinessResolver
    {
        public Task<DomainDnsObservation> QueryAsync(DomainDnsQuestion question,CancellationToken ct)=>Task.FromResult(
            new DomainDnsObservation(question.Name,question.Type,DomainDnsState.Observed,"2026-10-18T12:00:00.000Z","2026-10-18T12:00:00.000Z",
                DomainDnsResponse.Resolver,"1",[new(question.Name,DomainDnsType.TXT,"Answer",null,[policy(question.Name)])]));
    }
    [Theory] [InlineData("missing-read-and-edge")][InlineData("loop-as-reference")][InlineData("wrong-depth")]
    [InlineData("missing-repeated-edge")][InlineData("extra-edge")][InlineData("context-as-complete")][InlineData("budget-as-complete")]
    public async Task Complete_spf_coverage_is_proved_from_original_policies_not_supplied_edges(string defect)
    {
        var policy=defect switch
        {
            "loop-as-reference"=>"v=spf1 include:example.invalid -all",
            "missing-repeated-edge"=>"v=spf1 include:child.invalid include:child.invalid -all",
            "context-as-complete"=>"v=spf1 include:%{d}.invalid -all",
            "budget-as-complete"=>"v=spf1 "+string.Join(' ',Enumerable.Repeat("include:child.invalid",11))+" -all",
            _=>"v=spf1 include:child.invalid -all"
        };
        var clock=new FixedClock{UtcNow=new(2026,10,18,12,0,0,TimeSpan.Zero)};
        var collector=new DomainReadinessCollector(new PolicyResolver(n=>n=="example.invalid"?policy:"v=spf1 -all"),clock);
        var report=await collector.CollectAsync(new("example.invalid",[DomainReadinessCheck.Spf]),default);
        Assert.Equal(report.Status,DomainReadinessSchema.Read(Bytes(report)).Status); // Original complete and partial reports remain valid.
        switch(defect)
        {
            case "missing-read-and-edge":report.Observations.RemoveAt(1);report.References.Clear();break;
            case "loop-as-reference":report.References[0]=report.References[0] with{State="Reference"};break;
            case "wrong-depth":report.References[0]=report.References[0] with{Depth=2};break;
            case "missing-repeated-edge":report.References.RemoveAt(1);break;
            case "extra-edge":report.References.Add(report.References[0]);break;
        }
        report.Status=DomainReadinessState.Complete;report.IntegrityDigest=EvidenceIntegrity.Compute(report);
        Assert.Throws<ConfigurationException>(()=>DomainReadinessSchema.Read(Bytes(report)));
    }
    [Fact] public void Integrity_duplicate_fields_encoding_and_size_are_checked_before_admission()
    {
        var r=Valid();r.Results[0]=r.Results[0] with{Summary="Altered"};Assert.Throws<ConfigurationException>(()=>DomainReadinessSchema.Read(Bytes(r)));
        var text=ToolkitJson.Serialize(Valid()).Replace("\"schemaVersion\": 1","\"schemaVersion\": 2,\"schemaVersion\": 1",StringComparison.Ordinal);
        Assert.Throws<ConfigurationException>(()=>DomainReadinessSchema.Read(Encoding.UTF8.GetBytes(text)));
        Assert.Throws<ConfigurationException>(()=>DomainReadinessSchema.Read([0xff,0xfe]));
        Assert.Throws<ConfigurationException>(()=>DomainReadinessSchema.Read(new byte[DomainReadinessSchema.MaximumBytes+1]));
    }
}
