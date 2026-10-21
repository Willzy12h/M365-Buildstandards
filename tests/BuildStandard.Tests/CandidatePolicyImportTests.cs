using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;

public sealed class CandidatePolicyImportTests
{
    private sealed class Fixture : IDisposable
    {
        internal TempRoot Root { get; } = new();
        internal CandidateBaselineLoader Baselines { get; }
        internal CandidateAuthoringService Authoring { get; }
        internal CandidatePolicyImportService Importer { get; }
        internal BuildStandard.Core.Models.StandardCandidateDraft Draft { get; }
        internal JsonObject Source { get; }
        internal string FileName => Path.Combine(Root.Paths.ReportsDirectory, "policy.json");
        internal Fixture(bool knownLiteral = false)
        {
            var standard = DevicePolicyImportTests.Shipped();
            if (knownLiteral) standard.FindControl("CFG-WIN-002")!.Payload!["omaSettings"]![0]!["displayName"] = TestData.Office;
            Root.WriteStandard("base.json", ToolkitJson.Serialize(standard)); Root.WriteManifest();
            Baselines = new(Root.Paths, NullLog.Instance); Authoring = new(Baselines); Importer = new(Baselines, Authoring);
            Draft = Baselines.Create("base.json", "Imported candidate");
            Source = Baselines.Load(Draft).FindControl("CFG-WIN-002")!.Payload!.DeepClone().AsObject();
            Source["id"] = TestData.TenantB; Source["roleScopeTagIds"] = new JsonArray("42");
            Source["omaSettings"]![2]!["value"] = 22;
        }
        internal BuildStandard.Core.Models.StandardCandidateDraft Import(IReadOnlyDictionary<string, string>? map = null)
        { File.WriteAllText(FileName, Source.ToJsonString()); return Importer.ImportFile(Draft, "CFG-WIN-002", FileName, "CFG - Candidate - Windows", map); }
        public void Dispose() => Root.Dispose();
    }
    [Theory][InlineData("missing", "Unknown")][InlineData("null", "Unknown")][InlineData("empty", "ReturnedEmpty")]
    [InlineData("populated", "ReturnedNotApplied")][InlineData("malformed", "Unknown")]
    public void Import_separates_configuration_from_assignment_claims(string state, string expected)
    {
        using var f = new Fixture(); var original = CandidateDraftCodec.Serialize(f.Draft);
        if (state != "missing") f.Source["assignments"] = state switch
        {
            "null" => null, "empty" => new JsonArray(), "malformed" => JsonValue.Create(false),
            _ => new JsonArray(new JsonObject { ["target"] = new JsonObject { ["groupId"] = TestData.Office } })
        };
        var draft = f.Import(); var candidate = f.Authoring.Validate(draft);
        Assert.Equal(original, CandidateDraftCodec.Serialize(f.Draft)); Assert.Empty(draft.Unresolved);
        Assert.Equal(expected, Assert.Single(draft.AssignmentObservations).Status);
        Assert.Equal(CanonicalJson.Sha256Hex(File.ReadAllBytes(f.FileName)), Assert.Single(draft.Imports).Sha256);
        var payload = candidate.FindControl("CFG-WIN-002")!.Payload!;
        Assert.Equal(22, payload["omaSettings"]![2]!["value"]!.GetValue<int>());
        Assert.Null(payload["id"]); Assert.Null(payload["assignments"]); Assert.Null(payload["roleScopeTagIds"]);
        Assert.DoesNotContain(TestData.TenantB, payload.ToJsonString()); Assert.DoesNotContain(TestData.Office, payload.ToJsonString());
    }
    [Fact]public void Foreign_reference_remains_unresolved_and_blocks_validation_until_explicit_typed_mapping()
    {
        using var f = new Fixture(); f.Source["omaSettings"]![0]!["displayName"] = TestData.Office;
        var unresolved = f.Import(); Assert.Single(unresolved.Unresolved);
        Assert.ThrowsAny<ToolkitException>(() => f.Authoring.Validate(unresolved));
        _ = CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(unresolved));
        var mapped = f.Import(new Dictionary<string, string> { ["/payload/omaSettings/0/displayName"] = "officeLocationId" });
        Assert.Empty(mapped.Unresolved);
        Assert.Equal("{{officeLocationId}}", f.Authoring.Validate(mapped).FindControl("CFG-WIN-002")!.Payload!["omaSettings"]![0]!["displayName"]!.GetValue<string>());
    }
    [Theory][InlineData("wrong-type")][InlineData("unknown-property")][InlineData("new-uri")][InlineData("expression")]
    public void Unsupported_source_is_refused_without_mutating_the_draft(string fault)
    {
        using var f = new Fixture(); var before = CandidateDraftCodec.Serialize(f.Draft);
        switch (fault)
        {
            case "wrong-type": f.Source["@odata.type"] = "#microsoft.graph.win32LobApp"; break;
            case "unknown-property": f.Source["unknown"] = true; break;
            case "new-uri": f.Source["omaSettings"]![0]!["omaUri"] = "./Device/Vendor/MSFT/Other"; break;
            case "expression": f.Source["omaSettings"]![0]!["displayName"] = "{{officeLocationId}}"; break;
        }
        Assert.ThrowsAny<ToolkitException>(() => f.Import()); Assert.Equal(before, CandidateDraftCodec.Serialize(f.Draft));
    }
    [Theory][InlineData("missingParameter")][InlineData("emergencyAccountIds")][InlineData("{{officeLocationId}}")]
    public void Mapping_must_name_one_existing_compatible_parameter(string parameter)
    {
        using var f = new Fixture(); f.Source["omaSettings"]![0]!["displayName"] = TestData.Office;
        Assert.ThrowsAny<ToolkitException>(() => f.Import(new Dictionary<string, string> { ["/payload/omaSettings/0/displayName"] = parameter }));
    }
    [Fact]public void Existing_control_edits_are_not_silently_overwritten_by_import()
    {
        using var f = new Fixture(); var baseline = f.Baselines.Load(f.Draft).FindControl("CFG-WIN-002")!.Payload!;
        f.Draft.Changes.Add(new("CFG-WIN-002", "/payload/displayName", baseline["displayName"]!.DeepClone(), JsonValue.Create("CFG - Existing - Windows")));
        CandidateDraftCodec.Seal(f.Draft); Assert.ThrowsAny<ToolkitException>(() => f.Import());
    }
    [Fact]public void Exact_known_baseline_literal_is_retained_without_being_treated_as_a_foreign_reference()
    {
        using var f = new Fixture(knownLiteral: true); var draft = f.Import(); Assert.Empty(draft.Unresolved);
        Assert.Equal(TestData.Office, f.Authoring.Validate(draft).FindControl("CFG-WIN-002")!.Payload!["omaSettings"]![0]!["displayName"]!.GetValue<string>());
    }
    [Fact]public void Embedded_foreign_identifier_cannot_be_resolved_by_substring_interpolation()
    {
        using var f = new Fixture(); f.Source["omaSettings"]![0]!["displayName"] = "foreign/" + TestData.Office;
        Assert.Single(f.Import().Unresolved);
        Assert.ThrowsAny<ToolkitException>(() => f.Import(new Dictionary<string, string> { ["/payload/omaSettings/0/displayName"] = "officeLocationId" }));
    }
    [Fact]public void Stale_or_unused_mapping_is_refused_instead_of_silently_ignored()
    {
        using var f = new Fixture();
        Assert.ThrowsAny<ToolkitException>(() => f.Import(new Dictionary<string, string> { ["/payload/noLongerPresent"] = "officeLocationId" }));
    }
    [Fact]public void Non_guid_notification_template_reference_is_unresolved_in_the_historical_registered_recipe()
    {
        using var root = new TempRoot();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.10.json"));
        root.WriteStandard("base.json", File.ReadAllText(path)); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance); var authoring = new CandidateAuthoringService(loader);
        var draft = loader.Create("base.json", "Notification review"); var source = loader.Load(draft).FindControl("CMP-WIN-001")!.Payload!.DeepClone();
        source["scheduledActionsForRule"]![0]!["scheduledActionConfigurations"]![0]!["notificationTemplateId"] = "foreign-template";
        var file = Path.Combine(root.Paths.ReportsDirectory, "source.json"); File.WriteAllText(file, source.ToJsonString());
        var imported = new CandidatePolicyImportService(loader, authoring).ImportFile(draft, "CMP-WIN-001", file, "CMP - Candidate - Windows");
        Assert.Single(imported.Unresolved); Assert.ThrowsAny<ToolkitException>(() => authoring.Validate(imported));
    }
    [Fact]public void Unresolved_reference_cannot_hide_unsupported_settings_or_embedded_secrets_in_a_saved_draft()
    {
        using var root = new TempRoot();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.10.15.json"));
        root.WriteStandard("base.json", File.ReadAllText(path)); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance); var authoring = new CandidateAuthoringService(loader);
        var draft = loader.Create("base.json", "Refused import"); var source = loader.Load(draft).FindControl("CFG-WIN-005")!.Payload!.DeepClone();
        source["settings"] = new JsonArray(new JsonObject { ["settingInstance"] = new JsonObject {
            ["@odata.type"] = "#microsoft.graph.deviceManagementConfigurationSimpleSettingInstance", ["settingDefinitionId"] = "synthetic_setting",
            ["simpleSettingValue"] = new JsonObject { ["value"] = TestData.Office, ["secretValue"] = "synthetic-not-for-drafts" } } });
        var file = Path.Combine(root.Paths.ReportsDirectory, "source.json"); File.WriteAllText(file, source.ToJsonString());
        Assert.ThrowsAny<ToolkitException>(() => new CandidatePolicyImportService(loader, authoring).ImportFile(draft, "CFG-WIN-005", file, "CFG - Candidate - Windows"));
        Assert.Empty(draft.Changes); Assert.Empty(draft.Imports);
    }
    [Fact]public void Unresolved_reference_cannot_hide_unblocked_macOS_in_an_import_draft()
    {
        using var root = new TempRoot();
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.10.15.json"));
        root.WriteStandard("base.json", File.ReadAllText(path)); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance); var authoring = new CandidateAuthoringService(loader);
        var draft = loader.Create("base.json", "Refused enrolment import"); var source = loader.Load(draft).FindControl("ENR-002")!.Payload!.DeepClone();
        source["macOSRestriction"]!["platformBlocked"] = false;
        var file = Path.Combine(root.Paths.ReportsDirectory, "source.json"); File.WriteAllText(file, source.ToJsonString());
        Assert.ThrowsAny<ToolkitException>(() => new CandidatePolicyImportService(loader, authoring).ImportFile(draft, "ENR-002", file, "ENR - " + TestData.Office));
    }
}
