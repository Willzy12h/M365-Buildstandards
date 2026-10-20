using Xunit;
namespace BuildStandard.Tests;
public sealed class PortableValidationScriptTests
{
    [Fact]
    public void Bomless_windows_powershell_package_validator_is_ascii()
    {
        // Windows PowerShell 5.1 decodes BOM-less UTF-8 as ANSI. A literal middle dot in the
        // status assertion previously became two characters and rejected correct CLI output.
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../build/Test-Portable.ps1"));
        Assert.All(File.ReadAllBytes(file), value => Assert.InRange(value, (byte)0, (byte)127));
    }
}
