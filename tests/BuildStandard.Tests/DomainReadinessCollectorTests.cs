using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
using Xunit;

namespace BuildStandard.Tests;

public sealed class DomainReadinessCollectorTests
{
    private readonly FixedClock _clock=new(){UtcNow=new(2026,10,18,12,0,0,TimeSpan.Zero)};
    private sealed class Resolver(Func<DomainDnsQuestion,DomainDnsObservation> read):IDomainReadinessResolver
    {
        public List<DomainDnsQuestion> Questions { get; }=[];
        public Task<DomainDnsObservation> QueryAsync(DomainDnsQuestion question,CancellationToken ct){Questions.Add(question);return Task.FromResult(read(question));}
    }
    private DomainDnsObservation Answer(DomainDnsQuestion question,params DomainDnsAnswer[] answers)=>new(question.Name,question.Type,DomainDnsState.Observed,
        Timestamps.Format(_clock.UtcNow),Timestamps.Format(_clock.UtcNow),DomainDnsResponse.Resolver,DomainDnsResponse.AdapterVersion,answers,null,null,null);
    private DomainDnsObservation Txt(DomainDnsQuestion question,params string[] records)=>Answer(question,records.Select(text=>new DomainDnsAnswer(question.Name,DomainDnsType.TXT,"Answer",null,new[]{text},null,null,null)).ToArray());
    private DomainReadinessRequest Request(params DomainReadinessCheck[] checks)=>new("example.invalid",checks);
    [Fact] public async Task Oversized_constructed_enrolment_name_refuses_before_any_other_selected_read()
    {
        var domain=string.Join('.',new string('a',63),new string('b',63),new string('c',63),new string('d',61));
        var resolver=new Resolver(q=>Answer(q,new DomainDnsAnswer(q.Name,DomainDnsType.MX,"Answer",null,null,"gateway.invalid",10,null)));
        await Assert.ThrowsAsync<ConfigurationException>(()=>new DomainReadinessCollector(resolver,_clock).CollectAsync(
            new(domain,new[]{DomainReadinessCheck.Mx,DomainReadinessCheck.WindowsEnrolment}),default));
        Assert.Empty(resolver.Questions);
    }
    [Theory] [InlineData("v=spf1 -all")][InlineData("v=spf1 include:example.invalid -all")]
    [InlineData("v=spf1 include:%{d}.invalid -all")][InlineData("v=spf1 include:child.invalid -all")]
    public async Task Collected_complete_and_incomplete_reports_roundtrip_the_same_strict_schema(string policy)
    {
        var resolver=new Resolver(q=>Txt(q,q.Name=="example.invalid"?policy:"v=spf1 -all"));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf),default);
        var parsed=DomainReadinessSchema.Read(System.Text.Encoding.UTF8.GetBytes(ToolkitJson.Serialize(report)));
        Assert.Equal(report.IntegrityDigest,parsed.IntegrityDigest);Assert.Equal(report.Status,parsed.Status);
    }
    [Fact] public async Task Null_mx_and_mixed_null_mx_are_explicit_observations_not_failed_reads()
    {
        foreach(var mixed in new[]{false,true})
        {
            var resolver=new Resolver(q=>Answer(q,mixed
                ? [new(q.Name,DomainDnsType.MX,"Answer",null,null,".",0,null),new(q.Name,DomainDnsType.MX,"Answer",null,null,"gateway.invalid",10,null)]
                : [new(q.Name,DomainDnsType.MX,"Answer",null,null,".",0,null)]));
            var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Mx),default);
            Assert.Equal(DomainReadinessState.Complete,report.Status);
            Assert.Equal(mixed?DomainReadinessMeaning.ReviewFinding:DomainReadinessMeaning.NotConfigured,Assert.Single(report.Results).Meaning);
        }
    }
    [Fact] public async Task The_answer_budget_preserves_prior_reads_without_claiming_a_complete_smaller_inventory()
    {
        var sequence=0;var resolver=new Resolver(q=>Txt(q,Enumerable.Repeat("unrelated verification text",99)
            .Append($"v=spf1 include:next{++sequence}.invalid -all").ToArray()));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf),default);
        Assert.Equal(DomainReadinessState.Partial,report.Status);Assert.NotEmpty(report.Observations);
        Assert.True(report.Observations.Sum(o=>1+o.Answers.Count)+report.References.Count<=500);
        Assert.Contains(report.Limitations,l=>l.Contains("500",StringComparison.Ordinal));
    }
    [Fact] public async Task Unknown_gateway_never_becomes_microsoft_compliance_or_tenant_binding()
    {
        var resolver=new Resolver(q=>Answer(q,new DomainDnsAnswer(q.Name,DomainDnsType.MX,"Answer",300,null,"thirdparty.invalid",10,null)));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Mx),default);
        Assert.Single(resolver.Questions);Assert.Equal(DomainReadinessState.Complete,report.Status);Assert.Null(report.TenantId);Assert.Null(report.AccountObjectId);
        Assert.Equal(DomainReadinessMeaning.Observed,Assert.Single(report.Results).Meaning);Assert.Contains("not configured",report.Results[0].Summary,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task Explicit_expected_gateway_is_compared_without_assuming_microsoft_mx()
    {
        var resolver=new Resolver(q=>Answer(q,new DomainDnsAnswer(q.Name,DomainDnsType.MX,"Answer",300,null,"THIRDPARTY.invalid.",10,null)));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Mx) with{ExpectedMx=new[]{"thirdparty.invalid"},ExpectedMxSource="Engineer-selected gateway"},default);
        Assert.Equal(DomainReadinessMeaning.ObservedMatch,Assert.Single(report.Results).Meaning);
        Assert.Equal("Engineer-selected gateway",report.ExpectedMxSource);
    }
    [Fact] public async Task Native_nxdomain_with_unknown_negative_owner_does_not_claim_the_domain_is_absent()
    {
        var resolver=new Resolver(q=>new(q.Name,q.Type,DomainDnsState.NameError,Timestamps.Format(_clock.UtcNow),Timestamps.Format(_clock.UtcNow),DomainDnsResponse.Resolver,"1",[],9003,null,"Negative owner unknown"));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Mx),default);
        Assert.Equal(DomainReadinessMeaning.UnableToCheck,Assert.Single(report.Results).Meaning);Assert.Contains("unknown",report.Results[0].Summary,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task Repeated_includes_share_one_question_but_retain_both_edges_and_original_terms()
    {
        var resolver=new Resolver(q=>Txt(q,q.Name=="example.invalid"?"v=spf1 include:_spf.example.invalid include:_spf.example.invalid -all":"v=spf1 ip4:192.0.2.0/24 -all"));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf),default);
        Assert.Equal(2,resolver.Questions.Count);Assert.Equal(2,report.References.Count);Assert.Equal(DomainReadinessState.Complete,report.Status);
        Assert.DoesNotContain(report.Results,r=>r.Summary.Contains("sender passed",StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public async Task Multiple_txt_policies_are_not_concatenated_or_followed()
    {
        var resolver=new Resolver(q=>Txt(q,"v=spf1 include:a.invalid -all","v=spf1 include:b.invalid -all"));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf),default);
        Assert.Single(resolver.Questions);Assert.Empty(report.References);Assert.Equal(DomainReadinessMeaning.ReviewFinding,Assert.Single(report.Results).Meaning);
    }
    [Theory] [InlineData("v=spf1 redirect=ignored.invalid -all include:ignored.invalid")]
    [InlineData("v=spf1 include:%{d}.invalid -all")]
    public async Task Ignored_and_macro_references_never_make_network_questions(string policy)
    {
        var resolver=new Resolver(q=>Txt(q,policy));var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf),default);
        Assert.Single(resolver.Questions);Assert.Empty(report.References);
        if(policy.Contains('%'))Assert.Equal(DomainReadinessMeaning.UnableToCheck,Assert.Single(report.Results).Meaning);
    }
    [Fact] public async Task Loops_and_ten_term_budget_are_limited_not_a_sender_permerror()
    {
        foreach(var loop in new[]{false,true})
        {
            var resolver=new Resolver(q=>Txt(q,loop?"v=spf1 include:example.invalid -all":"v=spf1 "+string.Join(' ',Enumerable.Repeat("include:leaf.invalid",11))+" -all"));
            var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf),default);
            Assert.Equal(DomainReadinessState.Partial,report.Status);Assert.Contains(report.Limitations,l=>l.Contains(loop?"loop":"budget",StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(report.Results,r=>r.Summary.Contains("permerror",StringComparison.OrdinalIgnoreCase));Assert.True(resolver.Questions.Count<=2);
        }
    }
    [Fact] public async Task Public_enrolment_questions_are_distinct_and_do_not_claim_device_enrolment()
    {
        var resolver=new Resolver(q=>Answer(q,new DomainDnsAnswer(q.Name,DomainDnsType.CNAME,"Answer",null,null,null,null,
            q.Name.StartsWith("enterpriseenrollment.",StringComparison.Ordinal)?"enterpriseenrollment-s.manage.microsoft.com":"enterpriseregistration.windows.net")));
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.WindowsEnrolment),default);
        Assert.Equal(2,resolver.Questions.Count);Assert.Equal(2,report.Results.Count);Assert.All(report.Results,r=>Assert.Equal(DomainReadinessMeaning.ObservedMatch,r.Meaning));
        Assert.Contains(report.Limitations,l=>l.Contains("not proof",StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public async Task Cancellation_preserves_observations_and_marks_later_checks_unable()
    {
        using var stop=new CancellationTokenSource();var resolver=new Resolver(q=>{stop.Cancel();return Txt(q,"v=spf1 -all");});
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf,DomainReadinessCheck.Mx),stop.Token);
        Assert.Equal(DomainReadinessState.Cancelled,report.Status);Assert.Single(report.Observations);Assert.Single(resolver.Questions);
        Assert.Contains(report.Results,r=>r.Check==DomainReadinessCheck.Mx&&r.Meaning==DomainReadinessMeaning.UnableToCheck);
    }
    [Fact] public async Task Report_deadline_prevents_new_queries_while_retaining_earlier_observations()
    {
        var resolver=new Resolver(q=>{var answer=Txt(q,"v=spf1 include:later.invalid -all");_clock.UtcNow=_clock.UtcNow.AddSeconds(121);return answer;});
        var report=await new DomainReadinessCollector(resolver,_clock).CollectAsync(Request(DomainReadinessCheck.Spf,DomainReadinessCheck.Mx),default);
        Assert.Single(resolver.Questions);Assert.Single(report.Observations);Assert.Equal(DomainReadinessState.Partial,report.Status);
    }
    [Fact] public async Task Unsupported_cloud_and_missing_expected_gateway_provenance_refuse_before_queries()
    {
        var resolver=new Resolver(q=>throw new InvalidOperationException());var collector=new DomainReadinessCollector(resolver,_clock);
        await Assert.ThrowsAsync<ConfigurationException>(()=>collector.CollectAsync(Request(DomainReadinessCheck.WindowsEnrolment) with{Cloud="Other"},default));
        await Assert.ThrowsAsync<ConfigurationException>(()=>collector.CollectAsync(Request(DomainReadinessCheck.Mx) with{ExpectedMx=new[]{"gateway.invalid"}},default));
        Assert.Empty(resolver.Questions);
    }
}
