using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Identity;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ProfileWritesTests : IDisposable
{
    private readonly TempRoot _root=new();
    private readonly EvidenceStore _store;
    public ProfileWritesTests() => _store=new(_root.Paths,NullLog.Instance);
    public void Dispose()=>_root.Dispose();
    private PartnerCustomerPreview Preview()=>PartnerCustomerImport.Preview(Encoding.UTF8.GetBytes("Microsoft ID,Company name\n"+TestData.TenantB+",Second client\n"),_store.LoadProfiles());
    [Fact] public void Import_adds_selected_new_clients_and_preserves_existing_values()
    {
        var existing=TestData.Profile();existing.Notes=null!;_store.SaveProfiles([existing]);
        var result=_store.ImportCustomers(Preview(),[TestData.TenantB],DateTimeOffset.UtcNow);
        Assert.Equal(2,result.Count);Assert.Equal(existing.Id,result[0].Id);Assert.Null(_store.LoadProfiles()[0].Notes);
        Assert.Equal(TestData.TenantB,result[1].TenantId);
    }
    [Fact] public void An_intervening_save_invalidates_import_and_preserves_newer_bytes()
    {
        _store.SaveProfiles([TestData.Profile()]);var preview=Preview();var newer=TestData.Profile();newer.Notes="Another engineer's edit";
        new EvidenceStore(_root.Paths,NullLog.Instance).SaveProfiles([newer]);var bytes=File.ReadAllBytes(_root.Paths.ProfilesFile);
        Assert.Throws<ConfigurationException>(()=>_store.ImportCustomers(preview,[TestData.TenantB],DateTimeOffset.UtcNow));
        Assert.Equal(bytes,File.ReadAllBytes(_root.Paths.ProfilesFile));
    }
    [Fact] public void All_writers_share_the_lease_during_comparison_and_replacement()
    {
        _store.SaveProfiles([TestData.Profile()]);var expected=_store.LoadProfiles();var other=new EvidenceStore(_root.Paths,NullLog.Instance);
        _store.UpdateProfiles(expected,current=>
        {
            Assert.Throws<ConfigurationException>(()=>other.SaveProfiles([TestData.Profile(TestData.TenantB)]));
            Assert.Throws<ConfigurationException>(()=>other.UpdateProfiles(expected,c=>c));
            current[0].Notes="Lease holder";return current;
        });
        Assert.Equal("Lease holder",Assert.Single(_store.LoadProfiles()).Notes);
        other.UpdateProfiles(_store.LoadProfiles(),c=>c); // Lease retires after atomic replacement.
    }
    [Fact] public void A_stale_edit_cannot_erase_another_clients_new_profile()
    {
        _store.SaveProfiles([TestData.Profile()]);var stale=_store.LoadProfiles();_store.SaveProfiles([TestData.Profile(),TestData.Profile(TestData.TenantB)]);
        Assert.Throws<ConfigurationException>(()=>_store.UpdateProfiles(stale,c=>[c[0]]));Assert.Equal(2,_store.LoadProfiles().Count);
    }
    [Fact] public void Cancellation_does_not_commit_or_leave_a_held_lease()
    {
        _store.SaveProfiles([TestData.Profile()]);var preview=Preview();using var stop=new CancellationTokenSource();stop.Cancel();
        Assert.Throws<OperationCanceledException>(()=>_store.ImportCustomers(preview,[TestData.TenantB],DateTimeOffset.UtcNow,stop.Token));
        Assert.Single(_store.LoadProfiles());Assert.Equal(2,_store.ImportCustomers(preview,[TestData.TenantB],DateTimeOffset.UtcNow).Count);
    }
    [Fact] public void Empty_selection_is_explicit_and_does_not_write_profiles()
    {
        var preview=Preview();Assert.Throws<ConfigurationException>(()=>_store.ImportCustomers(preview,[],DateTimeOffset.UtcNow));Assert.False(File.Exists(_root.Paths.ProfilesFile));
    }
    [Fact] public void An_occupied_profile_file_is_not_an_empty_portfolio()
    {
        Directory.CreateDirectory(_root.Paths.ProfilesFile);Assert.Throws<ConfigurationException>(()=>_store.LoadClientProfiles());
    }
    [Fact] public void Portfolio_count_is_bounded_before_profile_materialisation()
    {
        File.WriteAllText(_root.Paths.ProfilesFile,"["+string.Join(",",Enumerable.Repeat("null",5001))+"]");
        var ex=Assert.Throws<ConfigurationException>(()=>_store.LoadClientProfiles());Assert.Contains("5,000",ex.Message);
    }
    [Fact] public void A_callback_failure_keeps_original_bytes_and_releases_its_lease()
    {
        _store.SaveProfiles([TestData.Profile()]);var before=File.ReadAllBytes(_root.Paths.ProfilesFile);
        Assert.Throws<InvalidOperationException>(()=>_store.UpdateProfiles(_store.LoadProfiles(),_=>throw new InvalidOperationException("synthetic")));
        Assert.Equal(before,File.ReadAllBytes(_root.Paths.ProfilesFile));_store.UpdateProfiles(_store.LoadProfiles(),p=>p);
    }
    [Fact] public void Backups_exclude_the_released_profile_lease_but_keep_profiles()
    {
        _store.SaveProfiles([TestData.Profile()]);var file=new WorkspaceBackup(_root.Paths).Create();
        using var zip=System.IO.Compression.ZipFile.OpenRead(file);
        Assert.Contains(zip.Entries,e=>e.FullName=="data/profiles.json");Assert.DoesNotContain(zip.Entries,e=>e.Name=="profiles-write.lock");
    }
    [Fact] public void Backup_refuses_while_the_profile_transaction_is_active()
    {
        _store.SaveProfiles([TestData.Profile()]);
        _store.UpdateProfiles(_store.LoadProfiles(),p=>
        {var ex=Assert.Throws<ConfigurationException>(()=>new WorkspaceBackup(_root.Paths).Create());Assert.Contains("in progress",ex.Message);return p;});
    }
    [Fact] public void An_oversized_replacement_cannot_make_the_profile_store_unreadable()
    {
        _store.SaveProfiles([TestData.Profile()]);var before=File.ReadAllBytes(_root.Paths.ProfilesFile);
        Assert.Throws<ConfigurationException>(()=>_store.UpdateProfiles(_store.LoadProfiles(),p=>{p[0].Notes=new string('x',8*1024*1024);return p;}));
        Assert.Equal(before,File.ReadAllBytes(_root.Paths.ProfilesFile));
    }
}
