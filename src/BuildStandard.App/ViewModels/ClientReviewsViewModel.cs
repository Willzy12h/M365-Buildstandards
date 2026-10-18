using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Identity;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Workflow;

namespace BuildStandard.App.ViewModels;

public sealed record ClientPortfolioRow(string ProfileId, string TenantId, string Company, string Assessment, string Standard,
    string Writes, string Jobs, string Detail);
public sealed class ClientImportRow(PartnerCustomerRow row) : ObservableObject
{
    private bool _selected;
    public PartnerCustomerRow Row { get; } = row;
    public bool CanSelect => Row.State == CustomerImportState.Ready;
    public bool IsSelected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, CanSelect && value)) CommandManager.InvalidateRequerySuggested(); }
    }
}

/// <summary>Explicit, local-only reads. Portfolio selection never acquires or replaces a tenant connection.</summary>
public sealed class ClientReviewsViewModel : PageViewModel
{
    private ClientPortfolioRow? _selected;
    private long _selectionVersion;
    private ClientReviewDocuments.Document? _document;
    private PresentationContextKey? _reviewContext;
    private string _status = "Read local evidence or preview a customer CSV. No connection is made.";
    private string _importStatus = "Choose a Partner Centre customer CSV (5 MiB / 5,000 rows maximum). Only selected valid new clients will be saved.";

    public ClientReviewsViewModel(ShellViewModel shell) : base(shell, "Clients and reviews")
    {
        ScanCommand = Command(ScanAsync, () => Workspace.Idle);
        PreviewImportCommand = Command(ChooseCsvAsync, () => Workspace.Idle);
        SaveImportCommand = Command(SaveImportAsync, () => Workspace.Idle && ImportRows.Any(r => r.IsSelected));
        ReviewCommand = Command(ReviewSelectedAsync, () => Workspace.Idle && Selected is not null);
        OpenCommand = Command(OpenSelectedAsync, () => OpenBlocker.Length == 0);
        ExportCommand = Command(ChooseExportAsync, () => ExportBlocker.Length == 0);
        CopyDetailsCommand = CopyText(() => Details);
    }

