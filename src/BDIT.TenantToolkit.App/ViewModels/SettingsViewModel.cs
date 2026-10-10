using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>
/// Settings, support metadata and the two evidence-transfer journeys: handing this workspace to another engineer, and
/// receiving one. Each journey is a short sequence of steps; a step is enabled only when the one before it is done, and
/// the page says what is missing rather than leaving a button greyed out without a reason.
/// </summary>
public sealed class SettingsViewModel : PageViewModel
{
    private string _lastResult = "";
    private string _lastOutputFolder = "";
    private string _backupFile = "";
    private string _createdBackup = "";
    private string _createdDigest = "";
    private string _restoreFolder = "";
    private string _trustedDigest = "";
    private string _digestSource = "";
    private string _digestSourceFile = "";
    private bool _noTrustedDigest;
    private bool _includeEnvironment, _includeRecentErrors, _includeCollectionStatus, _includeTimeouts;
    private SupportSections _supportSections = SupportSections.None;
    private SupportContext _supportContext = SupportContext.Empty;

    public SettingsViewModel(ShellViewModel shell) : base(shell, "Settings and diagnostics")
    {
        OpenReportsCommand = Sync(() => OpenFolder(Workspace.Paths.ReportsDirectory));
        OpenLogsCommand = Sync(() => OpenFolder(Workspace.Paths.LogsDirectory));
        OpenDataCommand = Sync(() => OpenFolder(Workspace.Paths.DataDirectory));
        OpenConfigCommand = Sync(() => OpenFolder(Workspace.Paths.ConfigDirectory));
        OpenStandardsCommand = Sync(() => OpenFolder(Workspace.Paths.StandardsDirectory));
        OpenLastOutputCommand = Sync(() => OpenFolder(LastOutputFolder), () => Directory.Exists(LastOutputFolder));
        ExportSupportCommand = Command(ExportSupport, () => Workspace.Idle);
        CopySupportCommand = CopyText(() => SupportPreview);

        CreateBackupCommand = Command(CreateBackup, CanTransfer);
        CopyDigestCommand = CopyText(() => CreatedDigest);

        ChooseBackupCommand = Sync(() =>
        {
            var dialog = new OpenFileDialog { Filter = "Workspace backup (*.zip)|*.zip", CheckFileExists = true };
            if (dialog.ShowDialog() == true) BackupFile = dialog.FileName;
        }, CanTransfer);
        LoadDigestFileCommand = Sync(() =>
        {
            var dialog = new OpenFileDialog { Filter = "SHA-256 fingerprint (*.sha256;*.txt)|*.sha256;*.txt|All files (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog() == true) LoadDigestFrom(dialog.FileName);
        }, CanTransfer);
        RestoreBackupCommand = Command(() => Transfer("Verifying and restoring backup", "Checking the backup against its fingerprint and restoring it to a new folder.",
            () => new WorkspaceBackup(Workspace.Paths).RestoreSeparate(BackupFile, RestoreFolder, DigestForTransfer("restored")),
            folder => (folder, $"Restored and verified into {folder}.\nNext: review the restored evidence there. Your current workspace was not changed.")),
            () => CanTransfer() && File.Exists(BackupFile) && RestoreFolder.Length > 0 && FingerprintReady);
        VerifyRestoreCommand = Command(() => Transfer("Verifying restored folder", "Re-checking every restored file against its recorded SHA-256.",
            () => WorkspaceBackup.VerifyRestored(RestoreFolder).ToString(System.Globalization.CultureInfo.InvariantCulture),
            count => (RestoreFolder, $"Restored folder verified: {count} evidence file(s) match their recorded SHA-256.\nNext: nothing to do; the folder is unchanged since it was restored.")),
            () => Workspace.Idle && Directory.Exists(RestoreFolder));
        AdoptRestoreCommand = Command(Adopt, () => CanTransfer() && File.Exists(BackupFile) && FingerprintReady && AdoptBlockedReason.Length == 0);

        // Sensitive restores go to transfers/, apart from reports engineers share.
        RestoreFolder = Path.Combine(Workspace.Paths.TransfersDirectory, "restored-evidence-" + Guid.NewGuid().ToString("N"));
        RefreshSupportPreview();
    }

    public ICommand OpenReportsCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenDataCommand { get; }
    public ICommand OpenConfigCommand { get; }
    public ICommand OpenStandardsCommand { get; }
    public ICommand OpenLastOutputCommand { get; }
    public ICommand ExportSupportCommand { get; }
    public ICommand CopySupportCommand { get; }
    public ICommand CreateBackupCommand { get; }
    public ICommand CopyDigestCommand { get; }
    public ICommand ChooseBackupCommand { get; }
    public ICommand LoadDigestFileCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand VerifyRestoreCommand { get; }
    public ICommand AdoptRestoreCommand { get; }

    public string BackupGuide => WorkspaceBackup.Guide;
    public string LastResult { get => _lastResult; private set { if (SetProperty(ref _lastResult, value)) OnPropertyChanged(nameof(HasResult)); } }
    public bool HasResult => LastResult.Length > 0;
    /// <summary>The folder holding the last file written or restored, for the "Open folder" action beside the result.</summary>
    public string LastOutputFolder { get => _lastOutputFolder; private set => SetProperty(ref _lastOutputFolder, value); }

    // ---- support metadata -------------------------------------------------------------------------------------------

    public bool IncludeEnvironment { get => _includeEnvironment; set { if (SetProperty(ref _includeEnvironment, value)) RefreshSupportPreview(); } }
    public bool IncludeRecentErrors { get => _includeRecentErrors; set { if (SetProperty(ref _includeRecentErrors, value)) RefreshSupportPreview(); } }
    public bool IncludeCollectionStatus { get => _includeCollectionStatus; set { if (SetProperty(ref _includeCollectionStatus, value)) RefreshSupportPreview(); } }
    public bool IncludeTimeouts { get => _includeTimeouts; set { if (SetProperty(ref _includeTimeouts, value)) RefreshSupportPreview(); } }
    /// <summary>Exactly what Export writes: both use the sections and values captured when the preview was last built.</summary>
    public string SupportPreview => SupportBundle.Preview(Workspace.Standard, _supportSections, _supportContext);

    private void RefreshSupportPreview()
    {
        _supportSections = new SupportSections(IncludeEnvironment, IncludeRecentErrors, IncludeCollectionStatus, IncludeTimeouts);
        _supportContext = new SupportContext(DisplayScalePercent(), Workspace.Snapshot, Workspace.Settings, RecentGraphErrors.Shared.Snapshot());
        OnPropertyChanged(nameof(SupportPreview));
    }

    private async Task ExportSupport()
    {
        var sections = _supportSections; var context = _supportContext;
        var file = await Workspace.ExportAsync(() => SupportBundle.Export(Workspace.Paths, Workspace.Standard, sections, context),
            "Writing support metadata", "Writing the previewed support metadata to the reports folder.");
        ShowResult(file, $"Support metadata written: {file}\nNext: review it, then attach it to the support request. Nothing was uploaded.");
    }

    private static double? DisplayScalePercent()
    {
        var window = System.Windows.Application.Current?.MainWindow;
        return window is null ? null : System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX * 100;
    }

    // ---- hand this workspace to another engineer --------------------------------------------------------------------

    /// <summary>Why backup, restore and adoption are unavailable right now, in words; empty when they are available.</summary>
    public string TransferBlockedReason =>
        !Workspace.Idle ? "Wait for the current task to finish."
        : Workspace.IsConnected ? "Disconnect from the tenant first (top right). Evidence is only moved while no session is open."
        : Workspace.ApplicationSetup is not null ? "Close the application setup session first."
        : "";
    public bool HasTransferBlock => TransferBlockedReason.Length > 0;

    public string CreatedBackup { get => _createdBackup; private set => SetProperty(ref _createdBackup, value); }
    /// <summary>The new archive's SHA-256, to send to the receiver separately from the archive itself.</summary>
    public string CreatedDigest { get => _createdDigest; private set { if (SetProperty(ref _createdDigest, value)) OnPropertyChanged(nameof(HasCreatedBackup)); } }
    public bool HasCreatedBackup => CreatedDigest.Length > 0;

    private async Task CreateBackup()
    {
        var file = await Workspace.ExportAsync(() => new WorkspaceBackup(Workspace.Paths).Create(),
            "Creating evidence backup", "Copying and checksumming every evidence file into a new backup archive.");
        CreatedBackup = file;
        CreatedDigest = WorkspaceBackup.ArchiveDigest(file);
        ShowResult(file, $"Backup written: {file}\nNext: send the backup file to the other engineer, and send its SHA-256 fingerprint by a different route (for example the file by the shared folder and the fingerprint by Teams message). Use Copy fingerprint.");
    }

    // ---- receive a workspace ----------------------------------------------------------------------------------------

    public string BackupFile { get => _backupFile; set { if (SetProperty(ref _backupFile, value)) { OnPropertyChanged(nameof(ReceiveNextStep)); OnPropertyChanged(nameof(DigestSource)); OnPropertyChanged(nameof(HasDigestSource)); } } }
    public string RestoreFolder { get => _restoreFolder; set => SetProperty(ref _restoreFolder, value); }

    /// <summary>
    /// The archive SHA-256 received separately from the archive, as pasted or loaded. A checksum line such as
    /// "&lt;digest&gt;  backup.zip" is accepted; the digest is taken from it.
    /// </summary>
    public string TrustedDigest
    {
        get => _trustedDigest;
        set
        {
            if (!SetProperty(ref _trustedDigest, value)) return;
            _digestSource = ""; _digestSourceFile = "";
            OnPropertyChanged(nameof(TrustedDigestValid)); OnPropertyChanged(nameof(DigestStatus)); OnPropertyChanged(nameof(DigestSource)); OnPropertyChanged(nameof(HasDigestSource)); OnPropertyChanged(nameof(ReceiveNextStep));
        }
    }
    public bool TrustedDigestValid => WorkspaceBackup.IsValidDigest(WorkspaceBackup.NormaliseDigest(TrustedDigest));
    public string DigestStatus =>
        TrustedDigest.Trim().Length == 0 ? "Paste the 64-character SHA-256 fingerprint you were sent, or load it from its .sha256 file."
        : TrustedDigestValid ? "Fingerprint format is valid. It is checked against the backup when you restore or adopt."
        : "This is not a SHA-256 fingerprint. It should be 64 characters, 0–9 and a–f.";
    /// <summary>Where a loaded fingerprint came from, and a warning when it may have travelled with the backup.</summary>
    public string DigestSource => _digestSource.Length == 0 ? "" : _digestSource + TravelledTogetherWarning();
    public bool HasDigestSource => DigestSource.Length > 0;

    /// <summary>Explicit acknowledgement that no independent digest is available; internal checksums then detect only accidental damage.</summary>
    public bool NoTrustedDigest { get => _noTrustedDigest; set { if (SetProperty(ref _noTrustedDigest, value)) OnPropertyChanged(nameof(ReceiveNextStep)); } }
    private bool FingerprintReady => TrustedDigestValid || (NoTrustedDigest && TrustedDigest.Trim().Length == 0);

    /// <summary>Why adoption is unavailable while restore to a separate folder may still be possible; empty when adoption is allowed.</summary>
    public string AdoptBlockedReason =>
        Workspace.Profile is not null ? "A client is selected. Adoption is only offered in a workspace with no clients: use a freshly extracted copy of the tool, or restore to a separate folder instead."
        : EvidencePresent() ? "This workspace already holds evidence. Adopt into a freshly extracted copy of the tool, or restore to a separate folder instead."
        : "";

    public string ReceiveNextStep =>
        TransferBlockedReason.Length > 0 ? TransferBlockedReason
        : !File.Exists(BackupFile) ? "Step 1: choose the backup file you were sent."
        : !FingerprintReady ? "Step 2: paste or load the SHA-256 fingerprint you were sent separately."
        : AdoptBlockedReason.Length == 0 ? "Step 3: adopt the evidence into this workspace (recommended for a new copy of the tool), or restore it to a separate folder to inspect it."
        : "Step 3: restore to a separate folder. " + AdoptBlockedReason;

    public void LoadDigestFrom(string file)
    {
        string text;
        using (var reader = new StreamReader(file)) { var buffer = new char[1024]; text = new string(buffer, 0, reader.Read(buffer, 0, buffer.Length)); }
        var firstLine = text.Split('\n', 2)[0];
        TrustedDigest = firstLine.Trim();
        _digestSource = $"Loaded from {Path.GetFileName(file)}.";
        _digestSourceFile = Path.GetFullPath(file);
        OnPropertyChanged(nameof(DigestSource)); OnPropertyChanged(nameof(HasDigestSource));
    }

    private string TravelledTogetherWarning()
    {
        if (BackupFile.Length == 0 || _digestSourceFile.Length == 0) return "";
        var backupFolder = Path.GetDirectoryName(Path.GetFullPath(BackupFile));
        return string.Equals(backupFolder, Path.GetDirectoryName(_digestSourceFile), StringComparison.OrdinalIgnoreCase)
            ? " This file is in the same folder as the backup, so it may have travelled with it. A fingerprint that travelled with the backup only proves the file was not damaged, not that nobody changed it. Check it against the fingerprint the sender gave you separately."
            : "";
    }

    /// <summary>
    /// The digest, normalised from what was pasted or loaded, for the engine to validate, so a malformed digest is
    /// refused rather than treated as absent; null only when the field is empty, which needs the explicit acknowledgement.
    /// </summary>
    private string? DigestForTransfer(string action)
    {
        if (TrustedDigest.Trim().Length > 0) return WorkspaceBackup.NormaliseDigest(TrustedDigest);
        Workspace.Logger.Warn("Transfer", $"Backup {action} without an independently received archive digest, at the engineer's explicit acknowledgement.");
        return null;
    }

    private async Task Adopt()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Check the selected backup against its fingerprint and copy its evidence into this workspace's empty evidence folder?\n\nEvery file is checked again after copying. The adopted clients are loaded straight away. No sign-in, plan approval or session is restored, and the next plan still needs fresh evidence.",
            "Adopt evidence into this workspace", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;
        var digest = DigestForTransfer("adopted");
        try
        {
            await Transfer("Adopting verified evidence", "Checking the backup against its fingerprint, copying its evidence in and re-checking every file.",
                () => new WorkspaceBackup(Workspace.Paths).AdoptFromArchive(BackupFile, digest),
                message => (Workspace.Paths.DataDirectory, message + "\nNext: choose an adopted client on Connect. Capture fresh evidence before planning any change."));
        }
        finally
        {
            // If adoption put evidence in place, the in-memory client list must match it, or a client saved next would
            // rewrite profiles.json without the adopted clients. Reloading when adoption was refused is harmless. A
            // reload failure is logged rather than replacing the adoption's own result or error.
            if (EvidencePresent())
            {
                try { Workspace.ReloadAdoptedEvidence(); }
                catch (ToolkitException ex) { Workspace.Logger.Warn("Transfer", "Adopted clients could not be loaded until restart: " + ex.Message); }
            }
            OnPropertyChanged(nameof(AdoptBlockedReason)); OnPropertyChanged(nameof(ReceiveNextStep));
        }
    }

