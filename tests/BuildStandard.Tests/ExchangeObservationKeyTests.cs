using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;

public sealed class ExchangeObservationKeyTests
{
    [Fact] public void Key_is_stable_prefixed_and_not_a_service_guid()
    {var key=ExchangeObservationKey.InboxRule(TestData.TenantA,"184467440737095516150000");Assert.Equal(key,ExchangeObservationKey.InboxRule(TestData.TenantA,"184467440737095516150000"));Assert.Equal("sha256:3aceac0e8249d88f16810ae07faedc684496726ee5c6ed4a74e498ec3a6a473a",key);Assert.Matches("^sha256:[0-9a-f]{64}$",key);Assert.False(Guid.TryParse(key,out _));}
    [Fact] public void Opaque_source_identity_is_not_case_folded_or_numeric_coerced()
    {Assert.NotEqual(ExchangeObservationKey.InboxRule(TestData.TenantA,"001"),ExchangeObservationKey.InboxRule(TestData.TenantA,"1"));Assert.NotEqual(ExchangeObservationKey.InboxRule(TestData.TenantA,"RuleA"),ExchangeObservationKey.InboxRule(TestData.TenantA,"rulea"));}
    [Fact] public void Explicit_null_is_distinct_from_false_and_empty_and_fields_cannot_shift()
    {var unknown=ExchangeObservationKey.MailboxPermission(TestData.TenantA,"FullAccess","SID","S-1-0-0",["FullAccess"],null,null);Assert.NotEqual(unknown,ExchangeObservationKey.MailboxPermission(TestData.TenantA,"FullAccess","SID","S-1-0-0",["FullAccess"],false,false));Assert.NotEqual(ExchangeObservationKey.CalendarPermission(TestData.TenantA,"a|b","User","c"),ExchangeObservationKey.CalendarPermission(TestData.TenantA,"a","User","b|c"));}
    [Fact] public void Same_named_sources_and_different_namespaces_are_not_joined()
    {Assert.NotEqual(ExchangeObservationKey.GroupMember(TestData.TenantA,"SID","same"),ExchangeObservationKey.GroupMember(TestData.TenantA,"SMTP","same"));Assert.NotEqual(ExchangeObservationKey.InboxRule(TestData.TenantA,"rule"),ExchangeObservationKey.InboxRule(TestData.TenantB,"rule"));}
    [Fact] public void One_trace_keeps_different_recipients_and_observation_times()
    {var first=ExchangeObservationKey.MessageTrace(TestData.TenantA,"one@synthetic.example","2026-10-20T00:00:00Z");Assert.NotEqual(first,ExchangeObservationKey.MessageTrace(TestData.TenantA,"two@synthetic.example","2026-10-20T00:00:00Z"));Assert.NotEqual(first,ExchangeObservationKey.MessageTrace(TestData.TenantA,"one@synthetic.example","2026-10-20T00:00:01Z"));}
    [Fact] public void Rights_arrays_have_unambiguous_boundaries_and_preserve_exact_values()
    {Assert.NotEqual(ExchangeObservationKey.MailboxPermission(TestData.TenantA,"FullAccess","User","raw",["A","B"],false,false),ExchangeObservationKey.MailboxPermission(TestData.TenantA,"FullAccess","User","raw",["A,B"],false,false));}
    [Fact] public void Unknown_rights_and_a_returned_empty_list_remain_distinct()
    {Assert.NotEqual(ExchangeObservationKey.MailboxPermission(TestData.TenantA,"FullAccess","User","raw",null,null,null),ExchangeObservationKey.MailboxPermission(TestData.TenantA,"FullAccess","User","raw",[],null,null));}
    [Theory] [InlineData("")] [InlineData(" ")] [InlineData("bad\nidentity")]
    public void Required_source_identity_is_not_fabricated(string rule)
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeObservationKey.InboxRule(TestData.TenantA,rule));}
    [Fact] public void Source_limits_are_enforced_without_truncation()
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeObservationKey.InboxRule(TestData.TenantA,new string('a',1025)));Assert.ThrowsAny<ToolkitException>(()=>ExchangeObservationKey.MailboxPermission(TestData.TenantA,"FullAccess","User","raw",Enumerable.Repeat("right",101).ToArray(),false,false));}
    [Fact] public void No_random_guid_is_created_for_missing_mailbox_or_trace_identity()
    {Assert.ThrowsAny<ToolkitException>(()=>ExchangeObservationKey.InboxRule("not-guid","opaque"));Assert.ThrowsAny<ToolkitException>(()=>ExchangeObservationKey.MessageTrace("","recipient","2026-10-20T00:00:00Z"));Assert.ThrowsAny<ToolkitException>(()=>ExchangeObservationKey.MessageTrace(TestData.TenantA,"recipient","not-time"));}
}
