using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Windows Hello is configured end to end rather than merely enabled. These tests pin the decisions that are easy to
/// invert by accident: the composition rules where 1 requires a character class and 2 forbids it, a PIN that never
/// expires, and the settings deliberately left out because configuring them would weaken or break the policy.
/// </summary>
public class WindowsHelloRecipeTests
{
    private const string File_ = "2026.09.10.json";

    private static string Path_ =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", File_));

    private static bool Available => File.Exists(Path_);

    private static JsonObject Hello()
    {
        var catalogue = StandardsLoader.Parse(File.ReadAllText(Path_), File_);
        return catalogue.FindControl("CFG-WIN-003")!.Payload!;
    }

    private static JsonNode? Setting(string uriEnding) =>
        Hello()["omaSettings"]!.AsArray().OfType<JsonObject>()
            .SingleOrDefault(s => s["omaUri"]!.GetValue<string>().EndsWith(uriEnding, StringComparison.Ordinal))?["value"];

    [Fact]
    public void Hello_is_enabled_on_a_hardware_security_device()
    {
        Assert.True(Available, "The Windows Hello catalogue fixture must be available.");

        Assert.True(Setting("/Policies/UsePassportForWork")!.GetValue<bool>());
        Assert.True(Setting("/Policies/RequireSecurityDevice")!.GetValue<bool>());
        Assert.True(Setting("/Policies/ExcludeSecurityDevices/TPM12")!.GetValue<bool>());
    }

    /// <summary>A forced provisioning prompt at first sign-in disrupts an Autopilot build; recovery avoids re-enrolment.</summary>
    [Fact]
    public void Provisioning_is_not_forced_and_a_forgotten_pin_can_be_recovered()
    {
        Assert.True(Available, "The Windows Hello catalogue fixture must be available.");

        Assert.True(Setting("/Policies/DisablePostLogonProvisioning")!.GetValue<bool>());
        Assert.True(Setting("/Policies/EnablePinRecovery")!.GetValue<bool>());
    }

    [Fact]
    public void Pin_composition_requires_a_digit_and_permits_letters_without_demanding_them()
    {
        Assert.True(Available, "The Windows Hello catalogue fixture must be available.");

        Assert.Equal(8, Setting("PINComplexity/MinimumPINLength")!.GetValue<int>());
        Assert.Equal(1, Setting("PINComplexity/Digits")!.GetValue<int>());

        // Unset means allowed but not required. Setting 2 would forbid the class outright, which is the inversion
        // this test exists to catch.
        foreach (var rule in new[] { "PINComplexity/LowercaseLetters", "PINComplexity/UppercaseLetters", "PINComplexity/SpecialCharacters" })
            Assert.Null(Setting(rule));

        // A maximum only constrains a user who wants a longer PIN.
        Assert.Null(Setting("PINComplexity/MaximumPINLength"));
    }

    /// <summary>Zero is never. A non-zero expiry would reintroduce the rotation this policy deliberately avoids.</summary>
    [Fact]
    public void The_pin_never_expires_and_history_is_not_kept()
    {
        Assert.True(Available, "The Windows Hello catalogue fixture must be available.");

        Assert.Equal(0, Setting("PINComplexity/Expiration")!.GetValue<int>());
        Assert.Null(Setting("PINComplexity/History"));
    }

    [Fact]
    public void Biometrics_and_security_keys_are_permitted_at_the_device_scope()
    {
        Assert.True(Available, "The Windows Hello catalogue fixture must be available.");

        Assert.True(Setting("Biometrics/UseBiometrics")!.GetValue<bool>());
        Assert.Equal(1, Setting("SecurityKey/UseSecurityKeyForSignin")!.GetValue<int>());

        // These two are not tenant-scoped; putting them under the tenant node silently does nothing.
        foreach (var setting in Hello()["omaSettings"]!.AsArray().OfType<JsonObject>())
        {
            var uri = setting["omaUri"]!.GetValue<string>();
            if (uri.Contains("Biometrics/", StringComparison.Ordinal) || uri.Contains("SecurityKey/", StringComparison.Ordinal))
                Assert.DoesNotContain("{{tenantId}}", uri, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Each of these breaks something when configured: phone sign-in is retired, the two hardware-dependent settings
    /// disable biometrics on devices that do not support them, and cloud Kerberos trust is for hybrid tenants.
    /// </summary>
    [Theory]
    [InlineData("UsePhoneSignIn")]
    [InlineData("FacialFeaturesUseEnhancedAntiSpoofing")]
    [InlineData("UseCloudTrustForOnPremAuth")]
    public void Settings_that_would_harm_a_cloud_only_tenant_are_absent(string name)
    {
        Assert.True(Available, "The Windows Hello catalogue fixture must be available.");

        Assert.DoesNotContain(name, Hello().ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_setting_is_a_device_scoped_passport_policy_with_a_unique_path()
    {
        Assert.True(Available, "The Windows Hello catalogue fixture must be available.");
        var settings = Hello()["omaSettings"]!.AsArray().OfType<JsonObject>().ToList();

        Assert.Equal(10, settings.Count);
        Assert.Equal(settings.Count, settings.Select(s => s["omaUri"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        foreach (var setting in settings)
            Assert.StartsWith("./Device/Vendor/MSFT/PassportForWork/", setting["omaUri"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
