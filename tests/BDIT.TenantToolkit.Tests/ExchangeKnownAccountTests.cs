using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Engine.Exchange;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExchangeKnownAccountTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Friendly engineer name")]
    [InlineData("engineer@example.invalid\nGet-Mailbox")]
    public void Integrated_process_refuses_unusable_account_before_it_can_launch(string account)
    {
        var ex = Assert.Throws<ConfigurationException>(() => ExchangeCaptureRunner.CreateStartInfo(
            "synthetic-powershell.exe", "owned read template.ps1", "new result.json", account, true));
        Assert.Contains("confirmed sign-in name", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Friendly engineer name")]
    public async Task Capture_entry_point_refuses_unpinned_account_before_platform_files_or_processes(string account)
    {
        var ex = await Assert.ThrowsAsync<ConfigurationException>(() => new ExchangeCaptureRunner().CaptureAsync(
            TestData.TenantA, "example.invalid", account, false, null, CancellationToken.None));
        Assert.Contains("confirmed sign-in name", ex.Message);
    }

    [Fact]
    public void Verified_account_is_passed_as_one_data_argument_with_no_policy_or_command_string()
    {
        var start = ExchangeCaptureRunner.CreateStartInfo("synthetic-powershell.exe", "owned read template.ps1",
            "new result.json", " ENGINEER@example.invalid ", true);
        Assert.False(start.UseShellExecute);
        Assert.Contains("-Integrated", start.ArgumentList); Assert.Contains("-IncludePurview", start.ArgumentList);
        var list = start.ArgumentList.ToList(); var index = list.IndexOf("-UserPrincipalName");
        Assert.True(index >= 0); Assert.Equal("ENGINEER@example.invalid", list[index + 1]);
        Assert.Contains("owned read template.ps1", list); Assert.DoesNotContain("-Command", list);
        Assert.DoesNotContain("-ExecutionPolicy", list); Assert.DoesNotContain("-Credential", list);
    }
}