    /// <summary>
    /// Runs a transfer step and reports the outcome in plain words. A refusal (wrong fingerprint, changed file, folder
    /// not empty) is an expected answer, so it is shown with what to do next rather than as an application error.
    /// </summary>
    private async Task Transfer(string title, string progress, Func<string> work, Func<string, (string Folder, string Message)> success)
    {
        try
        {
            var (folder, message) = success(await Workspace.ExportAsync(work, title, progress));
            ShowResult(folder, message);
        }
        catch (IntegrityException ex)
        {
            Workspace.Logger.Warn("Transfer", ex.Message);
            LastResult = "Stopped: " + ex.Message + "\nWhat to do: nothing was changed. Ask the sender to confirm the fingerprint by a different route, or to create a new backup. Do not use this backup until the fingerprint matches.";
        }
        catch (ConfigurationException ex)
        {
            Workspace.Logger.Warn("Transfer", ex.Message);
            LastResult = "Not done: " + ex.Message + "\nWhat to do: correct the step above and try again. Nothing was changed.";
        }
    }

    private void ShowResult(string path, string message)
    {
        LastOutputFolder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? "";
        LastResult = message;
    }

    private bool EvidencePresent()
    {
        var data = Workspace.Paths.DataDirectory;
        return Directory.Exists(data) && Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Any();
    }

