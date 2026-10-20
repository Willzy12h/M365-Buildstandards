using System.Diagnostics;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class BoundedExchangeProjectionTests
{
    [Theory][InlineData("values")][InlineData("rows")][InlineData("field")][InlineData("list")][InlineData("reference")][InlineData("identity")][InlineData("budget")][InlineData("shape")][InlineData("empty-identity")][InlineData("distinguished-reference")][InlineData("special-reference")][InlineData("unresolved-reference")]
    public async Task Fixed_private_projection_preserves_meaning_and_refuses_overflow_before_body_materialisation(string scenario)
    {
        var runtime=OperatingSystem.IsWindows()?Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe")
            : (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Select(p=>Path.Combine(p,"pwsh")).FirstOrDefault(File.Exists);
        Assert.False(string.IsNullOrWhiteSpace(runtime),"Use an already provisioned PowerShell runtime; no dependency installation is attempted.");
        using var root=new TempRoot();var wrapper=Path.Combine(root.Root,"owned-wrapper.ps1");
        using(var source=typeof(ReadRunnerTemplate).Assembly.GetManifestResourceStream("Runner.ReadRunner.ps1"))
        {Assert.NotNull(source);using var destination=File.Create(wrapper);source.CopyTo(destination);}
        var start=new ProcessStartInfo(runtime!){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",Path.Combine(ExchangeReportRunnerTests.FixtureDirectory,"Test-BoundedProjection.ps1"),"-Wrapper",wrapper,"-Case",scenario})start.ArgumentList.Add(value);
        using var process=new Process{StartInfo=start};Assert.True(process.Start());
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.True(process.ExitCode==0,await output+await error);Assert.Contains("PASS "+scenario,await output);}
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
    }
}
