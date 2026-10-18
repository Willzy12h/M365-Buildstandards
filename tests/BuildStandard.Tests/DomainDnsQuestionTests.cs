using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Exchange;
using Xunit;

namespace BuildStandard.Tests;

public sealed class DomainDnsQuestionTests
{
    [Theory]
    [InlineData("example.invalid","example.invalid")]
    [InlineData("_spf.Example.INVALID.","_spf.example.invalid")]
    [InlineData("selector._domainkey.example.invalid","selector._domainkey.example.invalid")]
    [InlineData("spf-1.example.invalid","spf-1.example.invalid")]
    public void Reference_retains_original_and_canonical_query_identity(string original,string canonical)
    {
        var reference=DomainDnsName.Reference(original);
        Assert.Equal(original,reference.Original);Assert.Equal(canonical,reference.Canonical);
    }
    [Theory]
    [InlineData("")][InlineData(".")][InlineData("localhost")][InlineData("a..invalid")]
    [InlineData("example.invalid..")] [InlineData(" example.invalid")][InlineData("example.invalid\n")]
    [InlineData("*.example.invalid")][InlineData("https://example.invalid")][InlineData("127.0.0.1")]
    [InlineData("::1")][InlineData("-spf.example.invalid")][InlineData("spf-.example.invalid")]
    [InlineData("%{d}.example.invalid")][InlineData("example';exit#.invalid")]
    [InlineData("éxample.invalid")][InlineData("exa mple.invalid")][InlineData("example.invalid/path")]
    public void Non_literal_or_ambiguous_reference_is_refused_before_a_query(string value)
        =>Assert.Throws<ConfigurationException>(()=>DomainDnsName.Reference(value));
    [Fact] public void Name_and_label_limits_are_enforced_without_truncation()
    {
        var largest=string.Join('.',new string('a',63),new string('b',63),new string('c',63),new string('d',61));
        Assert.Equal(253,DomainDnsName.Reference(largest+'.').Canonical.Length);
        Assert.Throws<ConfigurationException>(()=>DomainDnsName.Reference(largest+'d'));
        Assert.Throws<ConfigurationException>(()=>DomainDnsName.Reference(new string('a',64)+".invalid"));
    }
    [Theory] [InlineData(DomainDnsType.TXT)] [InlineData(DomainDnsType.MX)] [InlineData(DomainDnsType.CNAME)]
    public void Only_typed_canonical_questions_are_constructed(DomainDnsType type)
    {
        var question=new DomainDnsQuestion("_spf.Example.INVALID.",type);
        Assert.Equal("_spf.example.invalid",question.Name);Assert.Equal(type,question.Type);
        Assert.Throws<ConfigurationException>(()=>new DomainDnsQuestion("example.invalid",(DomainDnsType)99));
    }
    [Fact] public void Utility_reference_support_does_not_weaken_historical_mail_domain_validation()
    {
        Assert.Equal("_spf.example.invalid",DomainDnsName.Reference("_spf.example.invalid").Canonical);
        Assert.Throws<ConfigurationException>(()=>MailDomain.Validate("_spf.example.invalid"));
    }
}