    private bool CanTransfer() => TransferBlockedReason.Length == 0;

    public string Text
    {
        get
        {
            var s = Workspace.Settings;
            var p = Workspace.Paths;
            var lines = new List<string>
            {
                $"Toolkit version:            {Workspace.Version}",
                $"Diagnostics mode:           {(Workspace.Diagnostics ? "on (verbose logging)" : "off")}",
                $"Root folder:                {p.Root}",
                $"Application folder:         {p.AppDirectory}",
                $"Settings file:              {p.SettingsFile}",
                $"Standards folder:           {p.StandardsDirectory}",
                $"Evidence (data) folder:     {p.DataDirectory}",
                $"Reports folder:             {p.ReportsDirectory}",
                $"Transfers folder:           {p.TransfersDirectory}",
                $"Logs folder:                {p.LogsDirectory} (current file: {Workspace.Logger.FilePath})",
                "",
                $"Assessment application:     {(s.AssessmentClientId.Length == 0 ? "not configured" + (s.AllowMicrosoftGraphPowerShellFallback ? " - shared Microsoft Graph PowerShell app will be used (read-only)" : "") : s.AssessmentClientId)}",
                $"Deployment application:     {(s.DeploymentClientId.Length == 0 ? "not configured - deployment unavailable" : s.DeploymentClientId)}",
                $"Default standard release:   {s.DefaultStandardRelease}",
                $"Loaded standard:            {(Workspace.Standard is null ? "none (" + Workspace.StandardError + ")" : Workspace.Standard.Release + " digest " + Workspace.Standard.IntegrityDigest)}",
                $"Snapshot max age (minutes): {s.SnapshotMaxAgeMinutes}",
                $"Plan max age (minutes):     {s.PlanMaxAgeMinutes}",
                $"Sign-in timeout (minutes):  {s.SignInTimeoutMinutes}",
                $"Graph read timeout (s):     {s.GraphReadTimeoutSeconds}",
                $"Graph write timeout (s):    {s.GraphWriteTimeoutSeconds}",
                $"Max Retry-After honoured:   {s.MaxRetryAfterSeconds} s",
                $"Log level:                  {s.LogLevel}",
                "",
                "Settings are edited in config/toolkit.settings.json and applied at the next start. No secrets are stored; you always sign in to Microsoft directly, in the Windows sign-in window or, if selected on the Connect page, your browser.",
                "Token caches are DPAPI-protected per tenant and removed on disconnect and on exit.",
                "Run Start-Diagnostics.cmd to launch with verbose logging and a visible start-up log."
            };
            return string.Join(Environment.NewLine, lines);
        }
    }

    private static void OpenFolder(string path) => Infrastructure.ShellFolders.OpenFolder(path);

    public override void Refresh()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(TransferBlockedReason)); OnPropertyChanged(nameof(HasTransferBlock)); OnPropertyChanged(nameof(AdoptBlockedReason)); OnPropertyChanged(nameof(ReceiveNextStep));
        RefreshSupportPreview();
    }
}
