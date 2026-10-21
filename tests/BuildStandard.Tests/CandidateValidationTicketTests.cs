using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateValidationTicketTests
{
    [Fact]public void Fresh_ticket_revalidates_exact_source_but_saved_time_does_not_invalidate_it()
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);var draft=loader.Create("test.json","Synthetic candidate");var service=new CandidateAuthoringService(loader);var ticket=CandidateValidationTicket.Create(service,draft);
        draft.SavedAtUtc=DateTimeOffset.UtcNow;CandidateDraftCodec.Seal(draft);var result=ticket.RequireCurrent(service,draft);Assert.Equal("Candidate",result.Status);Assert.Equal(ticket.CandidateDigest,CanonicalJson.Sha256Value(result));
    }
    [Theory][InlineData("edit")][InlineData("candidate")][InlineData("baseline")][InlineData("file")]
    public void Stale_or_forged_ticket_does_not_admit_a_candidate(string fault)
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);var draft=loader.Create("test.json","Synthetic candidate");var service=new CandidateAuthoringService(loader);var ticket=CandidateValidationTicket.Create(service,draft);
        switch(fault){case "edit":draft.Title="Changed";CandidateDraftCodec.Seal(draft);break;case "candidate":ticket=ticket with{CandidateDigest=new string('b',64)};break;case "baseline":ticket=ticket with{BaselineDigest=new string('b',64)};break;case "file":root.WriteStandard("test.json",TestData.StandardJson+" ");root.WriteManifest();break;}
        Assert.ThrowsAny<ToolkitException>(()=>ticket.RequireCurrent(service,draft));
    }
}
