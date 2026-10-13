using BuildStandard.App.Services.Reader;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ReaderPolicyTests
{
    private static ReaderNavigationPolicy Policy() => new(["CA-001", "index"], ["https://learn.microsoft.com/en-us/entra/test", "https://example.org/reference"]);
    [Theory]
    [InlineData(ReaderNavigationPolicy.DocumentUrl)]
    [InlineData(ReaderNavigationPolicy.DocumentUrl + "#CA-001")]
    [InlineData(ReaderNavigationPolicy.DocumentUrl + "#index")]
    public void Exact_document_and_recorded_anchors_are_admitted(string url) => Assert.True(Policy().AllowsDocument(url));
    [Theory]
    [InlineData("https://reader.buildstandard.example/catalogue.html?x=1")]
    [InlineData("https://reader.buildstandard.example/%63atalogue.html")]
    [InlineData("https://reader.buildstandard.example/catalogue.html#missing")]
    [InlineData("https://reader.buildstandard.example:444/catalogue.html")]
    [InlineData("https://user@reader.buildstandard.example/catalogue.html")]
    [InlineData("https://reader.buildstandard.example.evil/catalogue.html")]
    [InlineData("file:///catalogue.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://reader.buildstandard.example/profile/preferences.json")]
    public void Deceptive_extra_or_unknown_targets_are_refused(string url) => Assert.False(Policy().AllowsDocument(url));
    [Fact] public void Resources_admit_only_the_fragment_free_document()
    { Assert.True(Policy().AllowsResource(ReaderNavigationPolicy.DocumentUrl)); Assert.False(Policy().AllowsResource(ReaderNavigationPolicy.DocumentUrl + "#CA-001")); }
    [Fact] public void Only_a_current_user_selected_recorded_official_reference_can_be_pending()
    {
        const string url = "https://learn.microsoft.com/en-us/entra/test";
        Assert.True(Policy().AllowsReference(url, true, true));
        Assert.False(Policy().AllowsReference(url, false, true)); Assert.False(Policy().AllowsReference(url, true, false));
        Assert.False(Policy().AllowsReference("https://learn.microsoft.com/en-us/entra/other", true, true));
        Assert.False(Policy().AllowsReference("https://example.org/reference", true, true));
        Assert.False(Policy().AllowsReference("https://learn.microsoft.com.evil/en-us/entra/test", true, true));
    }
}
