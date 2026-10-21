using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateAuthoringServiceTests
{
    private static (CandidateBaselineLoader Loader,StandardCandidateDraft Draft) Setup(TempRoot root)
    {
        var standard=TestData.Standard();standard.Parameters.Add(new(){Key="minimumVersion",Type="string",Label="Minimum version",Default=JsonValue.Create("14"),ReviewedOn="2026-10-01"});
        root.WriteStandard("test.json",ToolkitJson.Serialize(standard));root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);return(loader,loader.Create("test.json","Synthetic candidate"));
    }
    private static void Change(StandardCandidateDraft draft,string path,JsonNode? old,JsonNode? value)
    {draft.Changes.Add(new("CA-001",path,old,value));CandidateDraftCodec.Seal(draft);}
    private static (CandidateBaselineLoader Loader, StandardCandidateDraft Draft) Published(TempRoot root)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.10.15.json"));
        root.WriteStandard("base.json", File.ReadAllText(path)); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance);
        return (loader, loader.Create("base.json", "Offline regression"));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Authoring_cannot_unblock_macOS_by_leaf_or_parent_overlay(bool parent)
    {
        using var root = new TempRoot(); var (loader, draft) = Published(root);
        var payload = loader.Load(draft).FindControl("ENR-002")!.Payload!;
        var path = parent ? "/payload/macOSRestriction" : "/payload/macOSRestriction/platformBlocked";
        var original = parent ? payload["macOSRestriction"]!.DeepClone() : payload["macOSRestriction"]!["platformBlocked"]!.DeepClone();
        var proposed = original.DeepClone();
        if (parent) proposed["platformBlocked"] = false; else proposed = JsonValue.Create(false)!;
        draft.Changes.Add(new("ENR-002", path, original, proposed)); CandidateDraftCodec.Seal(draft);
        Assert.ThrowsAny<ToolkitException>(() => new CandidateAuthoringService(loader).Validate(draft));
    }
    [Theory][InlineData("emergencyAccountIds")][InlineData("oneDriveSettings")]
    public void Structured_OMA_settings_cannot_be_replaced_by_an_unrelated_array_parameter(string parameter)
    {
        using var root = new TempRoot(); var (loader, draft) = Published(root);
        var original = loader.Load(draft).FindControl("CFG-WIN-002")!.Payload!["omaSettings"]!.DeepClone();
        draft.Changes.Add(new("CFG-WIN-002", "/payload/omaSettings", original, JsonValue.Create("{{" + parameter + "}}"))); CandidateDraftCodec.Seal(draft);
        Assert.ThrowsAny<ToolkitException>(() => new CandidateAuthoringService(loader).Validate(draft));
    }
    [Theory][InlineData("CFG-WIN-005")][InlineData("APP-WIN-002")][InlineData("APP-WIN-003")]
    public void Description_edit_preserves_existing_supported_parameterised_recipe(string id)
    {
        using var root = new TempRoot(); var (loader, draft) = Published(root);
        var original = loader.Load(draft).FindControl(id)!.Payload!;
        draft.Changes.Add(new(id, "/payload/description", original["description"]!.DeepClone(), JsonValue.Create("Reviewed local description"))); CandidateDraftCodec.Seal(draft);
        var result = new CandidateAuthoringService(loader).Validate(draft).FindControl(id)!.Payload!;
        Assert.Equal("Reviewed local description", result["description"]!.GetValue<string>());
        var expected = original.DeepClone(); expected["description"] = "Reviewed local description";
        Assert.Equal(CanonicalJson.Serialize(expected), CanonicalJson.Serialize(result));
    }
    [Theory][InlineData("CFG-WIN-005")][InlineData("APP-WIN-003")]
    public void Authoring_admission_never_weakens_resolved_write_guards(string id)
    {
        using var root = new TempRoot(); var (loader, draft) = Published(root);
        var baseline = loader.Load(draft); var control = baseline.FindControl(id)!;
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(baseline.FindCollection(control.Collection)!, control.Payload!));
        var field = id == "CFG-WIN-005" ? "settings" : "packageIdentifier";
        draft.Changes.Add(new(id, "/payload/" + field, control.Payload![field]!.DeepClone(), JsonValue.Create("{{emergencyAccountIds}}")));
        CandidateDraftCodec.Seal(draft);
        Assert.ThrowsAny<ToolkitException>(() => new CandidateAuthoringService(loader).Validate(draft));
    }
    [Fact]public void Valid_name_edit_returns_deterministic_inert_candidate_without_altering_base_or_draft()
    {
        using var root=new TempRoot();var(loader,draft)=Setup(root);var baseline=loader.Load(draft);var old=baseline.FindControl("CA-001")!.Payload!["displayName"];
        Change(draft,"/payload/displayName",old!.DeepClone(),JsonValue.Create("CA - Synthetic MFA"));var before=CandidateDraftCodec.Serialize(draft);var service=new CandidateAuthoringService(loader);var candidate=service.Validate(draft);
        Assert.Equal("CA - Synthetic MFA",candidate.FindControl("CA-001")!.Payload!["displayName"]!.GetValue<string>());Assert.Equal("disabled",candidate.FindControl("CA-001")!.Payload!["state"]!.GetValue<string>());
        Assert.Equal("Candidate",candidate.Status);Assert.StartsWith("candidate-",candidate.Release);Assert.Equal("",candidate.IntegrityDigest);Assert.Equal("",candidate.SourceFileName);Assert.Equal("",candidate.PublishedOn);
        Assert.Equal(candidate.Release,service.Validate(draft).Release);Assert.Equal(before,CandidateDraftCodec.Serialize(draft));Assert.Equal(old!.ToJsonString(),loader.Load(draft).FindControl("CA-001")!.Payload!["displayName"]!.ToJsonString());
    }
    [Fact]public void Changed_default_has_no_inherited_vendor_review_date()
    {
        using var root=new TempRoot();var(loader,draft)=Setup(root);Change(draft,"/parameters/minimumVersion/default",JsonValue.Create("14"),JsonValue.Create("15"));
        var candidate=new CandidateAuthoringService(loader).Validate(draft);var parameter=candidate.Parameters.Single(p=>p.Key=="minimumVersion");Assert.Equal("15",parameter.Default!.GetValue<string>());Assert.Equal("",parameter.ReviewedOn);Assert.Equal("2026-10-01",loader.Load(draft).Parameters.Single(p=>p.Key=="minimumVersion").ReviewedOn);
    }
    [Theory][InlineData("state")][InlineData("targets")][InlineData("metadata")][InlineData("type")][InlineData("stale")][InlineData("foreign")][InlineData("template")][InlineData("identityDefault")][InlineData("unknownControl")]
    public void Unsupported_or_unsafe_changes_cannot_validate(string fault)
    {
        using var root=new TempRoot();var(loader,draft)=Setup(root);var control=loader.Load(draft).FindControl("CA-001")!;
        var path="/payload/displayName";JsonNode? old=control.Payload!["displayName"]!.DeepClone();JsonNode? value=JsonValue.Create("CA - Changed");
        switch(fault){case "state":path="/payload/state";old=JsonValue.Create("disabled");value=JsonValue.Create("enabled");break;case "targets":path="/payload/conditions/users/includeUsers";old=JsonNode.Parse("[\"All\"]");value=new JsonArray();break;case "metadata":path="/licence/servicePlans";old=new JsonArray("AAD_PREMIUM");value=new JsonArray();break;case "type":value=JsonValue.Create(9);break;case "stale":old=JsonValue.Create("Wrong");break;case "foreign":value=JsonValue.Create("CA - 11111111-1111-4111-8111-111111111111");break;case "template":value=JsonValue.Create("{{notDeclared}}");break;case "identityDefault":path="/parameters/emergencyAccountIds/default";old=null;value=new JsonArray("11111111-1111-4111-8111-111111111111");break;case "unknownControl":break;}
        Change(draft,path,old,value);if(fault=="unknownControl"){draft.Changes[0]=draft.Changes[0] with{ControlId="CA-999"};CandidateDraftCodec.Seal(draft);}
        Assert.ThrowsAny<ToolkitException>(()=>new CandidateAuthoringService(loader).Validate(draft));
    }
    [Theory][InlineData("{{minimumVersion}}")][InlineData("prefix {{minimumVersion}}")]
    public void A_default_cannot_contain_a_template_expression(string value)
    {
        using var root=new TempRoot();var(loader,draft)=Setup(root);Change(draft,"/parameters/minimumVersion/default",JsonValue.Create("14"),JsonValue.Create(value));
        Assert.ThrowsAny<ToolkitException>(()=>new CandidateAuthoringService(loader).Validate(draft));
    }
    [Theory][InlineData("CA-999","unknown")][InlineData("CA-001","unknown")]
    public void Imports_must_name_a_registered_importable_control_and_type(string control,string type)
    {
        using var root=new TempRoot();var(loader,draft)=Setup(root);draft.Imports.Add(new(new string('b',64),control,type));CandidateDraftCodec.Seal(draft);
        Assert.ThrowsAny<ToolkitException>(()=>new CandidateAuthoringService(loader).Validate(draft));
    }
    [Fact]public void Unresolved_source_draft_can_save_but_cannot_validate()
    {
        using var root=new TempRoot();var(loader,draft)=Setup(root);var hash=new string('b',64);draft.Imports.Add(new(hash,"CA-001","conditionalAccess"));draft.Unresolved.Add(new(hash,"CA-001","/payload/conditions","Foreign target"));CandidateDraftCodec.Seal(draft);
        Assert.NotEmpty(CandidateDraftCodec.Serialize(draft));Assert.ThrowsAny<ToolkitException>(()=>new CandidateAuthoringService(loader).Validate(draft));
    }
}
