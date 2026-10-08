using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class AuthoredImportNamingTests
{
    [Theory]
    [InlineData(false, "Reviewed candidate")]
    [InlineData(true, "Reviewed candidate")]
    [InlineData(false, "CFG - Reviewed LAPS")]
    [InlineData(true, "CFG - Reviewed LAPS")]
    [InlineData(false, "cfg - Reviewed LAPS - Windows")]
    [InlineData(true, "cfg - Reviewed LAPS - Windows")]
    [InlineData(false, "CFG - Reviewed  LAPS - Windows")]
    [InlineData(true, "CFG - Reviewed  LAPS - Windows")]
    [InlineData(false, " CFG - Reviewed LAPS - Windows")]
    [InlineData(true, " CFG - Reviewed LAPS - Windows")]
    [InlineData(false, "CFG - Reviewed LAPS - Windows ")]
    [InlineData(true, "CFG - Reviewed LAPS - Windows ")]
    [InlineData(false, "CFG - Reviewed LAPS \uD83D\uDE00 - Windows")]
    [InlineData(true, "CFG - Reviewed LAPS \uD83D\uDE00 - Windows")]
    public void New_import_refuses_nonconforming_name_without_changing_the_historical_baseline(bool general, string name)
    {
        var baseline = DevicePolicyImportTests.Shipped();
        var before = ToolkitJson.Serialize(baseline);
        var source = baseline.FindControl("CFG-WIN-002")!.Payload!.ToJsonString();
        var failure = Assert.Throws<ConfigurationException>(() => Import(general, baseline, "CFG-WIN-002", source, name));
        Assert.Contains("New authored name", failure.Message);
        Assert.Equal(before, ToolkitJson.Serialize(baseline));
    }

    [Theory]
    [InlineData(false, "CFG-WIN-002")]
    [InlineData(true, "CFG-WIN-002")]
    [InlineData(false, "SEC-WIN-001")]
    [InlineData(true, "SEC-WIN-001")]
    [InlineData(false, "SEC-WIN-002")]
    [InlineData(true, "SEC-WIN-002")]
    [InlineData(false, "SEC-WIN-003")]
    [InlineData(true, "SEC-WIN-003")]
    public void Valid_new_name_does_not_rewrite_other_controls_or_reject_historical_names(bool general, string id)
    {
        var baseline = DevicePolicyImportTests.Shipped();
        var before = ToolkitJson.Serialize(baseline);
        var name = "CFG - Reviewed candidate - Windows";
        var source = baseline.FindControl(id)!.Payload!.ToJsonString();
        var imported = Import(general, baseline, id, source, name);
        Assert.Equal(before, ToolkitJson.Serialize(baseline));
        Assert.Equal("Candidate", imported.Standard.Status);
        Assert.Equal(name, imported.Standard.FindControl(id)!.Payload!["displayName"]!.GetValue<string>());
        foreach (var original in baseline.Controls.Where(c => c.Id != id))
            Assert.Equal(ToolkitJson.Serialize(original), ToolkitJson.Serialize(imported.Standard.FindControl(original.Id)));
    }

    private static DevicePolicyImport Import(bool general, BDIT.TenantToolkit.Core.Models.StandardCatalogue baseline,
        string id, string source, string name) => general
        ? PolicyImporter.Import(baseline, id, source, name)
        : DevicePolicyImporter.Import(baseline, id, source, name);
}
