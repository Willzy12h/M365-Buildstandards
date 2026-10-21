using BuildStandard.Engine.Exchange;
using Xunit;

namespace BuildStandard.Tests;

public sealed class SpfPolicyAnalysisTests
{
    [Fact] public void Literal_references_are_canonical_but_policy_and_term_text_remain_original()
    {
        const string input="v=spf1 include:_SPF.example.invalid. redirect=other.invalid";
        var policy=SpfPolicyAnalysis.Parse(input);Assert.Equal(input,policy.Original);Assert.Empty(policy.Problems);
        Assert.Equal(new[]{"_spf.example.invalid","other.invalid"},policy.Terms.Select(t=>t.Reference));
        Assert.All(policy.Terms,t=>Assert.True(t.Reachable));Assert.All(policy.Terms,t=>Assert.True(t.DnsDependent));
        Assert.Equal("include:_SPF.example.invalid.",policy.Terms[0].Original);
    }
    [Fact] public void Terminal_all_makes_later_mechanisms_and_any_redirect_inactive()
    {
        var policy=SpfPolicyAnalysis.Parse("v=spf1 redirect=ignored.invalid include:first.invalid -all include:later.invalid");
        Assert.False(policy.Terms[0].Reachable);Assert.True(policy.Terms[1].Reachable);Assert.True(policy.Terms[2].Reachable);Assert.False(policy.Terms[3].Reachable);
        Assert.Single(policy.Terms,t=>t.Reachable&&t.DnsDependent);
    }
    [Fact] public void Repeated_includes_are_distinct_term_occurrences_not_distinct_dns_questions()
    {
        var policy=SpfPolicyAnalysis.Parse("v=spf1 include:same.invalid include:same.invalid -all");
        Assert.Equal(2,policy.Terms.Count(t=>t.DnsDependent&&t.Reachable));Assert.Single(policy.Terms.Where(t=>t.Reference!=null).Select(t=>t.Reference).Distinct());
    }
    [Theory] [InlineData("include:%{d}.invalid")][InlineData("redirect=%{d}")][InlineData("a")][InlineData("mx:example.invalid/24//64")]
    [InlineData("exists:example.invalid")][InlineData("ptr:example.invalid")][InlineData("exp=explain.invalid")]
    public void Message_context_and_macros_are_not_followed_as_literal_network_references(string text)
    {
        var policy=SpfPolicyAnalysis.Parse("v=spf1 "+text);var term=Assert.Single(policy.Terms);
        Assert.True(term.NeedsContext);Assert.Null(term.Reference);Assert.Empty(policy.Problems);
    }
    [Theory] [InlineData("ip4:192.0.2.1")][InlineData("ip4:192.0.2.0/24")][InlineData("ip6:2001:db8::/32")]
    [InlineData("-all")][InlineData("~all")][InlineData("?all")][InlineData("+all")]
    public void Literal_address_terms_and_all_have_no_dns_cost(string text)
    {
        var policy=SpfPolicyAnalysis.Parse("v=spf1 "+text);Assert.Empty(policy.Problems);var term=Assert.Single(policy.Terms);
        Assert.False(term.DnsDependent);Assert.False(term.NeedsContext);Assert.Null(term.Reference);
    }
    [Theory] [InlineData("v=spf2 -all")][InlineData("v=spf1 include:")][InlineData("v=spf1 redirect=")]
    [InlineData("v=spf1 ip4:999.0.0.1")][InlineData("v=spf1 ip4:192.0.2.1/33")][InlineData("v=spf1 ip6:2001:db8::/129")]
    [InlineData("v=spf1 all:example.invalid")][InlineData("v=spf1 unknown:example.invalid")]
    [InlineData("v=spf1 include:example.invalid/path")][InlineData("v=spf1 redirect=a.invalid redirect=b.invalid")]
    [InlineData("v=spf1 +redirect=a.invalid")][InlineData("v=spf1 include:a.invalid\t-all")]
    [InlineData("v=spf1 a/33")][InlineData("v=spf1 mx//129")]
    public void Malformed_syntax_is_a_finding_never_a_successful_policy(string input)
        =>Assert.NotEmpty(SpfPolicyAnalysis.Parse(input).Problems);
    [Fact] public void Unknown_modifiers_are_retained_as_ignored_not_executed_or_counted()
    {
        var policy=SpfPolicyAnalysis.Parse("v=spf1 vendor=value -all");Assert.Empty(policy.Problems);
        Assert.Equal("vendor=value",policy.Terms[0].Original);Assert.False(policy.Terms[0].Reachable);Assert.False(policy.Terms[0].DnsDependent);
    }
    [Fact] public void A_modifier_named_all_is_not_a_terminal_all_mechanism()
    {
        var policy=SpfPolicyAnalysis.Parse("v=spf1 all=vendor redirect=target.invalid");
        Assert.Empty(policy.Problems);Assert.True(policy.Terms[1].Reachable);
    }
    [Fact] public void A_macro_delimiter_is_not_a_modifier_separator()
    {
        var policy=SpfPolicyAnalysis.Parse("v=spf1 include:%{l=}.example.invalid");
        Assert.Empty(policy.Problems);Assert.True(Assert.Single(policy.Terms).NeedsContext);Assert.Null(policy.Terms[0].Reference);
    }
    [Theory] [InlineData("192.0.2.010")][InlineData("192.0.2.01")][InlineData("0192.0.2.1")][InlineData("192.00.2.1")]
    public void Leading_zero_ipv4_octets_are_not_spf_decimal_literals(string address)
        =>Assert.NotEmpty(SpfPolicyAnalysis.Parse("v=spf1 ip4:"+address+" -all").Problems);
    [Theory] [InlineData("0.0.0.0")][InlineData("255.255.255.255")][InlineData("192.0.2.10")]
    public void Canonical_decimal_ipv4_octets_remain_supported(string address)
        =>Assert.Empty(SpfPolicyAnalysis.Parse("v=spf1 ip4:"+address+" -all").Problems);
    [Fact] public void An_ipv6_interface_scope_is_not_a_valid_spf_literal()
        =>Assert.NotEmpty(SpfPolicyAnalysis.Parse("v=spf1 ip6:fe80::1%1 -all").Problems);
}
