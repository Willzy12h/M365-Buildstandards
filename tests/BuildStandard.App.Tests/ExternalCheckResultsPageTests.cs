using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExternalCheckResultsPageTests
{
    private static PropertyInfo Property(object instance, string name)
    { var result = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); Assert.NotNull(result); return result; }
    private static object Page(ConnectedReportsFixture fixture)
    { var parent = new GraphReportsViewModel(new ShellViewModel(fixture.Workspace)); return Property(parent, "External").GetValue(parent)!; }
    private static T Get<T>(object page, string name) => (T)Property(page, name).GetValue(page)!;
    private static Task Call(object page, string name, params object[] args)
    {
        var method = page.GetType().GetMethod(name); Assert.NotNull(method);
        try { return (Task)method.Invoke(page, args)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static byte[] Bytes()
    {
        var value = new JsonObject { ["kind"] = "external-check-results", ["schemaVersion"] = 1,
            ["toolName"] = "Synthetic checks", ["toolVersion"] = "1", ["sourceRunId"] = "synthetic", ["tenantId"] = TestData.TenantA,
            ["performedAt"] = "2026-10-20T09:00:00Z", ["sourceState"] = "Partial", ["integrityDigest"] = "",
            ["rows"] = new JsonArray(new JsonObject { ["testId"] = "TEST-1", ["outcome"] = "Passed", ["title"] = "Synthetic source claim" }) };
        value["integrityDigest"] = CanonicalJson.Sha256(value); return Encoding.UTF8.GetBytes("  " + value.ToJsonString() + "\n");
    }
    [Fact] public async Task Native_import_and_original_export_preserve_claims_without_snapshot_or_network_authority()
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f); var bytes = Bytes(); var file = Path.Combine(f.Root.Root, "external.json"); File.WriteAllBytes(file, bytes);
        var snapshot = f.Workspace.Snapshot; await Call(page, "OpenSuppliedAsync", file);
        Assert.Contains("untrusted", Get<string>(page, "Outcome"), StringComparison.OrdinalIgnoreCase); Assert.Contains("Partial", Get<string>(page, "Outcome"));
        Assert.True(Get<bool>(page, "HasPendingOriginal"));
        await Call(page, "ExportAsync", ExportFormat.Json);
        Assert.Equal(bytes, File.ReadAllBytes(Get<string>(page, "LastExport"))); Assert.False(Get<bool>(page, "HasPendingOriginal"));
        Assert.Same(snapshot, f.Workspace.Snapshot); Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    [Fact] public async Task Late_client_change_hides_import_but_retains_source_bound_original_for_recovery()
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f); var bytes = Bytes();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, string, Task<ExternalCheckImport>> read = async (_, tenant) => { entered.SetResult(); await release.Task; return ExternalCheckResultsSchema.Read(bytes, tenant); };
        Property(page, "ReadOriginal").SetValue(page, read);
        var opening = Call(page, "OpenSuppliedAsync", "synthetic.json"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        f.ReplaceConnection(); release.SetResult(); await Assert.ThrowsAnyAsync<ToolkitException>(() => opening);
        Assert.Contains("No current", Get<string>(page, "Outcome")); Assert.True(Get<bool>(page, "HasPendingOriginal"));
        await Call(page, "ExportPendingOriginalAsync"); Assert.Equal(bytes, File.ReadAllBytes(Get<string>(page, "LastExport")));
        Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    [Fact] public async Task Explicit_control_reference_round_trips_without_changing_native_assessment()
    {
        using var f = new ConnectedReportsFixture(); var page = Page(f); var bytes = Bytes();
        var file = Path.Combine(f.Root.Root, "external-reference-source.json"); File.WriteAllBytes(file, bytes);
        await Call(page, "OpenSuppliedAsync", file);
        Property(page, "SelectedTestId").SetValue(page, "TEST-1");
        Property(page, "SelectedControlId").SetValue(page, f.Workspace.Standard!.Controls[0].Id);
        var add = page.GetType().GetMethod("AddReference"); Assert.NotNull(add); add.Invoke(page, null);
        Assert.Equal(1, Get<int>(page, "ReferenceCount")); await Call(page, "ExportReferencesAsync");
        var referenceFile = Get<string>(page, "LastExport");
        var source = ExternalCheckResultsSchema.Read(bytes, TestData.TenantA);
        var link = Assert.Single(ExternalControlReferenceDocument.Read(File.ReadAllBytes(referenceFile), source));
        Assert.True(ExternalControlReferences.Resolve(f.Root.Paths, link, source, f.Workspace.Standard).Resolved);
        await Call(page, "OpenReferencesAsync", referenceFile); Assert.Equal(1, Get<int>(page, "ReferenceCount"));
        Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }

}
