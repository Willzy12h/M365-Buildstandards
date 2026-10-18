using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;
public sealed class DomainReadinessDocumentsTests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"bs-domain-docs-"+Guid.NewGuid().ToString("N"));
    private ToolkitPaths Paths {get;}
    public DomainReadinessDocumentsTests(){Directory.CreateDirectory(Path.Combine(_root,"standards"));Paths=ToolkitPaths.Resolve(_root);}
    public void Dispose()=>Directory.Delete(_root,true);
    private static byte[] Bytes()=>Encoding.UTF8.GetBytes("\uFEFF"+ToolkitJson.Serialize(DomainReadinessSchemaTests.Valid()));
    [Fact] public void Imported_bytes_stay_historical_unverified_and_are_defensively_copied()
    {
        var original=Bytes();var expected=original.ToArray();var doc=DomainReadinessDocument.Import(original);
        original[0]=0;doc.OriginalBytes[0]=0;doc.Report.Domain="changed.invalid";
        Assert.True(doc.Historical);Assert.Equal(expected,doc.OriginalBytes);Assert.Equal(CanonicalJson.Sha256Hex(expected),doc.OriginalSha256);
        Assert.Equal("example.invalid",doc.Report.Domain);Assert.Null(doc.Report.TenantId);
        Assert.Contains("Historical",DomainReadinessDocuments.Html(doc));Assert.Contains("unverified",DomainReadinessDocuments.Html(doc));
    }
    [Fact] public void Collected_document_is_validated_frozen_and_not_historical_or_a_deployment_capture()
    {
        var source=DomainReadinessSchemaTests.Valid();var doc=DomainReadinessDocument.Collected(source);source.Domain="changed.invalid";
        Assert.False(doc.Historical);Assert.Equal("example.invalid",doc.Report.Domain);
        Assert.Contains("not deployment evidence",DomainReadinessDocuments.Html(doc));
        Assert.Throws<ConfigurationException>(()=>DomainReadinessDocument.Collected(source));
    }
    [Fact] public void Html_and_csv_preserve_unknowns_and_safe_text_in_a_manual_dns_owner_worksheet()
    {
        var r=DomainReadinessSchemaTests.Valid();r.Results[0]=r.Results[0] with{Summary="<script>bad</script>",DnsOwnerNextStep="=HYPERLINK(unsafe)"};
        r.IntegrityDigest=EvidenceIntegrity.Compute(r);var doc=DomainReadinessDocument.Collected(r);
        var html=DomainReadinessDocuments.Html(doc);Assert.DoesNotContain("<script>",html);Assert.Contains("&lt;script&gt;",html);
        Assert.Contains("default-src 'none'",html);Assert.Contains("DNS-owner worksheet",html);Assert.Contains("Unknown",html);
        Assert.Contains("'=" ,CsvWriter.Write(DomainReadinessDocuments.Sheets(doc).Single(s=>s.Name=="DNS-owner worksheet").Rows));
        Assert.Contains("configured Windows resolver",html,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void Export_keeps_original_json_bytes_and_manifest_digests_and_never_overwrites_a_pack()
    {
        var bytes=Bytes();var doc=DomainReadinessDocument.Import(bytes);var output=Path.Combine(_root,"output");
        var first=DomainReadinessDocuments.Export(doc,Paths,output,()=>true);var second=DomainReadinessDocuments.Export(doc,Paths,output,()=>true);
        Assert.NotEqual(first,second);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(first,"report.json")));
        var manifest=JsonNode.Parse(File.ReadAllText(Path.Combine(first,"manifest.json")))!.AsObject();Assert.True(manifest["historical"]!.GetValue<bool>());
        foreach(var file in manifest["files"]!.AsObject())Assert.Equal(file.Value!.GetValue<string>(),CanonicalJson.Sha256Hex(File.ReadAllBytes(Path.Combine(first,file.Key))));
        using var zip=ZipFile.OpenRead(Path.Combine(first,"observations.csv.zip"));Assert.Contains(zip.Entries,e=>e.Name.Contains("Observations",StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.GetDirectories(output),p=>p.EndsWith(".tmp",StringComparison.Ordinal));
    }
    [Fact] public void Changed_original_file_is_refused_without_publishing_or_deleting_unrelated_output()
    {
        var input=Path.Combine(_root,"input.json");File.WriteAllBytes(input,Bytes());var doc=DomainReadinessDocument.ReadFile(input);
        File.AppendAllText(input," ");var output=Path.Combine(_root,"out");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"keep.txt"),"keep");
        Assert.Throws<ConfigurationException>(()=>DomainReadinessDocuments.Export(doc,Paths,output,()=>true));
        Assert.Equal("keep",File.ReadAllText(Path.Combine(output,"keep.txt")));Assert.Empty(Directory.GetDirectories(output));
    }
    [Theory][InlineData("data")][InlineData("standards")][InlineData("config")][InlineData("app")]
    public void Export_refuses_package_configuration_catalogues_and_original_evidence(string child)
    {
        var output=Path.Combine(_root,child,"nested");
        Assert.Throws<ConfigurationException>(()=>DomainReadinessDocuments.Export(DomainReadinessDocument.Import(Bytes()),Paths,output,()=>true));
        Assert.False(Directory.Exists(output));
    }
    [Fact] public void Context_change_after_staging_and_final_source_read_leaves_no_published_pack()
    {
        var checks=0;var output=Path.Combine(_root,"out");
        Assert.Throws<ConfigurationException>(()=>DomainReadinessDocuments.Export(DomainReadinessDocument.Import(Bytes()),Paths,output,()=>++checks<3));
        Assert.Empty(Directory.GetFileSystemEntries(output));Assert.Equal(3,checks);
    }
    [Fact] public void Cancellation_preserves_original_and_does_not_publish()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();var output=Path.Combine(_root,"out");
        Assert.Throws<OperationCanceledException>(()=>DomainReadinessDocuments.Export(DomainReadinessDocument.Import(Bytes()),Paths,output,()=>true,stop.Token));
        Assert.False(Directory.Exists(output));
    }
    [Fact] public void File_import_is_bounded_and_does_not_admit_another_report_family()
    {
        var input=Path.Combine(_root,"input.json");File.WriteAllBytes(input,new byte[5*1024*1024+1]);
        Assert.Throws<ConfigurationException>(()=>DomainReadinessDocument.ReadFile(input));
        Assert.Throws<ConfigurationException>(()=>DomainReadinessDocument.Import(Encoding.UTF8.GetBytes("{\"kind\":\"tenantSnapshot\"}")));
    }
}
