using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateAuthoringWorkflowTests
{
    private static StandardCandidateDraft Draft(TempRoot root)
    {
        root.WriteStandard("base.json", TestData.StandardJson); root.WriteManifest();
        return new CandidateBaselineLoader(root.Paths, NullLog.Instance).Create("base.json", "Local editor");
    }
    private static PresentationContextKey Context(long generation = 1) => new(null, null, null, "", "", "baseline", "", generation);
    [Fact]public async Task Unsaved_draft_cannot_be_replaced_and_failed_edit_preserves_it()
    {
        using var root = new TempRoot(); var draft = Draft(root); var workflow = new CandidateAuthoringWorkflow(() => Context());
        await workflow.OpenAsync(_ => draft); Assert.True(workflow.HasUnsavedChanges);
        await Assert.ThrowsAnyAsync<ToolkitException>(() => workflow.OpenAsync(_ => draft));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.EditAsync((_, _) => throw new InvalidOperationException("Bad proposed input")));
        Assert.Equal(draft.ContentDigest, workflow.Draft!.ContentDigest);
        workflow.MarkSaved(draft.ContentDigest); Assert.False(workflow.HasUnsavedChanges);
        await workflow.OpenAsync(_ => draft); Assert.True(workflow.HasUnsavedChanges);
    }
    [Fact]public async Task Late_open_after_context_change_is_not_admitted()
    {
        using var root = new TempRoot(); var draft = Draft(root); var current = Context(); var workflow = new CandidateAuthoringWorkflow(() => current);
        await Assert.ThrowsAnyAsync<ToolkitException>(() => workflow.OpenAsync(_ => { current = Context(3); return draft; }));
        Assert.Null(workflow.Draft);
    }
    [Fact]public async Task Edit_invalidates_validation_and_never_leaks_mutable_draft_references()
    {
        using var root = new TempRoot(); var draft = Draft(root); var workflow = new CandidateAuthoringWorkflow(() => Context());
        var service = new CandidateAuthoringService(new(root.Paths, NullLog.Instance));
        await workflow.OpenAsync(_ => draft); await workflow.ValidateAsync((d, _) => CandidateValidationTicket.Create(service, d)); Assert.NotNull(workflow.Validation);
        var exposed = workflow.Draft!; exposed.Title = "Must not mutate internal state";
        Assert.Equal(draft.Title, workflow.Draft!.Title);
        await workflow.EditAsync((d, _) => { d.Title = "New title"; CandidateDraftCodec.Seal(d); return d; });
        Assert.Null(workflow.Validation); Assert.Equal("New title", workflow.Draft!.Title);
        Assert.Equal("Local editor", draft.Title);
    }
    [Fact]public async Task Context_change_preserves_unsaved_draft_but_revokes_comparison_and_validation()
    {
        using var root = new TempRoot(); var draft = Draft(root); var current = Context(); var workflow = new CandidateAuthoringWorkflow(() => current);
        var service = new CandidateAuthoringService(new(root.Paths, NullLog.Instance));
        await workflow.OpenAsync(_ => draft); await workflow.ValidateAsync((d, _) => CandidateValidationTicket.Create(service, d));
        current = Context(2); workflow.Refresh(); Assert.False(workflow.ContextCurrent); Assert.Null(workflow.Validation);
        Assert.True(workflow.HasUnsavedChanges); Assert.Equal(draft.ContentDigest, workflow.Draft!.ContentDigest);
        await Assert.ThrowsAnyAsync<ToolkitException>(() => workflow.EditAsync((d, _) => d));
    }
    [Fact]public async Task Cancellation_or_discard_during_computation_cannot_restore_a_draft()
    {
        using var root = new TempRoot(); var draft = Draft(root); var workflow = new CandidateAuthoringWorkflow(() => Context());
        using var ct = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.OpenAsync(_ => { ct.Cancel(); return draft; }, ct.Token)); Assert.Null(workflow.Draft);
        await Assert.ThrowsAnyAsync<ToolkitException>(() => workflow.OpenAsync(_ => { workflow.Discard(); return draft; })); Assert.Null(workflow.Draft);
    }
    [Fact]public async Task Validation_for_a_mutated_callback_copy_cannot_validate_the_visible_draft()
    {
        using var root = new TempRoot(); var draft = Draft(root); var workflow = new CandidateAuthoringWorkflow(() => Context());
        var service = new CandidateAuthoringService(new(root.Paths, NullLog.Instance));
        await workflow.OpenAsync(_ => draft);
        await Assert.ThrowsAsync<IntegrityException>(() => workflow.ValidateAsync((d, _) =>
        { d.Title = "Different candidate"; CandidateDraftCodec.Seal(d); return CandidateValidationTicket.Create(service, d); }));
        Assert.Null(workflow.Validation); Assert.Equal(draft.ContentDigest, workflow.Draft!.ContentDigest);
    }
    [Fact]public async Task Stale_save_notification_does_not_mark_a_newer_draft_saved()
    {
        using var root = new TempRoot(); var draft = Draft(root); var workflow = new CandidateAuthoringWorkflow(() => Context());
        await workflow.OpenAsync(_ => draft); await workflow.EditAsync((d, _) => { d.Title = "Changed"; CandidateDraftCodec.Seal(d); return d; });
        Assert.ThrowsAny<ToolkitException>(() => workflow.MarkSaved(draft.ContentDigest)); Assert.True(workflow.HasUnsavedChanges);
    }
}
