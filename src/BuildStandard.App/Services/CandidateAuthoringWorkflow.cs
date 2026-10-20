using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards.Authoring;
namespace BuildStandard.App.Services;

/// <summary>Local authoring presentation admission; never authorises a tenant operation.</summary>
public sealed class CandidateAuthoringWorkflow(Func<PresentationContextKey> context)
{
    private StandardCandidateDraft? _draft;
    private CandidateValidationTicket? _validation;
    private PresentationContextKey? _binding;
    private string? _savedDigest;
    private long _generation;
    private bool _busy;
    public bool HasDraft => _draft is not null;
    public string DraftTitle => _draft?.Title ?? "";
    public string DraftDigest => _draft?.ContentDigest ?? "";
    public CandidateBaseline? Baseline => _draft?.Baseline;
    public StandardCandidateDraft? Draft => _draft is { } draft ? Copy(draft) : null;
    public CandidateValidationTicket? Validation => ContextCurrent ? _validation : null;
    public bool HasUnsavedChanges => _draft is { } draft && draft.ContentDigest != _savedDigest;
    public bool ContextCurrent => _binding is not null && _binding == context();

    public async Task OpenAsync(Func<CancellationToken, StandardCandidateDraft> load, CancellationToken ct = default)
    {
        if (HasUnsavedChanges) throw new ConfigurationException("Keep the current draft, save it, or explicitly discard it before opening another baseline or draft.");
        var result = await RunAsync(load, ct);
        _draft = Copy(result); _binding = context(); _savedDigest = null; _validation = null;
    }
    public async Task EditAsync(Func<StandardCandidateDraft, CancellationToken, StandardCandidateDraft> edit, CancellationToken ct = default)
    {
        var snapshot = RequireCurrent(); _validation = null;
        var result = await RunAsync(token => edit(snapshot, token), ct);
        _draft = Copy(result);
    }
    public async Task ValidateAsync(Func<StandardCandidateDraft, CancellationToken, CandidateValidationTicket> validate, CancellationToken ct = default)
    {
        var snapshot = RequireCurrent(); _validation = null;
        var expectedDraft = snapshot.ContentDigest; var expectedBaseline = snapshot.Baseline.FileSha256;
        var ticket = await RunAsync(token => validate(snapshot, token), ct);
        if (ticket.DraftDigest != expectedDraft || ticket.BaselineDigest != expectedBaseline
            || _draft?.ContentDigest != expectedDraft || _draft.Baseline.FileSha256 != expectedBaseline)
            throw new IntegrityException("Validation does not identify the reviewed draft and baseline.");
        _validation = ticket;
    }
    public void MarkSaved(string digest)
    {
        if (_draft is null || _draft.ContentDigest != digest) throw new IntegrityException("The saved file belongs to an earlier draft; current edits remain unsaved.");
        _savedDigest = digest;
    }
    public void Refresh()
    {
        if (_binding is not null && !ContextCurrent)
        {
            Interlocked.Increment(ref _generation); _binding = null; _validation = null;
        }
    }
    public void Discard()
    {
        Interlocked.Increment(ref _generation); _draft = null; _validation = null; _binding = null; _savedDigest = null;
    }
    private StandardCandidateDraft RequireCurrent()
    {
        if (!ContextCurrent || _draft is null)
            throw new ConfigurationException("The working context changed. Preserve this draft with Save, then reopen it deliberately before editing or validating. Nothing is applied to a tenant.");
        return Copy(_draft);
    }
    private async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken ct)
    {
        if (_busy) throw new ConfigurationException("Wait for the current local authoring operation to finish.");
        _busy = true;
        var captured = context(); var generation = Interlocked.Increment(ref _generation);
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await Task.Run(() => work(ct), ct);
            ct.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref _generation) || captured != context())
                throw new ConfigurationException("The local result was not admitted because its working context or draft changed. Existing draft files were not changed.");
            return result;
        }
        finally { _busy = false; }
    }
    private static StandardCandidateDraft Copy(StandardCandidateDraft draft) => CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));
}
