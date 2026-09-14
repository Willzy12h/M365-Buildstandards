using System.Net;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Standards;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class DeviceRecipeTests
{
    private static StandardCatalogue Shipped() => StandardsLoader.Parse(File.ReadAllText(Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.4.json"))), "2026.09.4.json");

    [Fact]
    public void Supplied_BitLocker_settings_preserve_unconfigured_options_and_inactive_PIN()
    {
        var standard = Shipped();
        var control = standard.FindControl("CFG-WIN-001")!;
        var payload = control.Payload!;
        Assert.Equal(GraphApi.Beta, standard.Collections[control.Collection!].ApiVersion);
        Assert.True(payload["bitLockerEncryptDevice"]!.GetValue<bool>());
        Assert.True(payload["bitLockerDisableWarningForOtherDiskEncryption"]!.GetValue<bool>());
        Assert.True(payload["bitLockerAllowStandardUserEncryption"]!.GetValue<bool>());
        var system = payload["bitLockerSystemDrivePolicy"]!;
        Assert.True(system["startupAuthenticationRequired"]!.GetValue<bool>());
        Assert.Equal("required", system["startupAuthenticationTpmUsage"]!.GetValue<string>());
        foreach (var key in new[] { "startupAuthenticationTpmPinUsage", "startupAuthenticationTpmKeyUsage", "startupAuthenticationTpmPinAndKeyUsage" })
            Assert.Equal("blocked", system[key]!.GetValue<string>());
        Assert.Equal(14, system["minimumPinLength"]!.GetValue<int>());
        Assert.Contains("inactive", control.DocumentationNotes);
        foreach (var drive in new[] { system, payload["bitLockerFixedDrivePolicy"]! })
        {
            var recovery = drive["recoveryOptions"]!.AsObject();
            Assert.True(recovery["enableRecoveryInformationSaveToStore"]!.GetValue<bool>());
            Assert.True(recovery["enableBitLockerAfterRecoveryInformationToStore"]!.GetValue<bool>());
            Assert.Equal(2, recovery.Count); // No invented recovery password/key/UI/content choices.
            Assert.Null(drive["encryptionMethod"]);
        }
        Assert.Null(payload["bitLockerRemovableDrivePolicy"]);
        Assert.False(payload.ContainsKey("assignments"));
    }

    [Fact]
    public void Long_paths_use_documented_ADMX_string_and_remain_unassigned()
    {
        var control = Shipped().FindControl("CFG-WIN-007")!;
        var setting = Assert.Single(control.Payload!["omaSettings"]!.AsArray())!;
        Assert.Equal("./Device/Vendor/MSFT/Policy/Config/ADMX_FileSys/LongPathsEnabled", setting["omaUri"]!.GetValue<string>());
        Assert.Equal("<enabled/>", setting["value"]!.GetValue<string>());
        Assert.Equal("unassigned", control.SafeDeployment.State);
        Assert.False(control.Payload.ContainsKey("assignments"));
    }

    [Theory]
    [InlineData("CFG-WIN-001")]
    [InlineData("CFG-WIN-007")]
    public async Task Shipped_candidate_is_journalled_verified_and_recoverable_but_assigned_objects_are_protected(string id)
    {
        var shipped = Shipped(); var control = shipped.FindControl(id)!;
        var standard = TestData.Standard();
        standard.Controls.Add(control);
        standard.Collections[control.Collection!] = shipped.Collections[control.Collection!];
        using var h = new RecoveryTests.Harness(standard);
        var run = await h.Deploy(id);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(WriteAcceptance.Accepted, run.Results.Single().WriteAcceptance);
        Assert.Equal(ConfigurationVerification.Pass, run.Results.Single().Configuration);
        Assert.False(h.Graph.Writes.Single().Payload.ContainsKey("assignments"));
        h.Object(run)["_assignments"] = new JsonArray(new JsonObject { ["id"] = TestData.Operator });
        await Assert.ThrowsAsync<SafetyViolationException>(() => h.Preview(run));
        Assert.Empty(h.Graph.RecoveryWrites);
        h.Object(run)["_assignments"] = new JsonArray();
        var recovery = await h.Execute(await h.Preview(run));
        Assert.True(recovery.ObjectAbsent);
        Assert.Equal(ConfigurationVerification.Pass, recovery.Verification);
        Assert.Null(h.Evidence.LoadMappings(h.Session.TenantId).Find(id));
    }

    [Fact]
    public async Task Beta_recovery_is_restricted_to_device_configuration_and_never_replayed()
    {
        var handler = new Handler();
        var routes = GraphRouteAllowList.Only(new[] {
            new GraphRoute(GraphApi.Beta, "/deviceManagement/deviceConfigurations", "read", "write", "endpointProtection"),
            new GraphRoute(GraphApi.Beta, "/identity/conditionalAccess/policies", "read", "write", "unsupported")
        });
        var graph = new GraphClient(new HttpClient(handler), new Tokens(), TestData.TenantA, SessionMode.Deployment,
            routes, new GraphClientOptions { Sleep = false }, NullLog.Instance);
        await Assert.ThrowsAsync<WriteDeniedException>(() => graph.RecoverAsync(GraphApi.Beta, RecoveryAction.DeleteCreatedObject,
            "/identity/conditionalAccess/policies/" + TestData.Operator, null, default));
        Assert.Equal(0, handler.Requests);
        await Assert.ThrowsAsync<AmbiguousWriteException>(() => graph.RecoverAsync(GraphApi.Beta, RecoveryAction.DeleteCreatedObject,
            "/deviceManagement/deviceConfigurations/" + TestData.Operator, null, default));
        Assert.Equal(1, handler.Requests);
        Assert.False(RecoverySafety.Supports(GraphApi.Beta, "/deviceManagement/deviceCompliancePolicies"));
    }
    private sealed class Tokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("synthetic");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.StartsWith("https://graph.microsoft.com/beta/deviceManagement/deviceConfigurations/", request.RequestUri!.ToString());
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
