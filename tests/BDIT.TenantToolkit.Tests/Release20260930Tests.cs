using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class Release20260930Tests
{
    private static string DirectoryPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards"));
    private static JsonObject Read(string release) => JsonNode.Parse(File.ReadAllText(Path.Combine(DirectoryPath, release + ".json")))!.AsObject();

    [Fact]
    public void Successor_retires_only_the_approved_legacy_controls_and_keeps_candidate_payloads()
    {
        var old = Release20260912Tests.Standard();
        var current = StandardsLoader.Parse(Read("2026.09.30").ToJsonString(), "2026.09.30.json");
        Assert.Equal("2026.09.30", current.Release);
        Assert.Equal(93, current.Controls.Count);
        Assert.Equal(61, current.Controls.Count(c => c.HasRecipe));
        Assert.Equal(new[] { "ENR-003", "ENR-004", "SEC-WIN-002" }, old.Controls.Select(c => c.Id).Except(current.Controls.Select(c => c.Id)).Order());
        Assert.NotNull(current.FindControl("ENR-007"));
        Assert.NotNull(current.FindControl("APP-WIN-008"));
        foreach (var control in current.Controls.Where(c => c.HasRecipe))
            Assert.True(JsonNode.DeepEquals(old.FindControl(control.Id)!.Payload, control.Payload), control.Id);
        Assert.Contains("SmartScreen", current.FindControl("CFG-WIN-004")!.DocumentationNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_control_has_complete_manual_disposition_and_a_primary_source()
    {
        var source = Read("2026.09.30");
        var current = StandardsLoader.Parse(source.ToJsonString(), "2026.09.30.json");
        foreach (var c in current.Controls)
        {
            Assert.NotNull(c.Implementation);
            Assert.NotEmpty(c.Implementation!.Before);
            Assert.NotEmpty(c.Implementation.PortalSteps);
            Assert.False(string.IsNullOrWhiteSpace(c.Implementation.PowerShell));
            Assert.NotEmpty(c.Implementation.After);
        }
        foreach (var c in source["controls"]!.AsArray())
            Assert.Contains(c!["references"]!.AsObject(), reference => reference.Value?.ToString().StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal) == true);
        foreach (var id in new[] { "CFG-WIN-009", "CFG-WIN-012", "CFG-WIN-013", "ENR-007", "ID-004" })
        {
            var c = current.FindControl(id)!;
            Assert.False(c.HasRecipe);
            Assert.Contains("unverified", c.DocumentationNotes, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Historical_standard_bytes_match_the_published_baseline_digests()
    {
        // These are publication digests, independent of the regenerable shipping manifest.
        var hashes = new Dictionary<string, string>
        {
            ["2026.09.3"] = "0f8f0fb9e974294b8c264f891045cbfe27ab4b0abddfe146c7e6f6a4c8f618cc",
            ["2026.09.4"] = "143de8f0e7cc0458da1d58284adec427ed56b758912f513facce11c609c28112",
            ["2026.09.5"] = "4f393b147d5feecbe7750cd38056085deea8c5258762825c210ca35f87f3bf23",
            ["2026.09.6"] = "267a0a2601c87a5c23f0f69027dee7b8bbda8c3b965e635c75b91550225373b9",
            ["2026.09.7"] = "0db379d6325d49d89a3e6f85f7765ba03fbe05760823a65ba2c1809d4e509163",
            ["2026.09.8"] = "316dfb6984e341f32e1859d0b36cdf8f30a7ec542f13af35027eea8cba6f423c",
            ["2026.09.9"] = "4cbb5ecc017633e44142e00b99402e769817fe07960644964842be0170a792b9",
            ["2026.09.10"] = "fc22f752390661db71bc5bd11ec895e2e934a6be16d5464e0c9790d60231d48d",
            ["2026.09.11"] = "f30b2e162901df80a21bf2b024f85d6bca83f45696125137f3b2fd0e1625973a",
            ["2026.09.12"] = "9553ecbcae476f45742042f7503095bb811019dec48527156c3791b05efa6a70",
            // Published as the default of the v1.1.0-preview.18 prerelease (RELEASE-RECORD.json catalogueSha256).
            ["2026.09.30"] = "124a033454385b262960db2a6f61392ee3e64ad8abb1a6f0d8276db29e21302f"
        };
        foreach (var (release, digest) in hashes)
            Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(DirectoryPath, release + ".json")))).ToLowerInvariant());
        Assert.Equal(64, StandardsManifest.Load(DirectoryPath).Verify(DirectoryPath, "2026.09.30.json").Length);
    }
}
