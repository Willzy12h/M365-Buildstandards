using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private string _lastResult = "";
    private string _backupFile = "";
    private string _restoreFolder = "";
    private string _trustedDigest = "";
    private bool _noTrustedDigest;
    public SettingsViewModel(ShellViewModel shell) : base(shell, "Settings and diagnostics")
    {
        OpenReportsCommand = Sync(() => OpenFolder(Workspace.Paths.ReportsDirectory));
        OpenLogsCommand = Sync(() => OpenFolder(Workspace.Paths.LogsDirectory));
        OpenDataCommand = Sync(() => OpenFolder(Workspace.Paths.DataDirectory));
        OpenConfigCommand = Sync(() => OpenFolder(Workspace.Paths.ConfigDirectory));
        OpenStandardsCommand = Sync(() => OpenFolder(Workspace.Paths.StandardsDirectory));
        ExportSupportCommand = Command(async () => LastResult = "Support metadata written: " + await Workspace.ExportAsync(() => SupportBundle.Export(Workspace.Paths, Workspace.Standard)), () => Workspace.Idle);
        CopySupportCommand = CopyText(() => SupportPreview);
        CreateBackupCommand = Command(async () => LastResult = "Sensitive evidence backup written: " + (BackupFile = await Workspace.ExportAsync(() => new WorkspaceBackup(Workspace.Paths).Create())), CanTransfer);
        ChooseBackupCommand = Sync(() =>
        {
            var dialog = new OpenFileDialog { Filter = "Workspace backup (*.zip)|*.zip", CheckFileExists = true };
            if (dialog.ShowDialog() == true) BackupFile = dialog.FileName;
        }, CanTransfer);
        RestoreBackupCommand = Command(async () => LastResult = "Verified separate restore written: " + await Workspace.ExportAsync(Restore),
            () => CanTransfer() && File.Exists(BackupFile) && RestoreFolder.Length > 0 && (TrustedDigestValid || NoTrustedDigest));
        VerifyRestoreCommand = Command(async () => LastResult = await Workspace.ExportAsync(() => $"Restored folder verified: {WorkspaceBackup.VerifyRestored(RestoreFolder)} evidence file(s) match their recorded SHA-256."),
            () => Workspace.Idle && Directory.Exists(RestoreFolder));
        AdoptRestoreCommand = Command(Adopt, () => CanTransfer() && Directory.Exists(RestoreFolder));
        // Sensitive restores go to transfers/, apart from reports engineers share.
        RestoreFolder = Path.Combine(Workspace.Paths.TransfersDirectory, "restored-evidence-" + Guid.NewGuid().ToString("N"));
    }

    public ICommand OpenReportsCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenDataCommand { get; }
    public ICommand OpenConfigCommand { get; }
    public ICommand OpenStandardsCommand { get; }
    public ICommand ExportSupportCommand { get; }
    public ICommand CopySupportCommand { get; }
    public ICommand CreateBackupCommand { get; }
    public ICommand ChooseBackupCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand VerifyRestoreCommand { get; }
    public ICommand AdoptRestoreCommand { get; }
    public string SupportPreview => SupportBundle.Preview(Workspace.Standard);
    public string BackupGuide => WorkspaceBackup.Guide;
    public string LastResult { get => _lastResult; private set => SetProperty(ref _lastResult, value); }
    public string BackupFile { get => _backupFile; set => SetProperty(ref _backupFile, value); }
    public string RestoreFolder { get => _restoreFolder; set => SetProperty(ref _restoreFolder, value); }
    /// <summary>The archive SHA-256 received separately from the archive, through the approved handoff channel.</summary>
    public string TrustedDigest { get => _trustedDigest; set { if (SetProperty(ref _trustedDigest, value)) OnPropertyChanged(nameof(TrustedDigestValid)); } }
    public bool TrustedDigestValid => TrustedDigest.Trim().Length == 64 && TrustedDigest.Trim().All(Uri.IsHexDigit);
    /// <summary>Explicit acknowledgement that no independent digest is available; internal checksums then detect only accidental damage.</summary>
    public bool NoTrustedDigest { get => _noTrustedDigest; set => SetProperty(ref _noTrustedDigest, value); }

    private string Restore()
    {
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(RestoreFolder)), Workspace.Paths.TransfersDirectory, StringComparison.OrdinalIgnoreCase))
            Directory.CreateDirectory(Workspace.Paths.TransfersDirectory);
        // A digest that was typed but is malformed is an error, never silently replaced by the no-digest route.
        if (TrustedDigest.Trim().Length > 0 && !TrustedDigestValid) throw new ConfigurationException("The trusted archive SHA-256 must be 64 hexadecimal characters. Correct it, or clear it and acknowledge that none is available.");
        var digest = TrustedDigestValid ? TrustedDigest.Trim() : null;
        if (digest is null) Workspace.Logger.Warn("Transfer", "Backup restored without an independently received archive digest, at the engineer's explicit acknowledgement.");
        return new WorkspaceBackup(Workspace.Paths).RestoreSeparate(BackupFile, RestoreFolder, digest);
    }

    private async Task Adopt()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Copy the verified restored evidence into this workspace's empty evidence folder?\n\nEvery file is checked again after copying. No sign-in, plan approval or session is restored, and the next plan still needs fresh evidence. Restart the tool afterwards.",
            "Adopt restored evidence", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;
        LastResult = await Workspace.ExportAsync(() => new WorkspaceBackup(Workspace.Paths).AdoptInto(RestoreFolder));
    }
    private bool CanTransfer() => Workspace.Idle && !Workspace.IsConnected && Workspace.ApplicationSetup is null;

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

    public override void Refresh() { OnPropertyChanged(nameof(Text)); OnPropertyChanged(nameof(SupportPreview)); }
}
