using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class TraceContextCompatibilityTests
{
    [Theory][InlineData("valid")][InlineData("member-order")][InlineData("window-order")][InlineData("null")][InlineData("extra")][InlineData("digest")][InlineData("bounds")][InlineData("schema")][InlineData("unfiltered")][InlineData("boolean")][InlineData("future-date")][InlineData("duplicate")][InlineData("escaped-duplicate")]
    public async Task Private_context_admission_recomputes_the_original_partition_and_refuses_forgery(string scenario)
    {
        static JsonNode Text(string text)=>JsonValue.Create(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)))!;
        var parameters=TraceRunPreparationTests.Parameters().Select(p=>p.Name=="StartDate"?p with{Value="2026-10-01",Payload=Text("2026-10-01")}:p.Name=="EndDate"?p with{Value="2026-10-19",Payload=Text("2026-10-19")}:p).ToArray();
        var prepared=TraceRunPreparation.Prepare(TraceRunPreparationTests.Entry(),new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),parameters,"7",new(2026,10,20,0,0,0,TimeSpan.Zero));
        var context=prepared.RequestContext();var args=new JsonObject{["StartDate"]="2026-10-01",["EndDate"]="2026-10-19",["Sender"]="sender@synthetic.example",["IncludeSubject"]=false,["MaximumRows"]=5000};
        switch(scenario)
        {
            case "window-order":var reversed=context["windows"]!.AsArray().Reverse().Select(n=>n!.DeepClone()).ToArray();context["windows"]=new JsonArray(reversed);break;
            case "extra":context["extra"]=true;break;case "digest":context["filterDigest"]=new string('a',64);break;
            case "bounds":context["windows"]![0]!["endUtc"]="2026-10-12T00:00:00.000Z";break;case "schema":context["schemaVersion"]="1";break;
            case "unfiltered":args.Remove("Sender");break;case "boolean":args["IncludeSubject"]="false";break;case "future-date":args["EndDate"]="2026-10-21";break;
            case "member-order":var reordered=new JsonObject();foreach(var pair in context.Reverse())reordered[pair.Key]=pair.Value?.DeepClone();context=reordered;break;
        }
        var request=new JsonObject{["traceContext"]=scenario=="null"?null:context,["parameters"]=new JsonArray(parameters.Select(ScriptReadRequest.Parameter).ToArray())};
        var raw=request.ToJsonString();
        if(scenario is "duplicate" or "escaped-duplicate")
        {
            Assert.Contains("\"schemaVersion\":1",raw);
            raw=raw.Replace("\"schemaVersion\":1",scenario=="duplicate"?"\"schemaVersion\":1,\"schemaVersion\":1":"\"schemaVersion\":1,\"\\u0073chemaVersion\":1",StringComparison.Ordinal);
        }
        var requestBytes=Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject{["requestUtf8Base64"]=requestBytes,["arguments"]=args}.ToJsonString()));
        using var root=new TempRoot();var wrapper=Path.Combine(root.Root,"wrapper.ps1");using(var input=typeof(ReadRunnerTemplate).Assembly.GetManifestResourceStream("Runner.ReadRunner.ps1")){Assert.NotNull(input);using var output=File.Create(wrapper);input.CopyTo(output);}
        var runtime=OperatingSystem.IsWindows()?Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"):ExchangeReportRunnerTests.RuntimePath("7");
        var start=new ProcessStartInfo(runtime){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",Path.Combine(ExchangeReportRunnerTests.FixtureDirectory,"Test-TraceContext.ps1"),"-Wrapper",wrapper,"-Payload",payload,"-Valid",scenario is "valid" or "member-order"?"true":"false"})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.True(process.ExitCode==0,await stdout+await stderr);Assert.Contains("PASS trace request context",await stdout);}
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
    }
}
