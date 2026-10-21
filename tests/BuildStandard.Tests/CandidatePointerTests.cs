using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidatePointerTests
{
    [Theory][InlineData("/a~1b/~0c", "a/b", "~c")][InlineData("/a/~01", "a", "~1")]
    public void Escaped_segments_are_decoded_once(string path,string first,string second) => Assert.Equal(new[]{first,second}, CandidatePointer.Parse(path));
    [Theory][InlineData("")][InlineData("a")][InlineData("/")][InlineData("/a/")][InlineData("/a~")][InlineData("/a~2")][InlineData("/a\nb")]
    public void Malformed_paths_are_refused(string path) => Assert.Throws<ConfigurationException>(()=>CandidatePointer.Parse(path));
    [Fact]public void Null_is_a_present_value_not_a_missing_property()
    {var root=JsonNode.Parse("{\"a\":null}")!.AsObject();Assert.Null(CandidatePointer.Read(root,"/a"));Assert.Throws<ConfigurationException>(()=>CandidatePointer.Read(root,"/b"));}
    [Fact]public void Replacing_a_value_clones_the_proposal_and_checks_original()
    {var root=JsonNode.Parse("{\"a\":{\"b\":1}}")!.AsObject();var proposed=JsonNode.Parse("[2,1]");CandidatePointer.Replace(root,"/a/b",JsonValue.Create(1),proposed);proposed!.AsArray().Add(3);Assert.Equal("[2,1]",root["a"]!["b"]!.ToJsonString());Assert.Throws<ConfigurationException>(()=>CandidatePointer.Replace(root,"/a/b",JsonValue.Create(1),null));}
    [Fact]public void Arrays_cannot_be_indexed_but_can_be_replaced_atomically()
    {var root=JsonNode.Parse("{\"a\":[1,2]}")!.AsObject();Assert.Throws<ConfigurationException>(()=>CandidatePointer.Read(root,"/a/0"));CandidatePointer.Replace(root,"/a",JsonNode.Parse("[1,2]"),JsonNode.Parse("[2,1]"));Assert.Equal("[2,1]",root["a"]!.ToJsonString());}
    [Theory][InlineData("/a","/a")][InlineData("/a","/A")][InlineData("/a","/a/b")][InlineData("/a/b","/a")]
    public void Duplicate_and_ancestor_paths_are_refused(string first,string second)=>Assert.Throws<ConfigurationException>(()=>CandidatePointer.RequireNonOverlapping(new[]{first,second}));
    [Fact]public void Distinct_siblings_and_escaped_slashes_do_not_overlap()=>CandidatePointer.RequireNonOverlapping(new[]{"/a/b","/a/bc","/a~1b"});
    [Fact]public void Lookup_does_not_guess_property_casing()=>Assert.Throws<ConfigurationException>(()=>CandidatePointer.Read(new JsonObject{["a"]=1},"/A"));
}
