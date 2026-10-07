using System.IO;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Engine.Evidence;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The Settings page walks an engineer through receiving a workspace one step at a time, says what is missing, accepts a
/// pasted checksum line, and warns when a loaded fingerprint may have travelled with the backup it is meant to check.
/// </summary>
public sealed class SettingsTransferFlowTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;

    public SettingsTransferFlowTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        _shell = new ShellViewModel(workspace);
    }

    public void Dispose()
    {
        _logger.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Receiving_a_workspace_is_guided_step_by_step_and_accepts_a_pasted_checksum_line()
    {
        using var source = new TempRoot(); File.WriteAllText(source.Paths.ProfilesFile, "[]");
        var zip = new WorkspaceBackup(source.Paths).Create();
        var vm = _shell.Page<SettingsViewModel>();

        Assert.Empty(vm.TransferBlockedReason);
        Assert.StartsWith("Step 1", vm.ReceiveNextStep);
        vm.BackupFile = zip;
        Assert.StartsWith("Step 2", vm.ReceiveNextStep);
        vm.TrustedDigest = "not a fingerprint";
        Assert.False(vm.TrustedDigestValid);
        Assert.Contains("not a SHA-256", vm.DigestStatus);
        vm.TrustedDigest = WorkspaceBackup.ArchiveDigest(zip).ToUpperInvariant() + "  " + Path.GetFileName(zip);
        Assert.True(vm.TrustedDigestValid);
        Assert.StartsWith("Step 3: adopt", vm.ReceiveNextStep);
        Assert.True(vm.AdoptRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void A_fingerprint_loaded_from_beside_the_backup_is_flagged_as_possibly_travelling_with_it()
    {
        using var source = new TempRoot(); File.WriteAllText(source.Paths.ProfilesFile, "[]");
        var zip = new WorkspaceBackup(source.Paths).Create();
        var vm = _shell.Page<SettingsViewModel>();
        vm.BackupFile = zip;
        vm.LoadDigestFrom(zip + ".sha256");
        Assert.True(vm.TrustedDigestValid);
        Assert.Contains("same folder as the backup", vm.DigestSource);

        var separate = Path.Combine(_root.Root, "received-separately.sha256");
        File.Copy(zip + ".sha256", separate);
        vm.LoadDigestFrom(separate);
        Assert.DoesNotContain("same folder", vm.DigestSource);
        Assert.Contains("received-separately.sha256", vm.DigestSource);
    }

    [Fact]
    public void Adoption_explains_why_it_is_unavailable_when_the_workspace_already_holds_evidence()
    {
        File.WriteAllText(_root.Paths.ProfilesFile, "[]");
        var vm = _shell.Page<SettingsViewModel>();
        vm.Refresh();
        Assert.Contains("already holds evidence", vm.AdoptBlockedReason);
        Assert.False(vm.AdoptRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void Support_preview_adds_only_the_sections_ticked()
    {
        var vm = _shell.Page<SettingsViewModel>();
        Assert.DoesNotContain("diagnostics.txt", vm.SupportPreview);
        vm.IncludeTimeouts = true;
        Assert.Contains("Graph read timeout (seconds): 120", vm.SupportPreview);
        Assert.DoesNotContain("[Environment]", vm.SupportPreview);
    }
}
