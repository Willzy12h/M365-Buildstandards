using System.IO.Compression;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationExportTests
{
    internal static ConditionalAccessEvaluationCapture Capture(string? source=null)
    {
        var raw=source is null?ConditionalAccessEvaluationSchemaTests.Response():Encoding.UTF8.GetBytes(source);
        var report=ConditionalAccessEvaluationSchemaTests.Valid(raw);
        report.Status=BuildStandard.Core.Reporting.ConditionalAccessEvaluationResponseCodec.Read(raw).IsPartial?"Partial":"Complete";
        ConditionalAccessEvaluationSchema.Seal(report,raw);return new(report,raw);
    }
    [Fact] public void Original_pair_round_trips_without_reformatting_or_resealing_and_import_is_historical()
    {
        var live=Capture();var input=ConditionalAccessEvaluationBundle.Write(live);var read=ConditionalAccessEvaluationBundle.Read(input,TestData.TenantA);
        Assert.Equal(live.MetadataBytes,read.MetadataBytes);Assert.Equal(live.OriginalResponseBytes,read.OriginalResponseBytes);
        Assert.Contains("historical",ConditionalAccessEvaluationDocuments.Html(read,"Synthetic"),StringComparison.OrdinalIgnoreCase);
        using var zip=new ZipArchive(new MemoryStream(input));Assert.Equal(new[]{"metadata.json","response.json","NOTICE.txt"},zip.Entries.Select(e=>e.FullName));
        Assert.DoesNotContain("token",Encoding.UTF8.GetString(read.MetadataBytes),StringComparison.OrdinalIgnoreCase);
    }
    [Theory][InlineData("metadata.json")][InlineData("response.json")][InlineData("NOTICE.txt")]
    public void Missing_required_files_are_not_an_empty_success(string file)
    {var bytes=Archive(new(){["metadata.json"]=Capture().MetadataBytes,["response.json"]=Capture().OriginalResponseBytes,["NOTICE.txt"]=Encoding.UTF8.GetBytes(ConditionalAccessEvaluationBundle.Notice)},remove:file);Assert.Throws<ConfigurationException>(()=>ConditionalAccessEvaluationBundle.Read(bytes,TestData.TenantA));}
    [Theory][InlineData("../metadata.json")][InlineData("other.json")][InlineData("METADATA.JSON")][InlineData("metadata.json")]
    public void Extra_or_duplicate_paths_are_refused_without_extracting_anything(string file)
    {var bytes=Archive(new(){["metadata.json"]=Capture().MetadataBytes,["response.json"]=Capture().OriginalResponseBytes,["NOTICE.txt"]=Encoding.UTF8.GetBytes(ConditionalAccessEvaluationBundle.Notice)},extra:file);Assert.Throws<ConfigurationException>(()=>ConditionalAccessEvaluationBundle.Read(bytes,TestData.TenantA));}
    [Fact] public void A_compressed_oversized_source_is_bounded_before_materialisation()
    {var bytes=Archive(new(){["metadata.json"]=Capture().MetadataBytes,["response.json"]=new byte[6*1024*1024],["NOTICE.txt"]=Encoding.UTF8.GetBytes(ConditionalAccessEvaluationBundle.Notice)});Assert.True(bytes.Length<100000);Assert.Throws<ConfigurationException>(()=>ConditionalAccessEvaluationBundle.Read(bytes,TestData.TenantA));}
    [Fact] public void Truncated_or_non_zip_content_is_not_a_successful_check()
    {Assert.Throws<ConfigurationException>(()=>ConditionalAccessEvaluationBundle.Read([1,2,3],TestData.TenantA));}
    [Fact] public void Wrong_expected_tenant_and_changed_source_are_refused()
    {
        var capture=Capture();Assert.Throws<TenantMismatchException>(()=>ConditionalAccessEvaluationBundle.Read(ConditionalAccessEvaluationBundle.Write(capture),TestData.TenantB));
        var source=capture.OriginalResponseBytes;source[0]=32;var bytes=Archive(new(){["metadata.json"]=capture.MetadataBytes,["response.json"]=source,["NOTICE.txt"]=Encoding.UTF8.GetBytes(ConditionalAccessEvaluationBundle.Notice)});
        Assert.ThrowsAny<ToolkitException>(()=>ConditionalAccessEvaluationBundle.Read(bytes,TestData.TenantA));
    }
    [Fact] public void Unknown_rows_are_preserved_as_unknown_in_HTML_and_tabular_output()
    {
        var c=Capture("{\"value\":[{\"id\":\""+TestData.Emergency+"\",\"state\":\"future\",\"policyApplies\":null}]}");
        var sheets=ConditionalAccessEvaluationDocuments.Sheets(c);var all=string.Join("\n",sheets.SelectMany(s=>s.Rows).SelectMany(r=>r));
        Assert.Contains("Partial",all);Assert.Contains("Unknown",all);Assert.Contains("not a real sign-in",all);Assert.Contains("Omitted",all);
        var html=ConditionalAccessEvaluationDocuments.Html(c,"Synthetic");Assert.Contains("Unknown",html);Assert.DoesNotContain("checked successfully",html,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void Service_text_cannot_inject_HTML_or_spreadsheet_formulas()
    {
        var source="{\"value\":[{\"id\":\""+TestData.Emergency+"\",\"displayName\":\"=HYPERLINK(<script>synthetic</script>)\",\"state\":\"enabled\",\"policyApplies\":true,\"analysisReasons\":\"notSet\"}]}";
        var c=Capture(source);var html=ConditionalAccessEvaluationDocuments.Html(c,"<img src=x>");
        Assert.DoesNotContain("<script>",html);Assert.DoesNotContain("<img",html);Assert.Contains("&lt;script&gt;",html);Assert.Contains("default-src 'none'",html);
        var csv=CsvWriter.Write(ConditionalAccessEvaluationDocuments.Sheets(c).Last().Rows);Assert.Contains("'=HYPERLINK",csv);
    }
    [Fact] public void A_successful_empty_array_is_explicitly_different_from_failed_or_missing_results()
    {var c=Capture("{\"value\":[]}");var html=ConditionalAccessEvaluationDocuments.Html(c,"");Assert.Contains("no policy results returned",html,StringComparison.OrdinalIgnoreCase);Assert.Contains("not proof",html,StringComparison.OrdinalIgnoreCase);}
    [Fact] public void Original_result_context_uses_the_recorded_scenario_actor_interval_and_limitations()
    {
        var c=Capture();var imported=ConditionalAccessEvaluationBundle.Read(ConditionalAccessEvaluationBundle.Write(c),TestData.TenantA);
        var text=ConditionalAccessEvaluationDocuments.OriginalContext(imported);
        Assert.Contains("Scenario user ID: "+TestData.Operator,text);Assert.Contains("Scenario application ID: "+TestData.TenantA,text);
        Assert.DoesNotContain("Scenario application ID: "+TestData.TenantB,text);Assert.Contains("Engineer account ID: "+TestData.Operator,text);
        Assert.Contains("2026-10-20T00:00:00.000Z",text);Assert.Contains("Historical",text);Assert.Contains("not a real sign-in",text);
        Assert.Contains("Omitted",text);Assert.Contains("Integrity digest (not a signature)",text);
    }
    private static byte[] Archive(Dictionary<string,byte[]> files,string? remove=null,string? extra=null)
    {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {foreach(var f in files.Where(f=>f.Key!=remove)){using var stream=zip.CreateEntry(f.Key).Open();stream.Write(f.Value);}if(extra is not null){using var s=zip.CreateEntry(extra).Open();s.Write([1]);}}
        return memory.ToArray();
    }
}
