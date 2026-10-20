using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateEditingServiceTests
{
    private static (CandidateEditingService Editor, BuildStandard.Core.Models.StandardCandidateDraft Draft) Setup(TempRoot root)
    {
        root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance);
        return (new(loader, new(loader)), loader.Create("test.json", "Editor regression"));
    }
    [Fact]public void Repeated_edits_keep_baseline_expectation_without_mutating_the_callers_draft()
    {
        using var root = new TempRoot(); var (editor, original) = Setup(root); var before = CandidateDraftCodec.Serialize(original);
        var first = editor.Set(original, "CA-001", "/payload/displayName", "\"CA - First\"");
        var next = editor.Set(first, "CA-001", "/payload/displayName", "\"CA - Second\"");
        Assert.Single(next.Changes); Assert.Equal("CA - Second", next.Changes[0].Proposed!.GetValue<string>());
        Assert.Equal(first.Changes[0].ExpectedOriginal!.ToJsonString(), next.Changes[0].ExpectedOriginal!.ToJsonString());
        Assert.Equal("CA - First", first.Changes[0].Proposed!.GetValue<string>());
        Assert.Equal(before, CandidateDraftCodec.Serialize(original)); Assert.NotEqual(first.ContentDigest, next.ContentDigest);
    }
    [Fact]public void Removing_an_edit_and_restoring_original_value_produce_the_original_identity()
    {
        using var root = new TempRoot(); var (editor, original) = Setup(root);
        var changed = editor.Set(original, "CA-001", "/payload/displayName", "\"CA - Proposed\"");
        var removed = editor.Remove(changed, "CA-001", "/payload/displayName");
        var restored = editor.Set(changed, "CA-001", "/payload/displayName", changed.Changes[0].ExpectedOriginal!.ToJsonString());
        Assert.Equal(original.ContentDigest, removed.ContentDigest); Assert.Equal(original.ContentDigest, restored.ContentDigest);
    }
    [Fact]public void Field_listing_shows_baseline_and_proposal_with_atomic_arrays()
    {
        using var root = new TempRoot(); var (editor, original) = Setup(root);
        var changed = editor.Set(original, "CA-001", "/payload/displayName", "\"CA - Proposed\"");
        var fields = editor.Fields(changed, "CA-001"); var name = fields.Single(f => f.Path == "/payload/displayName");
        Assert.NotEqual(name.OriginalJson, name.ProposedJson); Assert.Equal("String", name.ValueType);
        Assert.DoesNotContain(fields, f => f.Path.EndsWith("/0", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Path == "/payload/grantControls/builtInControls" && f.ValueType == "Array");
    }
    [Theory][InlineData("/payload/state", "\"enabled\"")][InlineData("/collection", "\"users\"")]
    public void Editing_cannot_bypass_existing_candidate_admission(string path, string json)
    {
        using var root = new TempRoot(); var (editor, original) = Setup(root); var before = CandidateDraftCodec.Serialize(original);
        Assert.ThrowsAny<ToolkitException>(() => editor.Set(original, "CA-001", path, json));
        Assert.Equal(before, CandidateDraftCodec.Serialize(original));
    }
    [Fact]public void Shared_parameter_default_is_presented_and_replaced_consistently_from_either_control()
    {
        using var root = new TempRoot(); var standard = TestData.Standard();
        standard.Parameters.Add(new() { Key = "minimumVersion", Type = "string", Label = "Minimum version", Default = JsonValue.Create("14"), ReviewedOn = "2026-10-01" });
        var first = standard.FindControl("CA-001")!; first.Payload!["description"] = "{{minimumVersion}}";
        var second = BuildStandard.Core.Json.ToolkitJson.Deserialize<BuildStandard.Core.Models.ControlDefinition>(BuildStandard.Core.Json.ToolkitJson.Serialize(first));
        second.Id = "CA-002"; standard.Controls.Add(second);
        root.WriteStandard("test.json", BuildStandard.Core.Json.ToolkitJson.Serialize(standard)); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance); var editor = new CandidateEditingService(loader, new(loader));
        var draft = loader.Create("test.json", "Shared default");
        var changed = editor.Set(draft, "CA-001", "/parameters/minimumVersion/default", "\"15\"");
        Assert.Equal("\"15\"", editor.Fields(changed, "CA-002").Single(f => f.Path == "/parameters/minimumVersion/default").ProposedJson);
        var replaced = editor.Set(changed, "CA-002", "/parameters/minimumVersion/default", "\"16\"");
        Assert.Single(replaced.Changes); Assert.Equal("14", replaced.Changes[0].ExpectedOriginal!.GetValue<string>());
        var removed = editor.Remove(replaced, "CA-001", "/parameters/minimumVersion/default"); Assert.Empty(removed.Changes);
    }
    [Fact]public void Field_projection_includes_a_valid_parent_object_overlay()
    {
        using var root = new TempRoot();
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.10.15.json"));
        root.WriteStandard("base.json", File.ReadAllText(file)); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance); var service = new CandidateAuthoringService(loader);
        var draft = loader.Create("base.json", "Parent overlay");
        var original = loader.Load(draft).FindControl("ENR-002")!.Payload!["windowsRestriction"]!.DeepClone();
        var proposed = original.DeepClone(); proposed["personalDeviceEnrollmentBlocked"] = false;
        draft.Changes.Add(new("ENR-002", "/payload/windowsRestriction", original, proposed)); CandidateDraftCodec.Seal(draft);
        _ = service.Validate(draft);
        var field = new CandidateEditingService(loader, service).Fields(draft, "ENR-002").Single(f => f.Path == "/payload/windowsRestriction/personalDeviceEnrollmentBlocked");
        Assert.Equal("true", field.OriginalJson); Assert.Equal("false", field.ProposedJson);
    }
    [Fact]public void Duplicate_input_properties_are_refused_before_any_edit()
    {
        using var root = new TempRoot(); var (editor, original) = Setup(root);
        Assert.ThrowsAny<ToolkitException>(() => editor.Set(original, "CA-001", "/payload/displayName", "{\"value\":1,\"Value\":2}"));
        Assert.Empty(original.Changes);
    }
}
