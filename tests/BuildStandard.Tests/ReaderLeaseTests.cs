using BuildStandard.App.Services.Reader;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ReaderLeaseTests
{
    [Fact] public void Retirement_requires_browser_exit_and_never_touches_another_instance()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new ReaderSessionLease(root, Guid.NewGuid()); using var second = new ReaderSessionLease(root, Guid.NewGuid());
            File.WriteAllText(Path.Combine(first.ProfileDirectory, "Preferences.json"), "synthetic");
            Assert.False(first.Retire(false)); Assert.True(Directory.Exists(first.ProfileDirectory));
            Assert.True(first.Retire(true)); Assert.False(Directory.Exists(first.SessionDirectory));
            Assert.True(Directory.Exists(second.ProfileDirectory)); Assert.True(second.Retire(true));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public void A_substituted_profile_link_cannot_delete_unrelated_data()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "keep.txt"), "synthetic retained");
        try
        {
            using var lease = new ReaderSessionLease(root, Guid.NewGuid());
            Directory.Delete(lease.ProfileDirectory); Directory.CreateSymbolicLink(lease.ProfileDirectory, outside);
            Assert.False(lease.Retire(true)); Assert.Equal("synthetic retained", File.ReadAllText(Path.Combine(outside, "keep.txt")));
            Directory.Delete(lease.ProfileDirectory);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); Directory.Delete(outside, true); }
    }
    [Fact] public void Missing_ownership_marker_refuses_cleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var lease = new ReaderSessionLease(root, Guid.NewGuid());
            var markers = Directory.GetFiles(lease.SessionDirectory, ".owner"); Assert.Single(markers);
            File.Delete(markers[0]); Assert.False(lease.Retire(true)); Assert.True(Directory.Exists(lease.SessionDirectory));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
