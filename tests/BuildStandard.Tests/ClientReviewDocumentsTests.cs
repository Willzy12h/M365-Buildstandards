using System.IO.Compression;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ClientReviewDocumentsTests : IDisposable
{
    private readonly TempRoot _root=new();private readonly EvidenceStore _store;private readonly TenantProfile _profile=TestData.Profile();
    public ClientReviewDocumentsTests(){_store=new(_root.Paths,NullLog.Instance);_store.SaveProfiles([_profile]);}
    public void Dispose()=>_root.Dispose();
    private ClientReviewDocuments.Document Document()=>ClientReviewDocuments.Create(_profile,_store.LoadClientReviewEvidence(TestData.TenantA),new FixedClock().UtcNow);
    private string Export(ClientReviewDocuments.Document doc,Func<bool>? current=null)=>ClientReviewDocuments.Export(doc,_store,_root.Paths.ReportsDirectory,current??(()=>true));
    [Fact] public void A_pack_contains_matching_summary_counts_safe_tables_and_original_source_citations()
    {
        var run=new DeploymentRun{Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA,StartedAt="2026-09-11T10:00:00Z",
            Results=[new RunResult{ControlId="CA-001",WriteAcceptance=WriteAcceptance.Unknown,Status=ResultStatus.Error}]};_store.SaveRun(run);
        var doc=Document();var folder=Export(doc);Assert.Equal(4,Directory.GetFiles(folder).Length);
        var manifest=ToolkitJson.ParseObject(File.ReadAllText(Path.Combine(folder,"manifest.json")));
        Assert.Equal(TestData.TenantA,manifest["tenantId"]!.GetValue<string>());Assert.Equal(1,manifest["unresolvedWrites"]!.GetValue<int>());
        var original=Assert.Single(doc.Evidence.Sources);Assert.Contains(original.Sha256,manifest.ToJsonString());
        foreach(var p in (System.Text.Json.Nodes.JsonObject)manifest["files"]!)Assert.Equal(p.Value!.GetValue<string>(),CanonicalJson.Sha256Hex(File.ReadAllBytes(Path.Combine(folder,p.Key))));
        Assert.Contains("Unresolved writes</td><td>1",File.ReadAllText(Path.Combine(folder,"engineer.html")));
        Assert.Contains("Unresolved writes</td><td>1",File.ReadAllText(Path.Combine(folder,"client.html")));
        using var zip=ZipFile.OpenRead(Path.Combine(folder,"review.csv.zip"));Assert.Contains(zip.Entries,e=>e.Name.Contains("Summary",StringComparison.Ordinal));
    }
    [Fact] public void Hostile_client_text_is_html_encoded_and_csv_formula_guarded()
    {
        _profile.Company="=HYPERLINK(\"bad\")<script>alert(1)</script>";_store.SaveProfiles([_profile]);var doc=Document();
        var html=ClientReviewDocuments.Html(doc);Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;",html);Assert.Contains("default-src 'none'",html);
        Assert.Contains("\"'=HYPERLINK",CsvWriter.Write(ClientReviewDocuments.Sheets(doc)[0].Rows));
    }
    [Fact] public void Changed_source_refuses_export_without_publishing_a_partial_pack()
    {
        var doc=Document();_store.SaveRun(new DeploymentRun{Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA,StartedAt="2026-09-11T10:00:00Z"});
        Assert.Throws<ConfigurationException>(()=>Export(doc));Assert.Empty(Directory.GetDirectories(_root.Paths.ReportsDirectory));
    }
    [Fact] public void Changed_saved_client_refuses_export_even_if_its_tenant_is_unchanged()
    {
        var doc=Document();var changed=TestData.Profile();changed.Company="Changed company";_store.SaveProfiles([changed]);
        Assert.Throws<ConfigurationException>(()=>Export(doc));
    }
    [Fact] public void A_changed_context_after_writing_discards_only_its_own_staging_files()
    {
        var keep=Path.Combine(_root.Paths.ReportsDirectory,"keep.txt");File.WriteAllText(keep,"original");var count=0;
        Assert.Throws<ConfigurationException>(()=>Export(Document(),()=>++count==1));Assert.Equal(2,count);
        Assert.Equal("original",File.ReadAllText(keep));Assert.Empty(Directory.GetDirectories(_root.Paths.ReportsDirectory));
    }
    [Fact] public void Cancellation_never_publishes_a_pack()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ClientReviewDocuments.Export(Document(),_store,_root.Paths.ReportsDirectory,()=>true,stop.Token));
        Assert.Empty(Directory.GetDirectories(_root.Paths.ReportsDirectory));
    }
    [Fact] public void Corrupt_but_fully_cited_history_exports_as_unknown_not_empty_success()
    {
        var dir=Path.Combine(_store.TenantDirectory(TestData.TenantA),"runs");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"bad.json"),"null");
        var doc=Document();Assert.False(doc.Review.WriteHistoryKnown);var folder=Export(doc);
        Assert.Contains("Unknown",File.ReadAllText(Path.Combine(folder,"engineer.html")));Assert.Contains("bad.json",File.ReadAllText(Path.Combine(folder,"manifest.json")));
    }
    [Fact] public void Exports_cannot_write_into_original_client_evidence()
    {Assert.Throws<ConfigurationException>(()=>ClientReviewDocuments.Export(Document(),_store,_store.TenantDirectory(TestData.TenantA),()=>true));}
    [Fact] public void A_stop_during_final_admission_does_not_publish_staged_files()
    {
        using var stop=new CancellationTokenSource();var calls=0;
        Assert.Throws<OperationCanceledException>(()=>ClientReviewDocuments.Export(Document(),_store,_root.Paths.ReportsDirectory,()=>
        {if(++calls==2)stop.Cancel();return true;},stop.Token));Assert.Empty(Directory.GetDirectories(_root.Paths.ReportsDirectory));
    }
    [Fact] public void An_output_junction_cannot_create_a_folder_under_original_evidence()
    {
        var link=Path.Combine(_root.Paths.ReportsDirectory,"linked");Directory.CreateSymbolicLink(link,_root.Paths.DataDirectory);
        Assert.Throws<ConfigurationException>(()=>ClientReviewDocuments.Export(Document(),_store,Path.Combine(link,"new-output"),()=>true));
        Assert.False(Directory.Exists(Path.Combine(_root.Paths.DataDirectory,"new-output")));
    }
}
