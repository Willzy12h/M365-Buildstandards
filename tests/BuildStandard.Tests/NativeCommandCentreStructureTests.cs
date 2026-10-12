using System.Xml.Linq;
using Xunit;
namespace BuildStandard.Tests;
public sealed class NativeCommandCentreStructureTests
{
    private static XDocument Source(string name) => XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/BuildStandard.App", name)));
    [Fact]
    public void Shared_native_accent_uses_approved_teal_direction()
    {
        var key = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key";
        var brush = Source("App.xaml").Descendants().Single(e => (string?)e.Attribute(key) == "Accent");
        var colour = (string)brush.Attribute("Color")!;
        var red = Convert.ToInt32(colour.Substring(1, 2), 16);
        var green = Convert.ToInt32(colour.Substring(3, 2), 16);
        var blue = Convert.ToInt32(colour.Substring(5, 2), 16);
        Assert.True(red < green / 2 && Math.Abs(green - blue) <= 8, "Accent must remain teal, not the old blue direction.");
    }
    [Fact]
    public void Primary_group_navigation_is_horizontal_instead_of_the_permanent_rail()
    {
        var name = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name";
        var groups = Source("MainWindow.xaml").Descendants().SingleOrDefault(e => (string?)e.Attribute(name) == "PrimaryGroups");
        Assert.NotNull(groups);
        Assert.Contains(groups.Descendants(), e => e.Name.LocalName == "StackPanel" && (string?)e.Attribute("Orientation") == "Horizontal");
    }

    [Fact]
    public void Command_centre_named_static_resources_are_defined_before_native_loading()
    {
        var key = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key";
        var names = Source("App.xaml").Descendants().Select(e => (string?)e.Attribute(key)).Where(k => k is not null).ToHashSet(StringComparer.Ordinal);
        var attributes = Source("Views/HomeView.xaml").Descendants().Attributes().Select(a => a.Value);
        foreach (var value in attributes)
        {
            var match = System.Text.RegularExpressions.Regex.Match(value, @"^\{StaticResource ([A-Za-z][A-Za-z0-9]*)\}$");
            if (match.Success) Assert.Contains(match.Groups[1].Value, names);
        }
    }

    [Fact]
    public void Working_shell_keeps_the_evidence_origin_and_original_capture_time_visible_across_pages()
    {
        var name = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name";
        var evidence = Source("MainWindow.xaml").Descendants().SingleOrDefault(e => (string?)e.Attribute(name) == "EvidenceSourceStatus");
        Assert.NotNull(evidence); Assert.Equal("{Binding EvidenceStatus}", (string?)evidence.Attribute("Text"));
        Assert.DoesNotContain(evidence.Ancestors(), e => e.Name.LocalName == "DataTemplate");
    }
    [Fact]
    public void Teal_accent_meets_AA_on_white_and_the_observed_native_expander_header()
    {
        var key=XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml")+"Key";
        var colour=(string)Source("App.xaml").Descendants().Single(e=>(string?)e.Attribute(key)=="Accent").Attribute("Color")!;
        static double Linear(double value) => value <= .04045 ? value/12.92 : Math.Pow((value+.055)/1.055,2.4);
        var luminance=.2126*Linear(Convert.ToInt32(colour.Substring(1,2),16)/255d)
            +.7152*Linear(Convert.ToInt32(colour.Substring(3,2),16)/255d)
            +.0722*Linear(Convert.ToInt32(colour.Substring(5,2),16)/255d);
        foreach(var surface in new[]{255d,221d}) // #DDDDDD measured by Windows38317966833; thresholds are unchanged.
        {
            var background=Linear(surface/255d);
            Assert.True((background+.05)/(luminance+.05)>=4.5,"Ordinary native text must meet WCAG AA on its observed surface.");
        }
    }

}
