using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ExchangeTargetSelectionTests
{
    private static string SourceGuid(int number) => $"12345678-1234-1234-1234-{number:000000000000}";

    [Fact]
    public void Fifty_explicit_targets_preserve_order_without_name_resolution_or_truncation()
    {
        var expected = Enumerable.Range(1, 50).Select(SourceGuid).Reverse().ToArray();
        var actual = ExchangeTargetSelection.Parse(string.Join(',', expected));
        Assert.Equal(expected, actual);
        Assert.Equal(1849, string.Join(',', actual).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("display name")]
    [InlineData("mailbox@synthetic.example")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData(" 12345678-1234-1234-1234-000000000001")]
    [InlineData("12345678-1234-1234-1234-000000000001,")]
    [InlineData("12345678-1234-1234-1234-000000000001\n12345678-1234-1234-1234-000000000002")]
    [InlineData("12345678-1234-1234-1234-000000000001;Get-EXOMailbox")]
    public void Missing_names_coercion_and_command_text_are_refused(string input) =>
        Assert.Throws<ConfigurationException>(() => ExchangeTargetSelection.Parse(input));

    [Fact]
    public void Duplicate_or_fifty_first_target_is_refused_instead_of_silently_cut_off()
    {
        Assert.Throws<ConfigurationException>(() => ExchangeTargetSelection.Parse(SourceGuid(1) + "," + SourceGuid(1)));
        Assert.Throws<ConfigurationException>(() => ExchangeTargetSelection.Parse(string.Join(',', Enumerable.Range(1, 51).Select(SourceGuid))));
    }
}
