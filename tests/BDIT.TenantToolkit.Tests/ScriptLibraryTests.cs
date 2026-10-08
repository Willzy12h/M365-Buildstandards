using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Engine.Scripts;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The script library (INT-072): only pinned, registered files load; read-only scripts cannot carry a write; form
/// values are typed and checked; and a copied script carries every value as an inert literal.
/// </summary>
public sealed class ScriptLibraryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static string LibraryDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts"));

    private static Dictionary<string, byte[]> LibraryFiles() =>
        Directory.EnumerateFiles(LibraryDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(LibraryDirectory, f).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);

    private static ScriptEntry Item(string id) => ScriptCatalogue.Shipped.Find(id);

    private static ScriptBinding Bind(string id, params (string Name, string? Value)[] fields) =>
        ScriptInputs.Bind(Item(id).Manifest, fields.ToDictionary(f => f.Name, f => f.Value), Now);

    /// <summary>Re-pins a changed file the way build/Update-ScriptRegistry.py does, so a test can reach the next check.</summary>
    private static void Repin(Dictionary<string, byte[]> files, string manifestPath)
    {
        var manifest = Encoding.UTF8.GetString(files[manifestPath]);
        var scriptPath = System.Text.Json.JsonDocument.Parse(manifest).RootElement.GetProperty("scriptPath").GetString()!;
        var oldSha = System.Text.Json.JsonDocument.Parse(manifest).RootElement.GetProperty("scriptSha256").GetString()!;
        var newSha = ScriptCatalogue.Sha256(files[scriptPath]);
        files[manifestPath] = Encoding.UTF8.GetBytes(manifest.Replace(oldSha, newSha, StringComparison.Ordinal));
        var registry = Encoding.UTF8.GetString(files[ScriptCatalogue.RegistryPath]);
        var document = System.Text.Json.JsonDocument.Parse(registry);
        foreach (var entry in document.RootElement.GetProperty("scripts").EnumerateArray())
        {
            if (entry.GetProperty("manifest").GetString() != manifestPath) continue;
            registry = registry.Replace(entry.GetProperty("manifestSha256").GetString()!, ScriptCatalogue.Sha256(files[manifestPath]), StringComparison.Ordinal)
                .Replace(entry.GetProperty("scriptSha256").GetString()!, newSha, StringComparison.Ordinal);
        }
        files[ScriptCatalogue.RegistryPath] = Encoding.UTF8.GetBytes(registry);
    }

    [Fact]
    public void The_shipped_library_loads_and_every_item_is_read_only_and_unverified()
    {
        var entries = ScriptCatalogue.Shipped.Entries;
        Assert.Equal(29, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal(ScriptMode.ReadOnly, e.Manifest.Mode);
            Assert.Equal(ScriptLiveStatus.Unverified, e.Manifest.LiveStatus);
            Assert.Equal(new[] { "exchangeOnline" }, e.Manifest.Resources);
        });
        Assert.Equal(entries.Count, entries.Select(e => e.Manifest.Id).Distinct().Count());
    }

    [Fact]
    public void The_embedded_library_matches_the_files_in_the_repository()
    {
        if (!Directory.Exists(LibraryDirectory)) return;
        var fromDisk = ScriptCatalogue.Load(LibraryFiles());
        Assert.Equal(ScriptCatalogue.Shipped.Entries.Select(e => e.ManifestSha256), fromDisk.Entries.Select(e => e.ManifestSha256));
    }

    [Fact]
    public void Search_matches_every_word_against_names_and_keywords()
    {
        Assert.Equal(new[] { "exo.message-trace" }, ScriptCatalogue.Shipped.Search("trace NDR").Select(e => e.Manifest.Id));
        Assert.Contains(ScriptCatalogue.Shipped.Search("send as"), e => e.Manifest.Id == "exo.mailbox-permissions");
        Assert.Empty(ScriptCatalogue.Shipped.Search("no such thing"));
    }

    /// <summary>The second Exchange Online pack (eleven read-only items) on top of the first ten.</summary>
    public static TheoryData<string> SecondPack => new()
    {
        "exo.group-members", "exo.transport-rules", "exo.connectors", "exo.domains-dkim", "exo.mobile-devices", "exo.mailbox-audit",
        "exo.mailbox-holds", "exo.protection-policies", "exo.resource-mailboxes", "exo.send-on-behalf", "exo.archive-mailboxes"
    };

    [Theory]
    [MemberData(nameof(SecondPack))]
    public void Each_second_pack_item_is_read_only_documents_roles_and_limits_and_says_it_is_unverified(string id)
    {
        var entry = Item(id);
        var m = entry.Manifest;
        Assert.Equal(ScriptMode.ReadOnly, m.Mode);
        Assert.Equal(ScriptLiveStatus.Unverified, m.LiveStatus);
        Assert.NotEmpty(m.Roles);
        Assert.Contains(m.Limitations, l => l.Contains("not yet tested in a tenant", StringComparison.Ordinal));
        Assert.Contains(m.Limitations, l => l.Contains("roles listed", StringComparison.Ordinal) && l.Contains("not been confirmed", StringComparison.Ordinal));
        Assert.Contains(m.Limitations, l => l.Contains("shown as Unknown, never as False", StringComparison.Ordinal));
        Assert.Equal("Notes", m.OutputSchema.Columns[^1]);

        // Windows PowerShell 5.1 has no null-coalescing, null-conditional or ternary operators.
        foreach (var token in new[] { "??", "?.", "?[" }) Assert.DoesNotContain(token, entry.Script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\)\s*\?\s*[^\s]+\s*:\s", entry.Script);
        // Every tenant command is a Get- read; nothing installs a module, changes policy or bypasses execution policy.
        foreach (var token in new[] { "Install-Module", "Import-Module", "ExecutionPolicy", "Set-", "New-", "Remove-", "Enable-", "Disable-", "Add-", "Update-" })
            Assert.DoesNotContain(token, entry.Script.Replace("Set-StrictMode", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SecondPack))]
    public void Each_second_pack_item_says_when_a_value_was_not_measured_and_when_a_read_was_bounded(string id)
    {
        var entry = Item(id);
        Assert.Contains("BDIT:UNKNOWN", entry.Script, StringComparison.Ordinal);
        // A limit on what is read, by mailbox, member or row, is always reported when it is reached.
        if (entry.Manifest.Parameters.Any(p => p.Name.StartsWith("Max", StringComparison.Ordinal)))
            Assert.Contains("BDIT:PARTIAL", entry.Script, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SecondPack))]
    public void Each_second_pack_item_binds_its_defaults_and_produces_a_copy_with_its_body_unchanged(string id)
    {
        var entry = Item(id);
        var fields = id == "exo.group-members" ? new[] { ("Group", (string?)"staff@contoso.example") } : Array.Empty<(string, string?)>();
        var binding = Bind(id, fields);
        Assert.True(binding.IsValid, string.Join(" ", binding.Problems));
        foreach (var p in entry.Manifest.Parameters.Where(p => p.Default is not null && p.Type != ScriptParameterType.Boolean))
            Assert.Contains(binding.Arguments, a => a.Name == p.Name);
        var script = ScriptCopy.Generate(entry, binding, new ScriptCopyTarget("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "Contoso"), Now);
        Assert.Contains(entry.Script.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), script.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Second_pack_forms_check_their_values()
    {
        Assert.Contains("Groups is required.", Bind("exo.group-members").Problems);
        Assert.Contains(Bind("exo.group-members", ("Group", "staff@contoso.example"), ("MaxMembers", "0")).Problems, p => p.Contains("from 1 to 20000", StringComparison.Ordinal));
        Assert.Contains(Bind("exo.domains-dkim", ("Domain", "not a domain")).Problems, p => p.Contains("domain such as example.com", StringComparison.Ordinal));
        Assert.Equal(new[] { "contoso.example" }, ((ScriptTextList)Bind("exo.domains-dkim", ("Domain", "Contoso.example")).Arguments.Single(a => a.Name == "Domain").Value).Values);
        Assert.Contains(Bind("exo.protection-policies", ("PolicyType", "AntiSpam, SafeLinks")).Problems, p => p.Contains("choose one of", StringComparison.Ordinal));
        Assert.Equal(new[] { "AntiSpam", "OutboundSpam", "AntiPhish", "AntiMalware" },
            ((ScriptTextList)Bind("exo.protection-policies").Arguments.Single(a => a.Name == "PolicyType").Value).Values);
        Assert.Contains(Bind("exo.resource-mailboxes", ("ResourceType", "Desk")).Problems, p => p.Contains("choose one of", StringComparison.Ordinal));
        Assert.Contains(Bind("exo.mobile-devices", ("StaleDays", "0")).Problems, p => p.Contains("from 1 to 3650", StringComparison.Ordinal));
        // A stale filter is optional and has no default, so leaving it blank lists every device.
        Assert.DoesNotContain(Bind("exo.mobile-devices").Arguments, a => a.Name == "StaleDays");
        // Unticking a filter that defaults to off leaves it out; mailbox lists are bounded.
        Assert.DoesNotContain(Bind("exo.archive-mailboxes").Arguments, a => a.Name == "OnlyWithArchive");
        var tooMany = string.Join(",", Enumerable.Range(1, 51).Select(i => $"m{i}@contoso.example"));
        Assert.Contains(Bind("exo.send-on-behalf", ("Mailbox", tooMany)).Problems, p => p.Contains("at most 50 values", StringComparison.Ordinal));
    }

    [Fact]
    public void Second_pack_identity_items_never_treat_a_name_or_alias_as_an_exact_identity()
    {
        // Astra's #46 review: an identity is joined only on an exact identity, never on a Name or display name.
        foreach (var id in new[] { "exo.group-members", "exo.send-on-behalf" })
        {
            var script = Item(id).Script;
            var exact = System.Text.RegularExpressions.Regex.Match(script, @"\$unique = @\(([^)]*)\)").Groups[1].Value;
            Assert.Equal("'PrimarySmtpAddress', 'DistinguishedName', 'ExternalDirectoryObjectId', 'Guid'", exact);
            Assert.Contains("'Unresolved'", script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Search_finds_the_second_pack_by_its_terms()
    {
        Assert.Contains(ScriptCatalogue.Shipped.Search("DKIM"), e => e.Manifest.Id == "exo.domains-dkim");
        Assert.Contains(ScriptCatalogue.Shipped.Search("litigation hold"), e => e.Manifest.Id == "exo.mailbox-holds");
        Assert.Contains(ScriptCatalogue.Shipped.Search("room booking"), e => e.Manifest.Id == "exo.resource-mailboxes");
        Assert.Contains(ScriptCatalogue.Shipped.Search("anti-phishing"), e => e.Manifest.Id == "exo.protection-policies");
        Assert.Contains(ScriptCatalogue.Shipped.Search("ActiveSync"), e => e.Manifest.Id == "exo.mobile-devices");
        Assert.Contains(ScriptCatalogue.Shipped.Search("transport rule"), e => e.Manifest.Id == "exo.transport-rules");
        Assert.Contains(ScriptCatalogue.Shipped.Search("smart host"), e => e.Manifest.Id == "exo.connectors");
        Assert.Contains(ScriptCatalogue.Shipped.Search("distribution list owners"), e => e.Manifest.Id == "exo.group-members");
        Assert.Contains(ScriptCatalogue.Shipped.Search("audit bypass"), e => e.Manifest.Id == "exo.mailbox-audit");
        Assert.Contains(ScriptCatalogue.Shipped.Search("online archive"), e => e.Manifest.Id == "exo.archive-mailboxes");
        Assert.Contains(ScriptCatalogue.Shipped.Search("GrantSendOnBehalfTo"), e => e.Manifest.Id == "exo.send-on-behalf");
    }

    /// <summary>The third Exchange Online pack (eight read-only items), held to the same rules as the second.</summary>
    public static TheoryData<string> ThirdPack => new()
    {
        "exo.mailbox-protocols", "exo.remote-domains", "exo.retention-policies", "exo.mailbox-policies", "exo.sharing", "exo.journal-rules",
        "exo.quarantine-policies", "exo.mail-contacts"
    };

    [Theory]
    [MemberData(nameof(ThirdPack))]
    public void Each_third_pack_item_is_read_only_documents_roles_and_limits_and_says_it_is_unverified(string id) =>
        Each_second_pack_item_is_read_only_documents_roles_and_limits_and_says_it_is_unverified(id);

    [Theory]
    [MemberData(nameof(ThirdPack))]
    public void Each_third_pack_item_says_when_a_value_was_not_measured_and_when_a_read_was_bounded(string id)
    {
        Each_second_pack_item_says_when_a_value_was_not_measured_and_when_a_read_was_bounded(id);
        // A bounded read asks for one more than its limit, so reaching the limit can be told apart from a complete list.
        var entry = Item(id);
        foreach (var max in entry.Manifest.Parameters.Where(p => p.Name.StartsWith("Max", StringComparison.Ordinal)))
            Assert.Contains("-ResultSize ($" + max.Name + " + 1)", entry.Script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"-ResultSize\s+(Unlimited|\$Max\w+\))", entry.Script);
    }

    [Theory]
    [MemberData(nameof(ThirdPack))]
    public void Each_third_pack_item_binds_its_defaults_and_produces_a_copy_with_its_body_unchanged(string id) =>
        Each_second_pack_item_binds_its_defaults_and_produces_a_copy_with_its_body_unchanged(id);

    [Fact]
    public void Third_pack_forms_check_their_values()
    {
        Assert.Contains(Bind("exo.mailbox-protocols", ("MaxMailboxes", "0")).Problems, p => p.Contains("from 1 to 10000", StringComparison.Ordinal));
        Assert.Contains(Bind("exo.mailbox-protocols", ("Mailbox", "not an address!")).Problems, p => p.Contains("email address or alias", StringComparison.Ordinal));
        Assert.Equal("2000", Bind("exo.mailbox-protocols").Arguments.Single(a => a.Name == "MaxMailboxes").Value.ToString());
        Assert.Contains(Bind("exo.mailbox-policies", ("PolicyType", "OwaMailbox, Teams")).Problems, p => p.Contains("choose one of", StringComparison.Ordinal));
        Assert.Equal(new[] { "OwaMailbox", "MobileDeviceMailbox" }, ((ScriptTextList)Bind("exo.mailbox-policies").Arguments.Single(a => a.Name == "PolicyType").Value).Values);
        Assert.Contains(Bind("exo.sharing", ("Kind", "Federation")).Problems, p => p.Contains("choose one of", StringComparison.Ordinal));
        Assert.Contains(Bind("exo.mail-contacts", ("RecipientType", "Mailbox")).Problems, p => p.Contains("choose one of", StringComparison.Ordinal));
        Assert.Contains(Bind("exo.mail-contacts", ("MaxRecipients", "10001")).Problems, p => p.Contains("from 1 to 10000", StringComparison.Ordinal));
        // Tick boxes that default to ticked are passed; ones that default to off are left out until ticked.
        Assert.Contains(Bind("exo.retention-policies").Arguments, a => a.Name == "IncludeUnlinkedTags");
        Assert.Contains(Bind("exo.quarantine-policies").Arguments, a => a.Name == "IncludeGlobalSettings");
        Assert.DoesNotContain(Bind("exo.journal-rules").Arguments, a => a.Name == "OnlyEnabled");
        Assert.DoesNotContain(Bind("exo.remote-domains").Arguments, a => a.Name == "OnlyAutoForwardAllowed");
    }

    [Fact]
    public void Third_pack_keeps_unreturned_values_unknown_and_joins_retention_tags_only_on_exact_identities()
    {
        // SMTP AUTH: an empty mailbox setting follows the organisation; a property that was not returned is Unknown.
        var protocols = Item("exo.mailbox-protocols").Script;
        Assert.Contains("'FollowsOrganisation'", protocols, StringComparison.Ordinal);
        Assert.Contains("$target.PSObject.Properties['SmtpClientAuthenticationDisabled']", protocols, StringComparison.Ordinal);
        Assert.Contains("Get-TransportConfig", protocols, StringComparison.Ordinal);
        // Retention tag links: matched only on the tag's exact Name, Identity or DistinguishedName, case-sensitively.
        var retention = Item("exo.retention-policies").Script;
        Assert.Contains("@('Name', 'Identity', 'DistinguishedName')", retention, StringComparison.Ordinal);
        Assert.Contains("-ccontains $link", retention, StringComparison.Ordinal);
        Assert.Contains("'Unresolved'", retention, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayName", retention, StringComparison.Ordinal);
        // Policy settings use the protection policy rules: null is Unknown, empty is NotSet, only an empty list is 0.
        foreach (var id in new[] { "exo.mailbox-policies", "exo.quarantine-policies" })
        {
            var script = Item(id).Script;
            Assert.Contains("$Item.PSObject.Properties[$name] -and $null -ne $Item.$name", script, StringComparison.Ordinal);
            Assert.Contains("$value = 'NotSet'", script, StringComparison.Ordinal);
        }
        // No item joins on a display name.
        foreach (var id in new[] { "exo.mailbox-protocols", "exo.remote-domains", "exo.retention-policies", "exo.mailbox-policies", "exo.sharing", "exo.journal-rules", "exo.quarantine-policies", "exo.mail-contacts" })
            Assert.DoesNotMatch(@"DisplayName'\)\s*-(c)?eq", Item(id).Script);
    }

    [Fact]
    public void Search_finds_the_third_pack_by_its_terms()
    {
        Assert.Contains(ScriptCatalogue.Shipped.Search("SMTP AUTH"), e => e.Manifest.Id == "exo.mailbox-protocols");
        Assert.Contains(ScriptCatalogue.Shipped.Search("POP IMAP"), e => e.Manifest.Id == "exo.mailbox-protocols");
        Assert.Contains(ScriptCatalogue.Shipped.Search("auto forward"), e => e.Manifest.Id == "exo.remote-domains");
        Assert.Contains(ScriptCatalogue.Shipped.Search("retention tag"), e => e.Manifest.Id == "exo.retention-policies");
        Assert.Contains(ScriptCatalogue.Shipped.Search("OWA policy"), e => e.Manifest.Id == "exo.mailbox-policies");
        Assert.Contains(ScriptCatalogue.Shipped.Search("free/busy"), e => e.Manifest.Id == "exo.sharing");
        Assert.Contains(ScriptCatalogue.Shipped.Search("journal"), e => e.Manifest.Id == "exo.journal-rules");
        Assert.Contains(ScriptCatalogue.Shipped.Search("quarantine"), e => e.Manifest.Id == "exo.quarantine-policies");
        Assert.Contains(ScriptCatalogue.Shipped.Search("mail contact"), e => e.Manifest.Id == "exo.mail-contacts");
    }

    [Fact]
    public void Archive_status_reads_no_archive_size()
    {
        // The brief: no unbounded size calls. Archive size needs one statistics read per mailbox, so it is not made.
        var entry = Item("exo.archive-mailboxes");
        Assert.DoesNotContain("Get-EXOMailboxStatistics", entry.Script, StringComparison.Ordinal);
        Assert.Contains(entry.Manifest.Limitations, l => l.Contains("Archive size is not reported", StringComparison.Ordinal));
    }

    [Fact]
    public void A_changed_script_is_refused_until_it_is_pinned_again()
    {
        if (!Directory.Exists(LibraryDirectory)) return;
        var files = LibraryFiles();
        var path = "exchange-online/Get-InboxRuleReport.ps1";
        files[path] = files[path].Concat(Encoding.ASCII.GetBytes("\n# reviewed comment\n")).ToArray();
        var ex = Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files));
        Assert.Contains("does not match the SHA-256", ex.Message, StringComparison.Ordinal);

        Repin(files, "exchange-online/Get-InboxRuleReport.json");
        Assert.Equal(29, ScriptCatalogue.Load(files).Entries.Count);
    }

    [Theory]
    [InlineData("\nSet-Mailbox -Identity $Mailbox -ForwardingSmtpAddress 'x@example.com'\n", "cannot use Set-Mailbox")]
    [InlineData("\nRemove-InboxRule -Mailbox $Mailbox -Identity x\n", "cannot use Remove-InboxRule")]
    [InlineData("\nInvoke-Expression 'Get-Date'\n", "cannot use Invoke-")]
    [InlineData("\n& ([scriptblock]::Create('Get-Date'))\n", "[scriptblock]::Create")]
    [InlineData("\n#requires -Version 7.0\n", "#requires")]
    public void A_pinned_read_only_script_still_cannot_carry_a_write_or_dynamic_code(string addition, string expected)
    {
        if (!Directory.Exists(LibraryDirectory)) return;
        var files = LibraryFiles();
        var path = "exchange-online/Get-InboxRuleReport.ps1";
        files[path] = files[path].Concat(Encoding.ASCII.GetBytes(addition)).ToArray();
        Repin(files, "exchange-online/Get-InboxRuleReport.json");
        var ex = Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unregistered_files_and_paths_outside_a_category_are_refused()
    {
        if (!Directory.Exists(LibraryDirectory)) return;
        var files = LibraryFiles();
        files["exchange-online/Get-Extra.ps1"] = Encoding.ASCII.GetBytes("param()\n");
        Assert.Contains("not in the registry", Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files)).Message, StringComparison.Ordinal);

        files = LibraryFiles();
        files["../outside.ps1"] = Encoding.ASCII.GetBytes("param()\n");
        Assert.Contains("not a contained library path", Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files)).Message, StringComparison.Ordinal);

        files = LibraryFiles();
        var manifest = "exchange-online/Get-InboxRuleReport.json";
        files[manifest] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files[manifest])
            .Replace("\"scriptPath\": \"exchange-online/Get-InboxRuleReport.ps1\"", "\"scriptPath\": \"exchange-online/../x.ps1\"", StringComparison.Ordinal));
        Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files));
    }

    [Theory]
    [InlineData("\"liveStatus\": \"unverified\"", "\"liveStatus\": \"unverified\", \"runAsAdmin\": true")]
    [InlineData("\"liveStatus\": \"unverified\"", "\"liveStatus\": \"verified\"")]
    [InlineData("\"mode\": \"readOnly\"", "\"mode\": \"change\"")]
    [InlineData("\"mode\": \"readOnly\"", "\"mode\": 0")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,")]
    [InlineData("\"resources\": [\n    \"exchangeOnline\"\n  ]", "\"resources\": [\n    \"graph\"\n  ]")]
    [InlineData("\"prerequisites\": ", "\"ignored\": null, \"prerequisites\": ")]
    public void Manifests_are_strict(string find, string replace)
    {
        if (!Directory.Exists(LibraryDirectory)) return;
        var files = LibraryFiles();
        var manifest = "exchange-online/Get-InboxRuleReport.json";
        var text = Encoding.UTF8.GetString(files[manifest]);
        Assert.Contains(find, text, StringComparison.Ordinal);
        files[manifest] = Encoding.UTF8.GetBytes(text.Replace(find, replace, StringComparison.Ordinal));
        Repin(files, manifest);
        Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files));
    }

    [Fact]
    public void Blank_optional_fields_are_left_out_and_defaults_are_shown()
    {
        var binding = Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"), ("RecipientAddress", "alex@contoso.example"),
            ("Subject", "  "), ("FromIP", ""), ("Status", null));
        Assert.True(binding.IsValid, string.Join(" ", binding.Problems));
        Assert.Equal(new[] { "StartDate", "EndDate", "RecipientAddress", "SubjectMatch", "MaxRows" }, binding.Arguments.Select(a => a.Name));
        Assert.Equal(new ScriptNumber(1000), binding.Arguments.Single(a => a.Name == "MaxRows").Value);
    }

    [Fact]
    public void Message_trace_needs_a_search_term_and_a_bounded_recent_range()
    {
        var none = Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"));
        Assert.Contains("Enter at least one of: Senders, Recipients, Subject, Message ID.", none.Problems);

        Assert.Contains("From (UTC) is required.", Bind("exo.message-trace", ("EndDate", "2026-10-08"), ("Subject", "x")).Problems);
        Assert.Contains("Choose a range of 90 days or fewer.", Bind("exo.message-trace", ("StartDate", "2026-07-01"), ("EndDate", "2026-10-08"), ("Subject", "x")).Problems);
        Assert.Contains("From (UTC) can be at most 90 days ago.", Bind("exo.message-trace", ("StartDate", "2026-07-01"), ("EndDate", "2026-07-02"), ("Subject", "x")).Problems);
        Assert.Contains("To (UTC) cannot be in the future.", Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-20"), ("Subject", "x")).Problems);
        Assert.Contains("To (UTC) must be on or after From (UTC).", Bind("exo.message-trace", ("StartDate", "2026-10-05"), ("EndDate", "2026-10-01"), ("Subject", "x")).Problems);
        Assert.Contains(Bind("exo.message-trace", ("StartDate", "1 Oct"), ("EndDate", "2026-10-08"), ("Subject", "x")).Problems, p => p.Contains("YYYY-MM-DD", StringComparison.Ordinal));
    }

    [Fact]
    public void Several_values_are_split_checked_and_bounded()
    {
        var binding = Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"),
            ("SenderAddress", "o'brien@contoso.example,\na@b.co.uk; A@B.co.uk"), ("Status", "delivered, FAILED"));
        Assert.True(binding.IsValid, string.Join(" ", binding.Problems));
        Assert.Equal(new ScriptTextList(new[] { "o'brien@contoso.example", "a@b.co.uk" }).Values, ((ScriptTextList)binding.Arguments.Single(a => a.Name == "SenderAddress").Value).Values);
        Assert.Equal(new[] { "Delivered", "Failed" }, ((ScriptTextList)binding.Arguments.Single(a => a.Name == "Status").Value).Values);

        var tooMany = string.Join(",", Enumerable.Range(1, 21).Select(i => $"u{i}@contoso.example"));
        Assert.Contains(Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"), ("SenderAddress", tooMany)).Problems,
            p => p.Contains("at most 20 values", StringComparison.Ordinal));
        Assert.Contains(Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"), ("Subject", "x"), ("Status", "Delivered,Lost")).Problems,
            p => p.Contains("choose one of", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("User", "alex@contoso", "sign-in name")]
    [InlineData("User", "alex@contoso.example\nSet-Mailbox", "line breaks")]
    [InlineData("User", "alex‮@contoso.example", "line breaks")]
    [InlineData("User", "\"alex\"@contoso.example", "sign-in name")]
    [InlineData("MaxMailboxes", "0", "from 1 to 10000")]
    [InlineData("MaxMailboxes", "1e3", "whole number")]
    [InlineData("Unknown", "x", "not a field")]
    public void Bad_values_are_refused_with_a_reason(string field, string value, string reason)
    {
        var fields = new List<(string, string?)> { (field, value) };
        if (field != "User") fields.Add(("User", "alex@contoso.example"));
        var binding = Bind("exo.user-mailbox-access", fields.ToArray());
        Assert.Contains(binding.Problems, p => p.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void Tick_boxes_use_their_default_and_an_untick_is_left_out()
    {
        var defaults = Bind("exo.mailbox-permissions");
        Assert.Equal(new[] { "FullAccess", "SendAs", "SendOnBehalf", "MaxMailboxes", "MaxRows" }, defaults.Arguments.Select(a => a.Name));
        var unticked = Bind("exo.mailbox-permissions", ("SendAs", "false"), ("IncludeInherited", "true"));
        Assert.Equal(new[] { "FullAccess", "SendOnBehalf", "IncludeInherited", "MaxMailboxes", "MaxRows" }, unticked.Arguments.Select(a => a.Name));
    }

    [Fact]
    public void A_copied_script_carries_hostile_values_as_inert_literals()
    {
        var entry = Item("exo.message-trace");
        var binding = Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"),
            ("Subject", "Don’t '; Remove-Mailbox x; '$(calc) `n \"quoted\""), ("SenderAddress", "o'brien@contoso.example"));
        var script = ScriptCopy.Generate(entry, binding, new ScriptCopyTarget("3F2504E0-4F89-11D3-9A0C-0305E82C3301", "Contoso\n#evil", "admin@contoso.example"), Now);

        Assert.Contains("        Subject = 'Don’’t ''; Remove-Mailbox x; ''$(calc) `n \"quoted\"'\n", script, StringComparison.Ordinal);
        Assert.Contains("        SenderAddress = @('o''brien@contoso.example')\n", script, StringComparison.Ordinal);
        Assert.Contains("$expectedTenantId = '3f2504e0-4f89-11d3-9a0c-0305e82c3301'\n", script, StringComparison.Ordinal);
        Assert.Contains("# Tenant:      Contoso #evil (3f2504e0", script, StringComparison.Ordinal);
        Assert.Contains("# Type:        Read only. Makes no changes.", script, StringComparison.Ordinal);
        Assert.Contains("#requires -Modules @{ ModuleName = 'ExchangeOnlineManagement'; ModuleVersion = '3.7.0' }", script, StringComparison.Ordinal);
        Assert.Contains(entry.Script.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'), script, StringComparison.Ordinal);
        Assert.Contains("Nothing was read.", script, StringComparison.Ordinal);
        Assert.Contains("Disconnect-ExchangeOnline -Confirm:$false", script, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "-Credential", "-AccessToken", "-Certificate", "Install-Module", "ExecutionPolicy", "Invoke-Expression" })
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_copy_is_refused_for_an_incomplete_form_or_a_bad_target()
    {
        var entry = Item("exo.message-trace");
        var incomplete = Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"));
        Assert.Throws<ConfigurationException>(() => ScriptCopy.Generate(entry, incomplete, new ScriptCopyTarget(), Now));

        var valid = Bind("exo.message-trace", ("StartDate", "2026-10-01"), ("EndDate", "2026-10-08"), ("Subject", "x"));
        Assert.Throws<ConfigurationException>(() => ScriptCopy.Generate(entry, valid, new ScriptCopyTarget("contoso"), Now));
        Assert.Throws<ConfigurationException>(() => ScriptCopy.Generate(entry, valid, new ScriptCopyTarget(Account: "admin@contoso.example' ; calc ; '"), Now));
        Assert.Contains("$expectedTenantId = ''\n", ScriptCopy.Generate(entry, valid, new ScriptCopyTarget(), Now), StringComparison.Ordinal);
    }

    [Fact]
    public void A_copied_script_checks_the_reviewed_account_after_sign_in_and_before_any_read()
    {
        // AST-20261008-07: the confirmation names the account, so the script must refuse any other one in the same tenant.
        var entry = Item("exo.mailbox-inventory");
        var script = ScriptCopy.Generate(entry, Bind("exo.mailbox-inventory"), new ScriptCopyTarget("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "Contoso", "Admin@Contoso.example"), Now);

        Assert.Contains("$signInAs = 'Admin@Contoso.example'\n", script, StringComparison.Ordinal);
        Assert.Contains("# Account:     Admin@Contoso.example (any other account is refused before anything is read)", script, StringComparison.Ordinal);
        var connect = script.IndexOf("Connect-ExchangeOnline @connect", StringComparison.Ordinal);
        var guard = script.IndexOf("try {", connect, StringComparison.Ordinal);
        var tenant = script.IndexOf("if ($expectedTenantId -and", StringComparison.Ordinal);
        var account = script.IndexOf("if ($signInAs -and -not [string]::Equals($signedInAs, $signInAs, [StringComparison]::OrdinalIgnoreCase)) {", StringComparison.Ordinal);
        var refusal = script.IndexOf("', the account confirmed when this script was copied. Nothing was read.')", StringComparison.Ordinal);
        var values = script.IndexOf("$arguments = @{", StringComparison.Ordinal);
        var body = script.IndexOf("$library = {", StringComparison.Ordinal);
        var disconnect = script.IndexOf("finally {\n    Disconnect-ExchangeOnline -Confirm:$false", StringComparison.Ordinal);
        Assert.True(connect >= 0 && guard > connect && tenant > guard && account > tenant && refusal > account && values > refusal && body > values && disconnect > body,
            "The account check must follow sign-in and the tenant check, inside the block that always disconnects, and precede the values and the body.");
        Assert.Contains("$signedInAs = ([string]$connections[0].UserPrincipalName).Trim()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_reviewed_account_the_copied_script_uses_the_account_chosen_at_the_prompt()
    {
        var entry = Item("exo.mailbox-inventory");
        var script = ScriptCopy.Generate(entry, Bind("exo.mailbox-inventory"), new ScriptCopyTarget("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "Contoso"), Now);
        Assert.Contains("$signInAs = ''\n", script, StringComparison.Ordinal);
        Assert.Contains("# Account:     chosen at the Microsoft sign-in prompt", script, StringComparison.Ordinal);
        // The check is present but guarded, so an empty reviewed account never refuses.
        Assert.Contains("if ($signInAs -and -not [string]::Equals(", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("o'brien", "'o''brien'")]
    [InlineData("‘x’‚‛", "'‘‘x’’‚‚‛‛'")]
    [InlineData("$(calc) `\"", "'$(calc) `\"'")]
    public void Quoting_doubles_every_PowerShell_single_quote_character(string value, string expected) =>
        Assert.Equal(expected, ScriptCopy.Quote(value));
}
