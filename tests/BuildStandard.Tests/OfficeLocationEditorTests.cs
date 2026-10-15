using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using Xunit;

namespace BuildStandard.Tests;

public sealed class OfficeLocationEditorTests
{
    [Fact]
    public void Office_name_edits_preserve_existing_instance_keys_and_JSON_round_trip()
    {
        var editor = new OfficeLocationEditor();
        editor.Load([new OfficeLocation { Key = "LONDON", Name = "Original office", IpRanges = ["8.8.8.8/32"] }]);
        editor.Rows[0].Name = "Renamed office";
        var result = Assert.Single(editor.Read());
        Assert.Equal("LONDON", result.Key); Assert.Equal("Renamed office", result.Name);
        var roundTrip = ToolkitJson.Deserialize<List<OfficeLocation>>(ToolkitJson.Serialize(editor.Read()));
        editor.Load(roundTrip); Assert.Equal("LONDON", Assert.Single(editor.Rows).Key);
    }

    [Theory]
    [InlineData("10.0.0.0/8")][InlineData("0.0.0.0/0")][InlineData("not a CIDR")]
    public void Invalid_ranges_cannot_be_saved_from_the_grid(string range)
    {
        var editor = new OfficeLocationEditor();
        editor.Load([new OfficeLocation { Key = "A", Name = "Office", IpRanges = ["8.8.8.8/32"] }]);
        editor.Rows[0].Ranges = range;
        Assert.Throws<ConfigurationException>(() => editor.Read());
    }

    [Fact]
    public void Duplicate_office_names_are_refused_and_new_context_replaces_unsaved_rows()
    {
        var editor = new OfficeLocationEditor();
        editor.Load([new OfficeLocation { Key = "A", Name = "Office A", IpRanges = ["8.8.8.8/32"] },
            new OfficeLocation { Key = "B", Name = "Office B", IpRanges = ["1.1.1.1/32"] }]);
        editor.Rows[1].Name = "Office A";
        Assert.Throws<ConfigurationException>(() => editor.Read());
        editor.Load(null); Assert.Empty(editor.Rows); Assert.Empty(editor.Read());
    }
}
