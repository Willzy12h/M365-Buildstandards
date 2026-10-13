using System.Diagnostics;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.App.Services.Reader;
using BuildStandard.Core.Models;
using BuildStandard.Core.Diagnostics;

namespace BuildStandard.App.ViewModels;

/// <summary>Transient native document presentation. Never grants tenant or execution authority.</summary>
public sealed class CatalogueReaderViewModel : PageViewModel
{
    private readonly ReaderAdmission _admission = new();
    private ReaderTicket? _ticket;
    private ReaderNavigationPolicy? _policy;
    private string _state = "Closed", _status = "", _pending = "", _openedAt = "", _find = "", _saved = "";
    public CatalogueReaderViewModel(ShellViewModel shell) : base(shell, "Catalogue reference")
    {
        BackCommand = Sync(() => { if (IsCurrent) Shell.Page<StandardViewModel>().RequestReferenceReturn(ControlId); Shell.Navigate("standard"); });
        RetryCommand = Command(() => Shell.ShowCatalogueReferenceAsync(Document!, ControlId, Workspace.CapturePresentationContext()),
            () => Document is not null && IsCurrent && Workspace.Idle && State is "Unavailable" or "CacheUnavailable" or "Failed");
        SaveCommand = Command(SaveAsync, () => Document is not null && State != "Invalid" && IsCurrent && Workspace.Idle);
        OpenReferenceCommand = Sync(OpenReference, () => IsReady && PendingReference.Length > 0);
        FindCommand = Sync(() => FindRequested?.Invoke(FindText), () => State == "Ready" && IsCurrent);
        ZoomInCommand = Sync(() => ZoomRequested?.Invoke(.25), () => State == "Ready" && IsCurrent);
        ZoomOutCommand = Sync(() => ZoomRequested?.Invoke(-.25), () => State == "Ready" && IsCurrent);
    }
    public event Action? Closing;
    public event Action<string>? FindRequested;
    public event Action<double>? ZoomRequested;
    public CatalogueDocument? Document { get; private set; }
    public string ControlId { get; private set; } = "";
    public bool IsCurrent => _ticket is not null && _admission.IsCurrent(_ticket, Workspace.CapturePresentationContext(), Shell.NavigationEpoch);
    public string State { get => _state; private set
    {
        if (SetProperty(ref _state, value))
        { OnPropertyChanged(nameof(IsReady)); CommandManager.InvalidateRequerySuggested(); }
    } }
    public bool IsReady => State == "Ready" && IsCurrent;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PendingReference { get => _pending; private set { if (SetProperty(ref _pending, value)) { OnPropertyChanged(nameof(HasPendingReference)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool HasPendingReference => PendingReference.Length > 0;
    public string OpenedAt { get => _openedAt; private set => SetProperty(ref _openedAt, value); }
    public string FindText { get => _find; set => SetProperty(ref _find, value); }
    public string Saved { get => _saved; private set => SetProperty(ref _saved, value); }
    public string DocumentIdentity => Document is not { } doc ? "No admitted document"
        : $"Catalogue reference · {doc.CatalogueRelease} · generated {doc.GeneratedAt:u}\nCatalogue SHA-256 {doc.CatalogueDigest}\nDocument SHA-256 {doc.HtmlSha256} · presentation {doc.PresentationVersion}\nReference material; not tenant evidence or deployment approval.";
    public ICommand BackCommand { get; }
    public ICommand RetryCommand { get; }
    public void RecordCleanup(string status)
    {
        if (status == "Reader cache cleanup incomplete") Workspace.Logger.Warn("Reader", status);
        else Workspace.Logger.Info("Reader", status);
    }
    public ICommand SaveCommand { get; }
    public ICommand OpenReferenceCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public Task OpenAsync(CatalogueDocument document, string controlId, PresentationContextKey context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Workspace.IsCurrent(context)) throw new InvalidOperationException("The current client or catalogue changed. Open its reference again.");
        document.RequireAnchor(controlId);
        Document = document; ControlId = controlId; _ticket = _admission.Begin(context, document.HtmlSha256, Shell.NavigationEpoch);
        _policy = new(document.Anchors, document.RendererReferences);
        State = "Preparing"; Status = "Preparing the contained catalogue reader. Native controls remain independent.";
        OnPropertyChanged(nameof(DocumentIdentity)); return Task.CompletedTask;
    }
    public void Loading(string stage) { if (IsCurrent) { State = "Loading"; Status = stage; } }
    public void Ready(string version) { if (IsCurrent) { State = "Ready"; Status = "Document displayed · Runtime " + version + ". Select/copy text, find or zoom; no commands run from this document."; } }
    public void Failed(string state, string notice) { if (IsCurrent) { State = state; Status = notice; PendingReference = ""; } }
    public void ObserveAnchor(string anchor) { if (IsCurrent) OpenedAt = "Opened at " + anchor; }
    public void SetPendingReference(string url) { if (IsReady && _policy?.AllowsReference(url, true, IsCurrent) == true) PendingReference = url; }
    private async Task SaveAsync()
    {
        var document = Document ?? throw new InvalidOperationException("No valid catalogue document to save.");
        if (!IsCurrent) throw new InvalidOperationException("This reference is no longer current.");
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.SaveHtml(document));
        if (IsCurrent) Saved = "Exact displayed HTML saved: " + file;
    }
    private void OpenReference()
    {
        var url = PendingReference;
        if (!IsReady || _policy?.AllowsReference(url, true, IsCurrent) != true) throw new InvalidOperationException("Select a current Microsoft reference again.");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); PendingReference = "";
    }
    public void Close()
    {
        _admission.Close(); PendingReference = ""; State = "Closed"; Closing?.Invoke();
        Workspace.StateChanged -= Refresh;
    }
    public override void Refresh()
    {
        if (_ticket is not null && State is not "Closed" and not "Superseded" && !IsCurrent)
        {
            _admission.Close(); PendingReference = ""; State = "Superseded";
            Status = "Client, account, evidence or catalogue changed. The old reader is hidden. Return to the current native catalogue.";
            Closing?.Invoke();
        }
    }
}
