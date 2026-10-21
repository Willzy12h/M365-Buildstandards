using BuildStandard.Core;
using BuildStandard.Graph.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageDownloadAdmissionTests
{
    [Theory][InlineData("https://reports.office.com/data/download/opaque?secret=synthetic")][InlineData("https://reports.office.com:443/data/download/opaque%2Bpart?secret=synthetic")]
    public void Only_the_fixed_https_download_surface_is_admitted(string location)
    {
        var uri=new Uri(location);Assert.Same(uri,UsageDownloadAdmission.Require(uri));
    }
    [Theory][InlineData("http://reports.office.com/data/download/opaque")][InlineData("https://reports.office.com.evil.example/data/download/opaque")]
    [InlineData("https://reports.office.com:444/data/download/opaque")][InlineData("https://operator@reports.office.com/data/download/opaque")]
    [InlineData("https://reports.office.com/data/download/opaque#fragment")][InlineData("https://reports.office.com/data/download/")]
    [InlineData("https://reports.office.com/data/download/../outside")][InlineData("https://reports.office.com/data/download/%2e%2e/opaque")]
    [InlineData("https://reports.office.com/data/download/opaque%2foutside")][InlineData("https://reports.office.com/data/download/opaque%5coutside")]
    [InlineData("https://reports.office.com/data/download/%252e%252e/opaque")][InlineData("https://reports.office.com/data/download/opaque%00")]
    [InlineData("https://reports.office.com/data/download/opaque%3fother")][InlineData("https://reports.office.com/data/download/opaque\\other")]
    [InlineData("https://reports.office.com/Data/download/opaque")][InlineData("https://reports.office.com/data/download//opaque")]
    [InlineData("https://127.0.0.1/data/download/opaque")][InlineData("/data/download/opaque")]
    public void Unsafe_or_ambiguous_locations_are_refused_without_disclosing_the_url(string location)
    {
        var error=Assert.ThrowsAny<ToolkitException>(()=>UsageDownloadAdmission.Require(new Uri(location,UriKind.RelativeOrAbsolute)));
        Assert.DoesNotContain("opaque",error.Message);Assert.DoesNotContain("secret",error.Message);
    }
    [Fact] public void Missing_location_is_not_a_successful_empty_download()=>Assert.ThrowsAny<ToolkitException>(()=>UsageDownloadAdmission.Require(null));
}
