using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleCliTests
{
    [Fact]public async Task Actual_offline_history_lists_an_intent_without_promising_a_safe_outcome()
    {
        using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var id=(string)preview.GetType().GetProperty("Id")!.GetValue(preview)!;var digest=(string)preview.GetType().GetProperty("IntegrityDigest")!.GetValue(preview)!;
        var save=f.Evidence.GetType().GetMethod("SaveLifecycleIntent",BindingFlags.NonPublic|BindingFlags.Instance)!;
        save.Invoke(f.Evidence,new object[]{new JsonObject { ["kind"]="lifecycle-intent",["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,["planDigest"]=digest,["startedAt"]=Timestamps.Format(f.Clock.UtcNow),["integrityDigest"]="" }});
        var result=await RegisteredReportCliTests.Run("lifecycle","--root",f.Root.Root,"--tenant",TestData.TenantA);
        Assert.True(result.Exit==0,result.Error+result.Output);Assert.Empty(result.Error);var history=JsonNode.Parse(result.Output)!;
        Assert.Equal("lifecycle-history",history["kind"]?.GetValue<string>());Assert.Equal("Unknown",history["records"]![0]!["writeAcceptance"]?.GetValue<string>());Assert.Contains("no execution approval",history["assurance"]?.GetValue<string>());
    }
    [Theory][InlineData("json")][InlineData("html")]
    public async Task Actual_offline_record_export_preserves_original_or_encodes_hostile_text(string format)
    {
        using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var id=(string)preview.GetType().GetProperty("Id")!.GetValue(preview)!;
        var file=Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle","plan-"+id+".json");var document=JsonNode.Parse(File.ReadAllText(file))!.AsObject();document["untrustedNote"]="<script>never execute</script>";document["integrityDigest"]="";document["integrityDigest"]=CanonicalJson.Sha256(document);File.WriteAllText(file,"  "+document.ToJsonString()+"\n");var bytes=File.ReadAllBytes(file);
        var result=await RegisteredReportCliTests.Run("lifecycle","--root",f.Root.Root,"--tenant",TestData.TenantA,"--record",id,"--family","plan","--format",format);
        Assert.True(result.Exit==0,result.Error+result.Output);Assert.Empty(result.Error);var output=result.Output.Split('\n')[0].Trim();Assert.True(File.Exists(output));
        if(format=="json")Assert.Equal(bytes,File.ReadAllBytes(output));else {var html=File.ReadAllText(output);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain("<script>",html);Assert.Contains("no execution authority",html,StringComparison.OrdinalIgnoreCase);}
        Assert.Equal(bytes,File.ReadAllBytes(file));
    }
    [Theory][InlineData("--execute")][InlineData("--approve")][InlineData("--token")][InlineData("--tenant")]
    public async Task No_execution_or_unknown_duplicate_option_is_admitted(string option)
    {
        using var f=new LifecyclePreparationTests.Fixture();var result=await RegisteredReportCliTests.Run("lifecycle","--root",f.Root.Root,"--tenant",TestData.TenantA,option,"synthetic");
        Assert.Equal(2,result.Exit);Assert.Contains("Refused:",result.Error);Assert.Empty(result.Output);
    }
}
