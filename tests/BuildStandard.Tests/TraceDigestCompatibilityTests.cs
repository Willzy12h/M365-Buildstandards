using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class TraceDigestCompatibilityTests
{
    [Theory][InlineData("plain")][InlineData("html")][InlineData("ascii")][InlineData("unicode")]
    public async Task Private_powershell_tuple_encoding_matches_the_current_engine_json_encoder(string scenario)
    {
        var header=scenario switch {"html"=>"<synthetic+&'\\\"`@message.invalid>","ascii"=>new string(Enumerable.Range(0,128).Select(value=>(char)value).ToArray()),"unicode"=>"旅行 é 😀 \u2028",_=>null};
        object?[] tuple=["sender@synthetic.example",null,header,null,false,5000];
        var expected=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tuple)));
        var payload=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{sender=tuple[0],recipient=tuple[1],internetMessageId=tuple[2],messageTraceId=tuple[3],includeSubject=false,maximumRows=5000}));
        using var root=new TempRoot();var wrapper=Path.Combine(root.Root,"wrapper.ps1");
        using(var input=typeof(ReadRunnerTemplate).Assembly.GetManifestResourceStream("Runner.ReadRunner.ps1")){Assert.NotNull(input);using var output=File.Create(wrapper);input.CopyTo(output);}
        var runtime=OperatingSystem.IsWindows()?Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"):ExchangeReportRunnerTests.RuntimePath("7");
        var start=new ProcessStartInfo(runtime){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",Path.Combine(ExchangeReportRunnerTests.FixtureDirectory,"Test-TraceDigest.ps1"),"-Wrapper",wrapper,"-Payload",payload,"-Expected",expected})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.True(process.ExitCode==0,await stdout+await stderr);Assert.Contains("PASS trace tuple digest",await stdout);}
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
    }
}
