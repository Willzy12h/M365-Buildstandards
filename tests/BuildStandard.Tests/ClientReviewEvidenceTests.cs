using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Workflow;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ClientReviewEvidenceTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly EvidenceStore _store;
    public ClientReviewEvidenceTests() => _store = new(_root.Paths, NullLog.Instance);
    public void Dispose() => _root.Dispose();
    private string Put(string path, string text)
    {
        var file = Path.Combine(_store.TenantDirectory(TestData.TenantA), path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); return file;
    }
    private AssessmentResult Assessment(string at = "2026-10-18T10:00:00Z") => new()
    { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, AssessedAt = at, Release = "test.1" };

    [Fact]
    public void Missing_history_is_distinguished_from_read_failures_and_creates_nothing()
    {
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Sources); Assert.Empty(history.Diagnostics); Assert.Empty(history.Assessments);
        Assert.True(history.SourceSetComplete);
        Assert.False(Directory.Exists(_store.TenantDirectory(TestData.TenantA)));
    }

    [Theory]
    [InlineData("assessments")]
    [InlineData("runs")]
    [InlineData("jobs")]
    [InlineData("reviewed-changes")]
    [InlineData("entra-laps")]
    public void An_occupied_history_directory_is_unknown_not_empty_success(string section)
    {
        var file = Put(section, "occupied synthetic path");
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Contains(history.Diagnostics, d => d.Section == section);
        Assert.False(history.SourceSetComplete); Assert.Equal("occupied synthetic path", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("{\"id\":null,\"tenantId\":null}")]
    public void Invalid_records_remain_visible_and_unchanged(string text)
    {
        var file = Put("assessments/invalid.json", text);
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Assessments); Assert.Single(history.Diagnostics);
        Assert.Single(history.Sources); Assert.True(history.SourceSetComplete);
        Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void Another_tenants_record_is_not_admitted_alongside_healthy_evidence()
    {
        var good = Assessment(); _store.SaveAssessment(good);
        var wrong = Assessment(); wrong.TenantId = TestData.TenantB;
        Put($"assessments/other-{wrong.Id}.json", ToolkitJson.Serialize(wrong));
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Equal(good.Id, Assert.Single(history.Assessments).Result.Id); Assert.Single(history.Diagnostics);
    }

    [Fact]
    public void Raw_citations_and_canonical_assessment_identity_remain_distinct()
    {
        var record = Assessment(); var file = _store.SaveAssessment(record);
        File.AppendAllText(file, "\n "); var bytes = File.ReadAllBytes(file);
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Equal(CanonicalJson.Sha256Hex(bytes), Assert.Single(history.Sources).Sha256);
        Assert.Equal(_store.LoadAssessment(TestData.TenantA, record.Id)!.Sha256, Assert.Single(history.Assessments).Sha256);
        Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void Changing_a_returned_assessment_does_not_mutate_captured_input()
    {
        var original = Assessment(); _store.SaveAssessment(original);
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        history.Assessments.Single().Result.Release = "changed";
        Assert.Equal("test.1", history.Assessments.Single().Result.Release);
    }

    [Fact]
    public void Digest_failed_runs_are_diagnostic_and_not_admitted()
    {
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, StartedAt = "2026-10-18T10:00:00Z" };
        _store.SaveRun(run); var file = _store.RunFile(run);
        File.WriteAllText(file, File.ReadAllText(file).Replace("Running", "Completed", StringComparison.Ordinal));
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Runs); Assert.Contains(history.Diagnostics, d => d.Section == "runs");
    }

    [Fact]
    public void Invalid_timestamps_do_not_become_latest_evidence()
    {
        _store.SaveAssessment(Assessment("not a timestamp"));
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Assessments); Assert.Single(history.Diagnostics);
    }

    [Fact]
    public void A_record_over_eight_MiB_is_refused_before_json_materialisation()
    {
        Put("assessments/oversized.json", new string(' ', 8 * 1024 * 1024 + 1));
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.False(history.SourceSetComplete); Assert.Empty(history.Assessments);
        Assert.Contains(history.Diagnostics, d => d.Problem.Contains("8 MiB", StringComparison.Ordinal));
    }

    [Fact]
    public void More_than_five_hundred_files_is_incomplete_never_a_complete_smaller_inventory()
    {
        for (var i = 0; i < 501; i++) Put($"assessments/{i:D4}.json", "null");
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.False(history.SourceSetComplete); Assert.InRange(history.Sources.Count, 0, 500);
        Assert.Contains(history.Diagnostics, d => d.Problem.Contains("500", StringComparison.Ordinal));
    }

    [Fact]
    public void Cancellation_does_not_repair_or_create_evidence()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => _store.LoadClientReviewEvidence(TestData.TenantA, stop.Token));
        Assert.False(Directory.Exists(_store.TenantDirectory(TestData.TenantA)));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("removed")]
    [InlineData("added")]
    public void Export_admission_refuses_a_changed_relevant_file_set_or_bytes(string action)
    {
        var file = _store.SaveAssessment(Assessment()); var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        if (action == "changed") File.AppendAllText(file, " ");
        else if (action == "removed") File.Delete(file);
        else _store.SaveAssessment(Assessment());
        Assert.Throws<ConfigurationException>(() => _store.RequireClientReviewCurrent(history));
    }

    [Fact]
    public void Unchanged_corrupt_source_can_be_reported_as_unknown_with_exact_citations()
    {
        Put("runs/broken.json", "broken"); var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        _store.RequireClientReviewCurrent(history);
        Assert.Single(history.Diagnostics); Assert.Single(history.Sources);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData(TestData.TenantA + "\n")]
    public void Tenant_identity_cannot_escape_its_exact_directory(string tenant) =>
        Assert.Throws<ConfigurationException>(() => _store.LoadClientReviewEvidence(tenant));
    [Fact]
    public void Workflow_dependencies_are_captured_and_a_missing_attached_record_is_not_invented()
    {
        var standard = TestData.Standard(); standard.IntegrityDigest = CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(TestData.StandardJson));
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        var capture = TestData.Snapshot(standard); _store.SaveSnapshot(capture);
        var clock = new FixedClock(); var workflow = new JobWorkflow(_store, clock);
        var job = workflow.OpenJob(TestData.Profile(), standard, JobIntention.NewBuild, "Owner", "", "engineer@test.example", capture.Id);
        var observation = workflow.Record(TestData.TenantA, job.Id, TestData.Profile(), standard,
            new ObservationRequest { SemanticId="CA-001", ControlId="CA-001", Status=ObservationStatus.Pass,
                Reason="Synthetic portal observation", ReviewDueAt=clock.UtcNow.AddDays(30), SnapshotId=capture.Id }, "engineer@test.example");
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Diagnostics);
        Assert.Equal(job.Id, Assert.Single(history.Records<TenantJob>("jobs")).Id);
        Assert.Equal(observation.Id, Assert.Single(history.Records<TenantObservation>("observations")).Id);
        Assert.Equal(capture.Id, Assert.Single(history.Records<TenantSnapshot>("snapshots")).Id);
        Assert.Contains(history.Sources, f => f.Section == "catalogue");
        File.Delete(Path.Combine(_store.TenantDirectory(TestData.TenantA), "observations", observation.Id + ".json"));
        Assert.Throws<ConfigurationException>(() => _store.RequireClientReviewCurrent(history));
    }

    [Theory]
    [InlineData("recovery")]
    [InlineData("reviewed-changes")]
    [InlineData("entra-laps")]
    public void Independent_write_histories_are_admitted_only_with_their_original_intact_digest(string section)
    {
        var id = Guid.NewGuid().ToString(); var at = DateTimeOffset.Parse("2026-10-18T10:00:00Z");
        if (section == "recovery") _store.SaveRecoveryRun(new RecoveryRun { Id=id, TenantId=TestData.TenantA, StartedAt=at.ToString("O"), WriteAcceptance=WriteAcceptance.Unknown });
        if (section == "reviewed-changes") _store.SaveReviewedChangeRun(new ReviewedChangeRun { Id=id, TenantId=TestData.TenantA, StartedAt=at, WriteAcceptance=WriteAcceptance.Unknown });
        if (section == "entra-laps") _store.SaveEntraLapsRun(new EntraLapsRun { Id=id, TenantId=TestData.TenantA, StartedAt=at, WriteAcceptance=WriteAcceptance.Unknown });
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Diagnostics); Assert.Single(history.Sources);
        var file = Path.Combine(_store.TenantDirectory(TestData.TenantA), section, "run-" + id + ".json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("Unknown", "Not attempted", StringComparison.Ordinal));
        Assert.Contains(_store.LoadClientReviewEvidence(TestData.TenantA).Diagnostics, d => d.Section == section);
    }

    [Fact]
    public void Manual_checks_and_deviations_preserve_their_explicit_review_details()
    {
        _store.SaveDeviations(TestData.TenantA, [new Deviation { Id=Guid.NewGuid().ToString(), TenantId=TestData.TenantA,
            ControlId="CA-001", Owner="Engineer", Reason="Synthetic exception", ApprovedBy="Approver", ReviewBy="2026-10-01" }]);
        _store.SaveManualChecks(new ManualCheckRegister { TenantId=TestData.TenantA, Checks = new() { ["CA-001"] = new ManualCheck { ControlId="CA-001", Status="Unknown" } } });
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Diagnostics); Assert.Equal(2, history.Sources.Count);
    }

    [Fact]
    public void Duplicate_ids_cannot_be_selected_by_arbitrary_file_order()
    {
        var a = Assessment(); var file = _store.SaveAssessment(a);
        File.Copy(file, Path.Combine(Path.GetDirectoryName(file)!, "other-" + a.Id + ".json"));
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Assessments); Assert.Contains(history.Diagnostics, d => d.Problem.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void A_symlink_to_another_clients_record_is_never_followed()
    {
        var other = Assessment(); other.TenantId=TestData.TenantB; var target = _store.SaveAssessment(other);
        var directory = Path.Combine(_store.TenantDirectory(TestData.TenantA), "assessments"); Directory.CreateDirectory(directory);
        var link = Path.Combine(directory, "linked-" + other.Id + ".json"); File.CreateSymbolicLink(link, target);
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.False(history.SourceSetComplete); Assert.Empty(history.Assessments); Assert.Empty(history.Sources); Assert.Single(history.Diagnostics);
    }

    [Fact]
    public void A_directory_named_as_a_json_record_is_a_visible_read_failure()
    {
        Directory.CreateDirectory(Path.Combine(_store.TenantDirectory(TestData.TenantA), "runs", "blocked.json"));
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.False(history.SourceSetComplete); Assert.Single(history.Diagnostics);
    }

    [Fact]
    public void A_renamed_unsupported_workflow_schema_cannot_be_admitted()
    {
        var id=Guid.NewGuid().ToString();
        Put("jobs/"+id+".json", ToolkitJson.Serialize(new TenantJob { Id=id,TenantId=TestData.TenantA,SchemaVersion=99 }));
        var history = _store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Records<TenantJob>("jobs")); Assert.Single(history.Diagnostics);
    }

    [Theory]
    [InlineData("id", "42")]
    [InlineData("tenantId", "[]")]
    public void A_wrong_property_type_is_a_diagnostic_not_a_failed_client_scan(string property, string value)
    {
        var assessment=Assessment();var node=ToolkitJson.ParseObject(ToolkitJson.Serialize(assessment));node[property]=ToolkitJson.ParseNode(value);
        Put("assessments/"+assessment.Id+".json",node.ToJsonString());
        var history=_store.LoadClientReviewEvidence(TestData.TenantA);Assert.Single(history.Diagnostics);Assert.Empty(history.Assessments);
    }

    [Theory]
    [InlineData("instanceKey")]
    [InlineData("observedObjectIds")]
    [InlineData("evidence")]
    public void Null_workflow_fields_are_diagnostics_even_when_the_digest_matches(string property)
    {
        var value=new TenantObservation{Id=Guid.NewGuid().ToString(),TenantId=TestData.TenantA};
        var node=ToolkitJson.ParseObject(ToolkitJson.Serialize(value));node[property]=null;
        value=ToolkitJson.Deserialize<TenantObservation>(node.ToJsonString());node["integrityDigest"]=EvidenceStore.RecoveryDigest(value);
        Put("observations/"+value.Id+".json",node.ToJsonString());
        var history=_store.LoadClientReviewEvidence(TestData.TenantA);Assert.Single(history.Diagnostics);Assert.Empty(history.Records<TenantObservation>("observations"));
    }

    [Fact]
    public void An_added_json_directory_invalidates_a_previously_empty_export()
    {
        var history=_store.LoadClientReviewEvidence(TestData.TenantA);
        Directory.CreateDirectory(Path.Combine(_store.TenantDirectory(TestData.TenantA),"runs","blocked.JSON"));
        Assert.Throws<ConfigurationException>(()=>_store.RequireClientReviewCurrent(history));
    }

    [Fact]
    public void A_changed_catalogue_invalidates_the_review_without_reading_new_bytes_into_it()
    {
        var standard=TestData.Standard();standard.IntegrityDigest=CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(TestData.StandardJson));
        _root.WriteStandard("test.json",TestData.StandardJson);_root.WriteManifest();
        new JobWorkflow(_store,new FixedClock()).OpenJob(TestData.Profile(),standard,JobIntention.NewBuild,"Owner","","engineer@test.example");
        var history=_store.LoadClientReviewEvidence(TestData.TenantA);Assert.Empty(history.Diagnostics);
        File.AppendAllText(Path.Combine(_root.Paths.StandardsDirectory,"test.json")," ");
        Assert.Single(history.Catalogues);Assert.Empty(_store.LoadClientReviewEvidence(TestData.TenantA).Catalogues);
        Assert.Throws<ConfigurationException>(()=>_store.RequireClientReviewCurrent(history));
    }

    [Fact]
    public void A_corrupt_standard_is_diagnostic_even_with_a_matching_manifest()
    {
        const string json="{\"schemaVersion\":5,\"release\":\"test.1\",\"collections\":null}";
        var standard=TestData.Standard();standard.IntegrityDigest=CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(json));
        _root.WriteStandard("test.json",json);_root.WriteManifest();
        new JobWorkflow(_store,new FixedClock()).OpenJob(TestData.Profile(),standard,JobIntention.NewBuild,"Owner","","engineer@test.example");
        var history=_store.LoadClientReviewEvidence(TestData.TenantA);Assert.Empty(history.Catalogues);Assert.Contains(history.Diagnostics,d=>d.Section=="catalogue");
    }
    [Theory]
    [InlineData("controls")]
    [InlineData("collections")]
    public void Null_catalogue_entries_never_abort_the_client_scan(string section)
    {
        var node=ToolkitJson.ParseObject(TestData.StandardJson);
        if(section=="controls") node["controls"]=new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        else node["collections"]!["conditionalAccess"]=null;
        var json=node.ToJsonString();var standard=TestData.Standard();standard.IntegrityDigest=CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(json));
        _root.WriteStandard("test.json",json);_root.WriteManifest();
        new JobWorkflow(_store,new FixedClock()).OpenJob(TestData.Profile(),standard,JobIntention.NewBuild,"Owner","","engineer@test.example");
        var history=_store.LoadClientReviewEvidence(TestData.TenantA);Assert.Empty(history.Catalogues);Assert.Contains(history.Diagnostics,d=>d.Section=="catalogue");
    }
    [Fact]
    public void Case_aliased_json_properties_are_not_silently_reinterpreted()
    {
        var a=Assessment();var json=ToolkitJson.Serialize(a).TrimEnd();json=json[..^1]+",\"AssessedAt\":\"2026-09-12T10:00:00Z\"}";
        Put("assessments/"+a.Id+".json",json);var history=_store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Assessments);Assert.Single(history.Diagnostics);
    }

    [Theory] [InlineData("signals")] [InlineData("caveats")]
    public void Null_nested_equivalence_entries_do_not_abort_the_entire_client_review(string member)
    {
        var node=ToolkitJson.ParseObject(TestData.StandardJson);
        var equivalence=new System.Text.Json.Nodes.JsonObject {
            ["collection"]="conditionalAccess",["note"]="Synthetic equivalence",
            ["signals"]=new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject {
                ["key"]="state",["label"]="State",["path"]="state",["operator"]="Present",["required"]=true }),
            ["caveats"]=new System.Text.Json.Nodes.JsonArray() };
        equivalence[member]=new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        node["controls"]![0]!["equivalence"]=equivalence;
        var json=node.ToJsonString();var standard=TestData.Standard();standard.IntegrityDigest=CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(json));
        _root.WriteStandard("test.json",json);_root.WriteManifest();
        new JobWorkflow(_store,new FixedClock()).OpenJob(TestData.Profile(),standard,JobIntention.NewBuild,"Owner","","engineer@test.example");
        var history=_store.LoadClientReviewEvidence(TestData.TenantA);
        Assert.Empty(history.Catalogues);Assert.Contains(history.Diagnostics,d=>d.Section=="catalogue");
        Assert.Empty(_store.LoadClientReviewEvidence(TestData.TenantB).Diagnostics);
    }

}