    public ObservableCollection<ClientPortfolioRow> Clients { get; } = new();
    public ObservableCollection<ClientImportRow> ImportRows { get; } = new();
    public ClientPortfolioRow? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) { Interlocked.Increment(ref _selectionVersion); ClearReview(); Refresh(); } }
    }
    public ClientReviewResult? Review => _document?.Review;
    public PartnerCustomerPreview? ImportPreview { get; private set; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ImportStatus { get => _importStatus; private set => SetProperty(ref _importStatus, value); }
    public string ReadGuidance => Workspace.Busy ? "Wait for the active operation, or use Stop above." : "Read local summaries explicitly. Navigation makes no tenant calls.";
    public string SelectionGuidance => Selected is null ? "Select a client row to review its saved history." : "Review reads this row's local history; it does not change the connected client shown above.";
    public string OpenBlocker => Workspace.Busy ? "Wait for the active operation to finish."
        : Selected is null ? "Select a client first."
        : Workspace.Connection is not null || Workspace.ApplicationSetup is not null ? "Disconnect the tenant and application setup sessions before opening another client offline."
        : Shell.Page<RunReportsViewModel>().HasPendingEvidence ? "Save or explicitly discard the pending result in Registered read-only runs before switching clients." : "";
    public string OpenGuidance => OpenBlocker.Length == 0 ? "Open this client in Configuration for offline work. No sign-in." : OpenBlocker;
    public string ExportBlocker => Workspace.Busy ? "Wait for the active operation."
        : _document is null || _reviewContext is null ? "Review a client first."
        : !Workspace.IsCurrent(_reviewContext) ? "The working context changed. Review this client again before exporting." : "";
    public string ExportGuidance => ExportBlocker.Length == 0 ? "Export engineer/client HTML, CSV and a manifest. Changed source bytes or client details refuse the export." : ExportBlocker;
    public string Details => _document is null ? Selected?.Detail ?? "No client reviewed."
        : string.Join("\n", ClientReviewDocuments.Sheets(_document)[0].Rows.Skip(1).Select(r => r[0] + ": " + r[1]))
          + "\n\nCollection and history limits:\n" + string.Join("\n", Review!.Diagnostics.Select(d => d.Section + " / " + d.File + ": " + d.Problem))
          + "\n\nJobs:\n" + string.Join("\n", Review.Jobs.Select(j => j.Id + ": " + j.Claim + " " + string.Join("; ", j.Reasons)));
    public ICommand ScanCommand { get; }
    public ICommand PreviewImportCommand { get; }
    public ICommand SaveImportCommand { get; }
    public ICommand ReviewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand CopyDetailsCommand { get; }

    public Task ScanAsync() => LocalOperationAsync("Reading saved client summaries", async progress =>
    {
        Selected = null; Clients.Clear();
        var context = Workspace.CapturePresentationContext();
        var token = Workspace.OperationToken;
        var result = await Task.Run(() =>
        {
            var profiles = Workspace.Evidence.LoadClientProfiles();
            var rows = new List<ClientPortfolioRow>();
            foreach (var profile in profiles)
            {
                token.ThrowIfCancellationRequested();
                progress.Report($"Reading local client {rows.Count + 1} of {profiles.Count}.");
                try
                {
                    var evidence = Workspace.Evidence.LoadClientReviewEvidence(profile.TenantId, token);
                    var review = ClientReviewProjection.Project(profile, evidence, DateTimeOffset.UtcNow);
                    rows.Add(new(profile.Id, profile.TenantId, profile.Company,
                        review.LastAssessment is { } a ? Timestamps.Format(a.At) + (a.Complete ? " · stored complete" : " · incomplete") : "No readable assessment",
                        review.LastAssessment?.Release ?? "Unknown",
                        review.WriteHistoryKnown ? $"{review.UnresolvedWrites} unresolved" : "Unknown history",
                        review.Jobs.Count == 0 ? "No readable jobs" : $"{review.Jobs.Count(j => j.Complete)} complete / {review.Jobs.Count} recorded",
                        profile.TenantId + "\n" + string.Join("\n", review.Diagnostics.Select(d => d.Section + ": " + d.Problem))));
                }
                catch (Exception ex) when (ex is ToolkitException or IOException or UnauthorizedAccessException)
                { rows.Add(new(profile.Id, profile.TenantId, profile.Company, "Could not check", "Unknown", "Unknown", "Unknown", ex.Message)); }
            }
            return (profiles, rows);
        }, token);
        token.ThrowIfCancellationRequested();
        RequireContext(context);
        foreach (var row in result.rows) Clients.Add(row);
        ReplaceProfileList(result.profiles);
        Status = Clients.Count == 0 ? "No saved clients. Preview a customer CSV or save a client in Client and connection."
            : $"Read {Clients.Count} local client summaries. These are saved records, not current tenant checks. Review a row for details.";
    });

    public Task PreviewImportAsync(byte[] bytes) => LocalOperationAsync("Previewing customer CSV", async _ =>
    {
        ImportPreview = null; ImportRows.Clear();
        var token = Workspace.OperationToken;
        var preview = await Task.Run(() => PartnerCustomerImport.Preview(bytes, Workspace.Evidence.LoadClientProfiles()), token);
        token.ThrowIfCancellationRequested();
        ImportPreview = preview;
        foreach (var row in preview.Rows) ImportRows.Add(new(row));
        ImportStatus = $"{preview.DataRows} source rows; {preview.Rows.Count} distinct entries; {preview.Rows.Count(r => r.State == CustomerImportState.Ready)} valid new clients. Nothing selected or saved yet. Relationship and tags grant no access.";
        Status = "CSV preview ready. Review the row reasons and tick only the clients to add.";
    });

    public Task SaveImportAsync()
    {
        var preview = ImportPreview ?? throw new ConfigurationException("Preview a customer CSV first.");
        var selected = ImportRows.Where(r => r.IsSelected).Select(r => r.Row.TenantId).ToArray();
        if (selected.Length == 0) throw new ConfigurationException("Tick at least one valid new client before saving.");
        return LocalOperationAsync("Saving selected client profiles", async _ =>
        {
            var token = Workspace.OperationToken;
            var saved = await Task.Run(() => Workspace.Evidence.ImportCustomers(preview, selected, DateTimeOffset.UtcNow, token), token);
            // A completed atomic save remains saved even if Stop is pressed just after its commit.
            ReplaceProfileList(saved);
            ImportPreview = null; ImportRows.Clear();
            ImportStatus = $"Saved {selected.Length} new client profiles locally. No sign-in, consent or tenant change.";
            Status = ImportStatus + " Read local summaries to refresh the table.";
        });
    }

    public Task ReviewSelectedAsync()
    {
        var selected = Selected ?? throw new ConfigurationException("Select a client to review.");
        var version = Interlocked.Read(ref _selectionVersion);
        var context = Workspace.CapturePresentationContext();
        ClearReview();
        return LocalOperationAsync("Reviewing saved client evidence", async _ =>
        {
            var token = Workspace.OperationToken;
            var document = await Task.Run(() =>
            {
                var profile = Workspace.Evidence.LoadClientProfiles().SingleOrDefault(p => p.Id == selected.ProfileId && p.TenantId == selected.TenantId)
                    ?? throw new ConfigurationException("The saved client changed. Read local summaries and select it again.");
                return ClientReviewDocuments.Create(profile, Workspace.Evidence.LoadClientReviewEvidence(profile.TenantId, token), DateTimeOffset.UtcNow);
            }, token);
            token.ThrowIfCancellationRequested();
            RequireContext(context, version);
            _document = document; _reviewContext = context;
            Status = $"Local review: {document.Review.ClientLabel}. {document.Review.Diagnostics.Count} history diagnostics. No tenant was contacted; the active client is unchanged.";
        });
    }

    public async Task OpenSelectedAsync()
    {
        if (OpenBlocker.Length > 0) throw new ToolkitException(OpenBlocker);
        var selected = Selected!;
        await Workspace.SelectOfflineProfileAsync(selected.ProfileId);
        if (Workspace.Profile?.Id == selected.ProfileId && Workspace.Profile.TenantId == selected.TenantId)
            Shell.Navigate("configuration");
    }

    public async Task<string> ExportAsync(string directory)
    {
        if (ExportBlocker.Length > 0) throw new ConfigurationException(ExportBlocker);
        var document = _document!; var context = _reviewContext!; var version = Interlocked.Read(ref _selectionVersion);
        var path = "";
        await LocalOperationAsync("Exporting local client review", async _ =>
        {
            var token = Workspace.OperationToken;
            path = await Task.Run(() => ClientReviewDocuments.Export(document, Workspace.Evidence, directory,
                () => Workspace.IsCurrent(context) && Interlocked.Read(ref _selectionVersion) == version, token), token);
            RequireContext(context, version);
            Status = "Review pack saved: " + path;
        });
        return path;
    }

    private async Task ChooseCsvAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Preview Partner Centre customer CSV", Filter = "Customer CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog() != true) return;
        byte[] bytes;
        using (var file = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (file.Length > PartnerCustomerImport.MaximumBytes) throw new ConfigurationException("Customer CSV exceeds the 5 MiB limit.");
            bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
            if (file.ReadByte() != -1) throw new ConfigurationException("Customer CSV changed while reading. Preview it again.");
        }
        await PreviewImportAsync(bytes);
    }
    private async Task ChooseExportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder for the client review pack" };
        if (dialog.ShowDialog() == true) await ExportAsync(dialog.FolderName);
    }
    private async Task LocalOperationAsync(string title, Func<IProgress<string>, Task> action)
    {
        await Workspace.RunExclusiveAsync(title, async progress =>
        {
            Status = title + "…";
            try { await action(progress); }
            catch (OperationCanceledException) when (Workspace.OperationToken.IsCancellationRequested)
            { Status = "Stopped. No complete result was admitted. Previous saved evidence is retained."; throw; }
            catch (Exception ex) { Status = "Could not complete: " + ex.Message; throw; }
        });
    }
    private void ReplaceProfileList(IReadOnlyList<TenantProfile> profiles)
    {
        Workspace.Profiles.Clear();
        foreach (var profile in profiles) Workspace.Profiles.Add(profile);
    }
    private void RequireContext(PresentationContextKey context, long? selectionVersion = null)
    {
        if (!Workspace.IsCurrent(context) || selectionVersion is { } version && version != Interlocked.Read(ref _selectionVersion))
            throw new ConfigurationException("Client selection or working context changed. The delayed result was not admitted; review again.");
    }
    private void ClearReview() { _document = null; _reviewContext = null; }
    public override void Refresh()
    {
        if (_reviewContext is not null && !Workspace.IsCurrent(_reviewContext))
        { ClearReview(); Status = "Working context changed. Review the selected client again before copying or exporting."; }
        foreach (var name in new[] { nameof(Review), nameof(ImportPreview), nameof(Details), nameof(ReadGuidance), nameof(SelectionGuidance), nameof(OpenGuidance), nameof(ExportGuidance) })
            OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
