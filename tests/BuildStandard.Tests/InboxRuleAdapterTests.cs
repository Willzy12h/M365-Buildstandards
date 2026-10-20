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
        new[] { "inbox-values", "inbox-missing", "inbox-empty", "inbox-failed", "wrong-account", "wrong-tenant" }
            .Select(scenario => new object[] { runtime[0], scenario }));

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
        if (scenario.StartsWith("wrong-", StringComparison.Ordinal))
        {
            Assert.Null(result.Evidence); Assert.DoesNotContain("Read ", log); return;
        }
        Assert.NotNull(result.Evidence);
        var evidence = result.Evidence;
        Assert.Equal(2, evidence.ReportSchemaVersion);
        var target = Assert.Single(evidence.Sections[0].Rows);
        Assert.Equal(TestData.Office, target["requestedExchangeGuid"]!.GetValue<string>());
        Assert.Contains("Read Get-InboxRule " + TestData.Office + " hidden=False", log);
        if (scenario == "inbox-failed")
        {
            Assert.Empty(evidence.Sections[1].Rows); Assert.Equal("Failed", target["childReadStatus"]!.GetValue<string>());
            Assert.Null(target["returnedEntries"]); Assert.NotEqual("Collected", evidence.Status);
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
            Assert.False(row["enabled"]!["value"]!.GetValue<bool>());
            Assert.Empty(row["forwardTo"]!["value"]!.AsArray());
            Assert.Equal(scenario == "inbox-missing" ? "Missing" : "Value", row["deleteMessage"]!["state"]!.GetValue<string>());
            Assert.Equal(scenario == "inbox-missing" ? "Partial" : "Collected", evidence.Status);
        }
        Assert.DoesNotContain("Set-", log); Assert.DoesNotContain("New-", log);
    }
}
