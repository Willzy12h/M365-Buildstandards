using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;

public sealed class InboxRuleAdapterTests
{
    public static IEnumerable<object[]> Cases() => ExchangeReportRunnerTests.Runtimes().Cast<object[]>().SelectMany(runtime =>
        new[] { "inbox-values", "inbox-missing", "inbox-empty", "inbox-failed", "inbox-missing-id", "inbox-duplicate", "inbox-null", "inbox-unresolved", "inbox-mismatch", "inbox-actor-change", "inbox-raw-identity", "wrong-account", "wrong-tenant" }
            .Select(scenario => new object[] { runtime[0], scenario }));

    [Theory][MemberData(nameof(ExchangeReportRunnerTests.Runtimes), MemberType = typeof(ExchangeReportRunnerTests))]
    public async Task Cooperative_stop_retains_earlier_admitted_rules_and_diagnostics_for_every_target(string runtime)
    {
        using var root = new TempRoot(); using var stop = new CancellationTokenSource();
        var entry = ScriptCatalogue.Shipped.Find("exo.inbox-rule-observations");
        var environment = ExchangeReportRunnerTests.FakeEnvironment(runtime, root.Root, "inbox-stop");
        var executable = OperatingSystem.IsWindows()
            ? (runtime == "5.1" ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"))
            : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, "pwsh")).First(File.Exists);
        var host = new ReadRunnerHost(_ => executable, environment, null, Path.Combine(root.Root, "staging"));
        var children = 0;
        var progress = new InlineProgress(text =>
        {
            if (text.StartsWith("Reading Get-InboxRule", StringComparison.Ordinal) && Interlocked.Increment(ref children) == 2) stop.Cancel();
        });
        var result = await new ExchangeReportRunner(host, new SystemClock()).RunAsync(entry,
            new(TestData.TenantA, TestData.Operator, "operator@synthetic.example"),
            new Dictionary<string,string?> { ["MailboxGuids"] = TestData.Office + ",bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" }, runtime, progress, stop.Token);
        Assert.Equal(ReadRunOutcome.Cancelled, result.Outcome); Assert.NotNull(result.Evidence);
        Assert.Equal("Cancelled", result.Evidence.Status); Assert.Equal(2, result.Evidence.Sections[0].Rows.Count);
        var rule = Assert.Single(result.Evidence.Sections[1].Rows);
        Assert.Equal(TestData.Office, rule["mailboxExchangeGuid"]!.GetValue<string>());
        Assert.Null(result.Evidence.Sections[0].Rows[1]["returnedEntries"]);
        Assert.Equal(2, children);
    }
    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    { public void Report(string value) => report(value); }

    [Theory][MemberData(nameof(Cases))]
    public async Task Pinned_inbox_adapter_runs_only_the_frozen_scope_and_retains_truthful_states(string runtime, string scenario)
    {
        using var root = new TempRoot();
        var entry = ScriptCatalogue.Shipped.Find("exo.inbox-rule-observations");
        var environment = ExchangeReportRunnerTests.FakeEnvironment(runtime, root.Root, scenario);
        var executable = OperatingSystem.IsWindows()
            ? (runtime == "5.1" ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"))
            : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, "pwsh")).First(File.Exists);
        var staging = Path.Combine(root.Root, "staging");
        var host = new ReadRunnerHost(_ => executable, environment, null, staging);
        var result = await new ExchangeReportRunner(host, new SystemClock()).RunAsync(entry,
            new(TestData.TenantA, TestData.Operator, "operator@synthetic.example"),
            new Dictionary<string, string?> { ["MailboxGuids"] = TestData.Office, ["IncludeHidden"] = "false" }, runtime, null, CancellationToken.None);
        var log = File.Exists(environment["BDIT_FAKE_LOG"]) ? File.ReadAllText(environment["BDIT_FAKE_LOG"]) : "";
        if (scenario == "inbox-actor-change")
        {
            Assert.Null(result.Evidence); Assert.NotEqual(ReadRunOutcome.Completed, result.Outcome);
            Assert.Equal(2, log.Split('\n').Count(line => line.StartsWith("Read ", StringComparison.Ordinal))); return;
        }
        if (scenario.StartsWith("wrong-", StringComparison.Ordinal))
        {
            Assert.Null(result.Evidence); Assert.DoesNotContain("Read ", log); return;
        }
        Assert.NotNull(result.Evidence);
        var evidence = result.Evidence;
        Assert.Equal(2, evidence.ReportSchemaVersion);
        var target = Assert.Single(evidence.Sections[0].Rows);
        Assert.Equal(TestData.Office, target["requestedExchangeGuid"]!.GetValue<string>());
        if (scenario == "inbox-mismatch")
        {
            Assert.Empty(evidence.Sections[1].Rows); Assert.Equal("Failed", target["lookupReadStatus"]!.GetValue<string>());
            Assert.DoesNotContain("Read Get-InboxRule", log); Assert.NotEqual("Collected", evidence.Status); return;
        }
        Assert.Contains("Read Get-InboxRule " + TestData.Office + " hidden=False", log);
        if (scenario is "inbox-failed" or "inbox-duplicate")
        {
            Assert.Empty(evidence.Sections[1].Rows); Assert.Equal("Failed", target["childReadStatus"]!.GetValue<string>());
            Assert.Null(target["returnedEntries"]); Assert.NotEqual("Collected", evidence.Status);
        }
        else if (scenario == "inbox-missing-id")
        {
            Assert.Empty(evidence.Sections[1].Rows); Assert.Equal(1, target["returnedEntries"]!.GetValue<int>());
            Assert.Equal(1, target["excludedEntries"]!.GetValue<int>()); Assert.Equal("Partial", evidence.Status);
        }
        else if (scenario == "inbox-empty")
        {
            Assert.Empty(evidence.Sections[1].Rows); Assert.Equal(0, target["returnedEntries"]!.GetValue<int>());
            Assert.Equal("Collected", evidence.Status);
        }
        else
        {
            var row = Assert.Single(evidence.Sections[1].Rows);
            Assert.Equal("18446744073709551615", row["ruleIdentity"]!.GetValue<string>());
            Assert.Equal(ExchangeObservationKey.InboxRule(TestData.Office, "18446744073709551615"), row["id"]!.GetValue<string>());
            Assert.Equal(scenario == "inbox-raw-identity" ? "opaque-original" : "raw opaque rule", row["actualIdentityRaw"]!.GetValue<string>());
            Assert.DoesNotContain("excluded unrelated", row.ToJsonString());
            Assert.False(row["enabled"]!["value"]!.GetValue<bool>());
            if (scenario == "inbox-unresolved")
            {
                var reference = Assert.Single(row["forwardTo"]!["value"]!.AsArray());
                Assert.Equal("UnresolvedDisplayLabel", reference!["namespace"]!.GetValue<string>());
                Assert.Null(reference["externalDirectoryObjectId"]);
            }
            else Assert.Empty(row["forwardTo"]!["value"]!.AsArray());
            Assert.Equal(scenario == "inbox-missing" ? "Missing" : scenario == "inbox-null" ? "ReturnedNull" : "Value", row["deleteMessage"]!["state"]!.GetValue<string>());
            Assert.Equal(scenario is "inbox-missing" or "inbox-null" or "inbox-unresolved" ? "Partial" : "Collected", evidence.Status);
        }
        Assert.DoesNotContain("Set-", log); Assert.DoesNotContain("New-", log);
    }
}
