using System.Text.Json.Nodes;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
public sealed class TraceRunPreparationTests
{
    private static readonly DateTimeOffset Now=new(2026,10,20,23,59,0,TimeSpan.Zero);
    private static readonly ReadRunContext Actor=new(TestData.TenantA,TestData.Operator,"operator@synthetic.example");
    internal static ScriptEntry Entry()
    {
        var old=ScriptCatalogue.Shipped.Find("exo.distribution-group-observations");
        // Pure review data may be characterised offline; this synthetic entry still cannot pass the package-owned Run gate.
        return old with {Manifest=old.Manifest with{Id="exo.message-trace-observations",Execution=old.Manifest.Execution! with{ReportId="exo-message-trace"}}};
    }
    internal static IReadOnlyList<ReadRunParameter> Parameters()
    {
        static JsonNode Text(string value)=>JsonValue.Create(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)))!;
        return new ReadRunParameter[]
        {
            new("StartDate","Date","2026-10-11",true,Text("2026-10-11")),new("EndDate","Date","2026-10-20",true,Text("2026-10-20")),
            new("Sender","Text","sender@synthetic.example",true,Text("sender@synthetic.example")),new("Recipient","Text","",false,null),
            new("InternetMessageId","Text","",false,null),new("MessageTraceId","Text","",false,null),new("IncludeSubject","Boolean","false",true,JsonValue.Create(false)),new("MaximumRows","Integer","5000",true,JsonValue.Create(5000))
        };
    }
    [Fact] public void A_self_consistent_result_clock_cannot_replace_the_original_review_clock()
    {
        var actor=Actor;var entry=Entry();var report=TraceEvidenceV2Tests.Report();
        var parameters=report.Parameters.Select(p=>new ReadRunParameter(p.Name,p.Type,p.Value,p.Type!="Text"||p.Value.Length>0,
            p.Type=="Boolean"?JsonValue.Create(p.Value=="true"):p.Type=="Integer"?JsonValue.Create(int.Parse(p.Value,System.Globalization.CultureInfo.InvariantCulture)):p.Value.Length==0?null:JsonValue.Create(Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Value))))).ToArray();
        var preparation=TraceRunPreparation.Prepare(entry,actor,parameters,"7",DateTimeOffset.Parse(TraceEvidenceV2Tests.Frozen,System.Globalization.CultureInfo.InvariantCulture));
        preparation.RequireResult(report.Sections);
        // Same dates and digest, and a later genuinely historical collection: still not this original request.
        report.Sections[0].Rows[0]["frozenAtUtc"]="2026-10-21T00:00:00.000Z";
        report.StartedAt="2026-10-21T00:00:01Z";report.EndedAt="2026-10-21T00:00:02Z";
        _=InboxRuleEvidenceV2Tests.Read(report);
        Assert.ThrowsAny<ToolkitException>(()=>preparation.RequireResult(report.Sections));
    }
    [Fact] public void A_wire_payload_that_disagrees_with_its_visible_value_is_refused()
    {
        var parameters=Parameters().Select(p=>p.Name=="StartDate"?p with{Payload=JsonValue.Create("Zm9yZ2Vk")}:p).ToArray();
        Assert.ThrowsAny<ToolkitException>(()=>TraceRunPreparation.Prepare(Entry(),Actor,parameters,"7",Now));
    }
    [Fact] public void Frozen_clock_and_windows_do_not_recompute_after_midnight()
    {
        var prepared=TraceRunPreparation.Prepare(Entry(),Actor,Parameters(),"7",Now);
        prepared.RequireMatch(Entry(),Actor,Parameters(),"7",Now.AddMinutes(2));
        Assert.Equal(Now,prepared.Query.FrozenNow);Assert.Equal(Now,prepared.Query.Windows[^1].End);
        var copy=prepared.RequestContext();copy["frozenAtUtc"]="forged";
        Assert.NotEqual("forged",prepared.RequestContext()["frozenAtUtc"]!.GetValue<string>());
    }
    [Theory][InlineData("expired")][InlineData("future")][InlineData("tenant")][InlineData("account")][InlineData("upn")][InlineData("runtime")][InlineData("pin")][InlineData("input")]
    public void Changed_or_stale_review_is_refused_before_any_run(string fault)
    {
        var entry=Entry();var actor=Actor;var parameters=Parameters();var runtime="7";var now=Now;
        var prepared=TraceRunPreparation.Prepare(entry,actor,parameters,runtime,now);
        switch(fault){case "expired":now=now.AddSeconds(301);break;case "future":now=now.AddTicks(-1);break;case "tenant":actor=actor with{TenantId=TestData.TenantB};break;
            case "account":actor=actor with{AccountObjectId=TestData.TenantB};break;case "upn":actor=actor with{AccountUpn="other@synthetic.example"};break;case "runtime":runtime="5.1";break;
            case "pin":entry=entry with{ManifestSha256=new string('a',64)};break;case "input":parameters=parameters.Select(p=>p.Name=="IncludeSubject"?p with{Value="true"}:p).ToArray();break;}
        Assert.ThrowsAny<ToolkitException>(()=>prepared.RequireMatch(entry,actor,parameters,runtime,now));
    }
}
