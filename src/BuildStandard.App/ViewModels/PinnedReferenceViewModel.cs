using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

/// <summary>Immutable original provenance. This record contains no command, credential or approval.</summary>
public sealed record PinnedReferenceRecord(string Title, string TenantId, string Domain, string CaptureId,
    string CapturedAt, string CapturedBy, string StandardRelease, string Digest, string Summary, PresentationContextKey SourceContext)
{
    public static PinnedReferenceRecord FromSnapshot(TenantSnapshot saved, PresentationContextKey context) => new(
        saved.TenantName.Length == 0 ? "Saved client capture" : saved.TenantName, saved.TenantId, saved.PrimaryDomain,
        saved.Id, saved.CapturedAt, saved.CapturedBy, saved.StandardRelease, saved.IntegrityDigest,
        $"Saved configuration capture\n{saved.Collections.Values.Count(c => c.Usable && c.Count == c.Items.Count)} of {saved.Collections.Count} recorded sources usable.\n"
            + $"Recorded completeness: {(saved.Complete ? "complete" : "incomplete")}.\nAvailability is not compliance or deployment readiness.", context);
}

/// <summary>Docked reference only; original details never become a new client's action context.</summary>
public sealed class PinnedReferenceViewModel : ObservableObject
{
    private PinnedReferenceRecord? _record;
    private bool _mismatch;
    private bool _expanded;
    private PresentationContextKey? _lastContext;
    public PinnedReferenceRecord? Record => _record;
    public bool HasReference => _record is not null;
    public bool HasMismatch => _mismatch;
    public bool DetailsExpanded { get => _expanded; set => SetProperty(ref _expanded, value); }
    public string Title => _record is null ? "No reference pinned" : "Saved reference · " + _record.Title;
    public string Provenance => _record is null ? "" : $"Original capture · {_record.CapturedAt} · standard {_record.StandardRelease}";
    public string Summary => _record?.Summary ?? "";
    public string MismatchText => _mismatch ? "The working context has changed. This is the original saved reference; it cannot approve or run actions for the current client." : "Reference only · no execution or approval authority.";
    public string OriginalDetails => _record is null ? "" : $"ORIGINAL SAVED REFERENCE\nClient: {_record.Title}\nTenant ID: {_record.TenantId}\nDomain: {_record.Domain}\nCapture: {_record.CaptureId}\nCaptured: {_record.CapturedAt}\nCaptured by: {_record.CapturedBy}\nStandard: {_record.StandardRelease}\nIntegrity digest: {_record.Digest}";
    public void Pin(PinnedReferenceRecord record) { _record = record; _lastContext = record.SourceContext; _mismatch = false; _expanded = false; RaiseAll(); }
    public void UpdateContext(PresentationContextKey current, string? release)
    {
        var mismatch = _record is not null && (_record.SourceContext != current || _record.StandardRelease != release);
        var changed = _lastContext != current;
        if (mismatch && changed) _expanded = false;
        _lastContext = current;
        if (_mismatch == mismatch && !changed) return;
        _mismatch = mismatch; RaiseAll();
    }
    public void Close() { _record = null; _lastContext = null; _mismatch = false; _expanded = false; RaiseAll(); }
}
