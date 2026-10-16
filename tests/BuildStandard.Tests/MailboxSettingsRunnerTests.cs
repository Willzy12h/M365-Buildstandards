using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;

namespace BuildStandard.Tests;

public sealed class MailboxSettingsRunnerTests
{
    public static TheoryData<string,string> Runtimes()
    {
        var data = new TheoryData<string,string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var pair in new[] { ("5.1",Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe")),
                ("7",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PowerShell","7","pwsh.exe")) })
                if (File.Exists(pair.Item2)) data.Add(pair.Item1,pair.Item2);
        }
        else
        {
            var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p,"pwsh")).FirstOrDefault(File.Exists);
            if (path is not null) data.Add("7",path);
        }
        return data; // No installed runtime is a failing theory, never a synthetic pass or skipped verification.
    }

    private sealed class Run(string runtime,string path) : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"buildstandard-settings-test-"+Guid.NewGuid().ToString("N"));
        public string Log => File.Exists(Path.Combine(Root,"fake.log")) ? File.ReadAllText(Path.Combine(Root,"fake.log")) : "";
        public async Task<ReadRunResult> Execute(string item,string scenario,CancellationToken ct=default,ScriptEntry? custom=null)
        {
            Directory.CreateDirectory(Root);
            var environment = new Dictionary<string,string>
            {
                ["PSModulePath"] = string.Join(Path.PathSeparator,Path.Combine(ExchangeReportRunnerTests.FixtureDirectory,"Modules"),Path.Combine(Path.GetDirectoryName(path)!,"Modules")),
                ["BDIT_FAKE_SCENARIO"] = scenario,["BDIT_FAKE_TENANT"] = TestData.TenantA,["BDIT_FAKE_LOG"] = Path.Combine(Root,"fake.log")
            };
            var host = new ReadRunnerHost(_ => path,environment,null,Path.Combine(Root,"staging"));
            return await new ExchangeReportRunner(host,new SystemClock()).RunAsync(custom ?? ScriptCatalogue.Shipped.Find(item),
                new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),new Dictionary<string,string?>(),runtime,null,ct);
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
    private static readonly string[] Items = ["exo.mailbox-protocol-settings","exo.mailbox-forwarding-settings","exo.mailbox-hold-settings"];
    private static JsonObject First(ReadRunResult result) => result.Evidence!.Sections[0].Rows[0];

    [Theory, MemberData(nameof(Runtimes))]
    public async Task Every_new_adapter_runs_with_typed_rows_and_preserves_false_empty_and_inheritance(string runtime,string path)
    {
        foreach (var item in Items)
        {
            using var run = new Run(runtime,path); var result = await run.Execute(item,"settings-normal");
            Assert.Equal(ReadRunOutcome.Completed,result.Outcome); Assert.Equal("Collected",result.Evidence!.Status);
            Assert.Equal(2,result.Evidence.Sections[0].Rows.Count); Assert.DoesNotContain("WRITE ",run.Log);
            var row = First(result); Assert.Equal("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",row["id"]!.GetValue<string>());
            if (item.Contains("protocol")) { Assert.False(row["popEnabled"]!.GetValue<bool>()); Assert.Equal("Inherit",row["smtpAuthOverride"]!.GetValue<string>()); Assert.True(row["effectiveSmtpAuthDisabled"]!.GetValue<bool>()); }
            else if (item.Contains("forwarding")) { Assert.Null(row["forwardingAddress"]); Assert.Equal("Collected",row["forwardingAddressReadStatus"]!.GetValue<string>()); Assert.False(row["deliverToMailboxAndForward"]!.GetValue<bool>()); }
            else { Assert.False(row["litigationHoldEnabled"]!.GetValue<bool>()); Assert.Empty(row["inPlaceHoldIds"]!.AsArray()); }
        }
    }
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Wrong_tenant_and_account_refuse_every_new_adapter_without_report_reads(string runtime,string path)
    {
        foreach (var item in Items) foreach (var scenario in new[] {"wrong-tenant","wrong-account"})
        {
            using var run = new Run(runtime,path); var result = await run.Execute(item,scenario);
            Assert.NotEqual(ReadRunOutcome.Completed,result.Outcome);Assert.Null(result.Evidence);Assert.DoesNotContain("Read ",run.Log);
        }
    }
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Missing_or_malformed_booleans_are_partial_unknown_never_false(string runtime,string path)
    {
        foreach (var (item,field) in Items.Zip(new[]{"popEnabled","deliverToMailboxAndForward","litigationHoldEnabled"}))
        foreach (var scenario in new[]{"settings-missing-flag","settings-malformed-flag"})
        {
            using var run = new Run(runtime,path);var result = await run.Execute(item,scenario);
            Assert.Equal(ReadRunOutcome.Completed,result.Outcome);Assert.Equal("Partial",result.Evidence!.Status);
            Assert.Null(First(result)[field]);Assert.Equal("Failed",First(result)[field+"ReadStatus"]!.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(First(result)["error"]!.GetValue<string>()));
        }
    }
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Missing_mailbox_identity_is_excluded_and_duplicate_identity_refuses_evidence(string runtime,string path)
    {
        foreach(var item in Items)
        {
            using var run = new Run(runtime,path); var result=await run.Execute(item,"settings-no-mailbox-guid");
            Assert.Equal("Partial",result.Evidence!.Status);Assert.Single(result.Evidence.Sections[0].Rows);
        }
        using var duplicate=new Run(runtime,path);var invalid=await duplicate.Execute(Items[1],"settings-duplicate-guid");
        Assert.Null(invalid.Evidence);Assert.NotEqual(ReadRunOutcome.Completed,invalid.Outcome);
    }
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Smtp_override_absence_org_failure_and_explicit_override_have_distinct_meanings(string runtime,string path)
    {
        foreach(var (scenario,expected,disabled) in new (string,string,bool?)[]{("settings-smtp-absent","Unknown",null),("settings-org-null","Inherit",null),("settings-smtp-enabled","Enabled",false),("settings-smtp-disabled","Disabled",true)})
        {
            using var run = new Run(runtime,path);var result=await run.Execute(Items[0],scenario);var row=First(result);
            Assert.Equal(expected,row["smtpAuthOverride"]!.GetValue<string>());Assert.Equal(disabled,row["effectiveSmtpAuthDisabled"]?.GetValue<bool>());
            Assert.Equal(disabled is null ? "Partial" : "Collected",result.Evidence!.Status);
        }
    }
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Missing_hold_collection_is_unknown_and_excess_is_not_truncated_into_success(string runtime,string path)
    {
        foreach(var scenario in new[]{"settings-null-holds","settings-excess-holds"})
        {
            using var run=new Run(runtime,path);var result=await run.Execute(Items[2],scenario);
            Assert.Equal("Partial",result.Evidence!.Status);Assert.Null(First(result)["inPlaceHoldIds"]);
        }
    }
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Cas_identity_mismatch_and_individual_read_failure_do_not_admit_settings(string runtime,string path)
    {
        foreach(var scenario in new[]{"settings-cas-wrong-id","settings-cas-fail"})
        {
            using var run=new Run(runtime,path);var result=await run.Execute(Items[0],scenario);
            Assert.Equal("Partial",result.Evidence!.Status);Assert.Null(First(result)["popEnabled"]);
        }
    }
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Cancellation_before_run_refuses_all_new_adapters_without_starting_a_child(string runtime,string path)
    {
        foreach(var item in Items)
        {
            using var run=new Run(runtime,path);using var cts=new CancellationTokenSource();cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Execute(item,"settings-normal",cts.Token));
            Assert.Empty(run.Log);
        }
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task Collection_output_in_an_inventory_scalar_column_is_still_refused_by_the_wrapper(string runtime,string path)
    {
        var body=ScriptCatalogue.Shipped.Find("exo.mailbox-size-quota").Script.Replace("$row.primarySizeRaw = $size","$row.primarySizeRaw = @('one','two')");
        Assert.Contains("@('one','two')",body);
        using var run=new Run(runtime,path);var result=await run.Execute("unused","settings-normal",custom:ExchangeReportRunnerTests.Entry(body));
        Assert.Equal(ReadRunOutcome.Failed,result.Outcome);Assert.Null(result.Evidence);Assert.DoesNotContain("WRITE ",run.Log);
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task Cap_is_applied_before_excluding_missing_identities_and_truncation_stays_partial(string runtime,string path)
    {
        using var run=new Run(runtime,path);var result=await run.Execute(Items[1],"over-cap-missing-guid");
        Assert.Equal(ReadRunOutcome.Completed,result.Outcome);Assert.Equal("Partial",result.Evidence!.Status);
        var section=result.Evidence.Sections[0];Assert.Equal(9999,section.Rows.Count);
        Assert.Contains("10,000",section.Error);Assert.Contains("excluded",section.Error);
    }
}
