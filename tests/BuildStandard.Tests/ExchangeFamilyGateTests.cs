using System.Diagnostics;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExchangeFamilyGateTests
{
    [Theory][InlineData("projection")][InlineData("permission-source")][InlineData("permission-unselected")][InlineData("permission-send-as")][InlineData("permission-send-on-behalf")][InlineData("permission-attribute-budget")][InlineData("lower-budget")][InlineData("diagnostic-reserve")][InlineData("wrong-target")][InlineData("unverified-child")][InlineData("wrong-object")][InlineData("extra-parameter")][InlineData("hidden-scope")][InlineData("rows")][InlineData("bytes")][InlineData("calls")][InlineData("actor")][InlineData("stale-parent")][InlineData("failed-lookup-after-good")]
    public Task Fixed_private_family_gate_binds_targets_scope_and_shared_budgets_before_reads(string scenario) => RunFixture("Test-FamilyGate.ps1", scenario);

    [Theory][InlineData("false")][InlineData("missing")][InlineData("empty")][InlineData("reference")][InlineData("counts")][InlineData("blank-name")]
    [InlineData("forged-boolean")][InlineData("unknown-with-value")][InlineData("reasonless")][InlineData("unknown-property")][InlineData("list-limit")]
    [InlineData("reference-limit")][InlineData("bad-reference")][InlineData("forged-id")][InlineData("unknown-column")][InlineData("count-overflow")]
    [InlineData("string-overflow")][InlineData("unknown-state")][InlineData("control-identity")][InlineData("priority-type")][InlineData("scalar-flag")][InlineData("read-state")]
    public Task Fixed_family_output_preserves_typed_states_and_refuses_arbitrary_values(string scenario) =>
        RunFixture("Test-FamilyOutput.ps1", scenario);

    [Theory][InlineData("raw-sid")][InlineData("self")][InlineData("label")][InlineData("missing")]
    public Task Pinned_permission_source_projection_preserves_principal_namespace_and_unknown_flags(string scenario) =>
        RunFixture("Test-PermissionProjection.ps1", scenario);

    private static async Task RunFixture(string fixture, string scenario)
    {
        var runtime=OperatingSystem.IsWindows()?Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe")
            : (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Select(p=>Path.Combine(p,"pwsh")).FirstOrDefault(File.Exists);
        Assert.False(string.IsNullOrWhiteSpace(runtime),"Use an already provisioned PowerShell runtime; no dependency installation is attempted.");
        using var root=new TempRoot();var wrapper=Path.Combine(root.Root,"owned-wrapper.ps1");
        using(var source=typeof(ReadRunnerTemplate).Assembly.GetManifestResourceStream("Runner.ReadRunner.ps1"))
        {Assert.NotNull(source);using var destination=File.Create(wrapper);source.CopyTo(destination);}
        var start=new ProcessStartInfo(runtime!){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var value in new[]{"-NoLogo","-NoProfile","-NonInteractive","-File",Path.Combine(ExchangeReportRunnerTests.FixtureDirectory,fixture),"-Wrapper",wrapper,"-Case",scenario})start.ArgumentList.Add(value);
        using var process=new Process{StartInfo=start};Assert.True(process.Start());
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.True(process.ExitCode==0,await output+await error);Assert.Contains("PASS "+scenario,await output);}
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
    }
}
