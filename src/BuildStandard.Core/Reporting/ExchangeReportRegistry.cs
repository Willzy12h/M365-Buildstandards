using BuildStandard.Core.Models;

namespace BuildStandard.Core.Reporting;

/// <summary>
/// Package-owned Exchange Online and Purview report registrations (INT-088). A registration fixes the adapter, the
/// source commands, the typed parameters, the sections and their row schemas. Nothing in a stored or imported record can
/// add to them: a record that names anything else is refused.
/// </summary>
public static class ExchangeReportRegistry
{
    /// <summary>The contract's per-section row cap for executable reports, lower than schema 1 manifests allowed.</summary>
    public const int MaximumRows = 10000;
    public const string AdapterVersion = "registered-exchange-report/1";
    public const string FamilyAdapterVersion = "registered-exchange-report/2";
    public const string ExchangeOnlineAdapter = "exchangeOnline";
    public const string PurviewAdapter = "purview";
    public const string ExchangeOnlineResource = "ExchangeOnline";
    public const string PurviewResource = "Purview";

    public sealed record Section(string Id, Type RowType);
    public sealed record Parameter(string Name, string Type);
    public sealed record Definition(string Id, string Name, string Adapter, IReadOnlyList<string> SourceCommands,
        IReadOnlyList<Parameter> Parameters, IReadOnlyList<Section> Sections, string Reference, string Limitations,
        int ReportSchemaVersion = 1, string AdapterVersion = ExchangeReportRegistry.AdapterVersion)
    {
        /// <summary>The evidence resource for this adapter. Only the two exact pairs exist; anything else is refused.</summary>
        public string Resource => ResourceFor(Adapter);
        public int RowLimit => ReportSchemaVersion == 2 ? 5000 : MaximumRows;
    }

    public static IReadOnlyList<Definition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new Definition("exo-mailbox-inventory", "Mailbox inventory, size and quotas", ExchangeOnlineAdapter,
            Array.AsReadOnly(new[] { "Get-EXOMailbox", "Get-EXOMailboxStatistics" }),
            Array.AsReadOnly(new[] { new Parameter("IncludeArchive", ExchangeReportParameterType.Boolean) }),
            Array.AsReadOnly(new[] { new Section("mailboxes", typeof(MailboxReportRow)) }),
            "https://learn.microsoft.com/en-us/powershell/module/exchange/get-exomailboxstatistics",
            "Mailbox identity is the exact Exchange mailbox GUID; the reported Entra object ID is kept separately and may be absent. "
            + "Shared, room and equipment mailboxes are listed as mailboxes even without a joinable user or licence. Sizes and quotas are "
            + "Exchange's own text, read separately: a read that failed is unknown, never zero, and Unlimited stays Unlimited. Primary "
            + "mailbox, archive and Recoverable Items are separate; no OST or PST claim is made. Live behaviour, required roles and module "
            + "output are unverified until accepted in a test tenant."),
        new Definition("exo-mailbox-protocols", "Mailbox protocol settings", ExchangeOnlineAdapter,
            Array.AsReadOnly(new[] { "Get-EXOMailbox", "Get-CASMailbox", "Get-TransportConfig" }),
            Array.Empty<Parameter>(), Array.AsReadOnly(new[] { new Section("protocols", typeof(MailboxProtocolReportRow)) }),
            "https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-casmailbox",
            "Observed protocol settings and SMTP AUTH configuration only, not proof that authentication succeeds. "
            + "A returned null SMTP override inherits the organisation setting; an absent value is unknown. "
            + "Each CAS read must repeat the exact Entra object ID from the ExchangeGuid inventory. Live behaviour and roles remain unverified."),
        new Definition("exo-mailbox-forwarding", "Mailbox forwarding settings", ExchangeOnlineAdapter,
            Array.AsReadOnly(new[] { "Get-EXOMailbox" }), Array.Empty<Parameter>(),
            Array.AsReadOnly(new[] { new Section("forwarding", typeof(MailboxForwardingReportRow)) }),
            "https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-exomailbox",
            "Observed forwarding identifiers and SMTP targets are separate raw values; no ownership or delivery inference. "
            + "A collected null target means explicitly unset, not an unread value. No forwarding changes. Live behaviour and roles remain unverified."),
        new Definition("exo-mailbox-holds", "Mailbox hold settings", ExchangeOnlineAdapter,
            Array.AsReadOnly(new[] { "Get-EXOMailbox" }), Array.Empty<Parameter>(),
            Array.AsReadOnly(new[] { new Section("holds", typeof(MailboxHoldReportRow)) }),
            "https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-exomailbox",
            "Observed mailbox hold flags and up to 100 exact InPlaceHolds identifiers; these do not prove legal/compliance coverage. "
            + "Litigation Hold, retention hold and policy identifiers retain their distinct meanings. No hold changes. Live behaviour and roles remain unverified."),
        new Definition("exo-inbox-rules", "Selected mailbox inbox-rule observations", ExchangeOnlineAdapter,
            Array.AsReadOnly(new[] { "Get-EXOMailbox", "Get-InboxRule" }),
            Array.AsReadOnly(new[] {new Parameter("MailboxGuids", ExchangeReportParameterType.Text), new Parameter("IncludeHidden", ExchangeReportParameterType.Boolean)}),
            Array.AsReadOnly(new[] {new Section("target-reads", typeof(ExchangeTargetReadRow)), new Section("rules", typeof(InboxRuleObservationRow))}),
            "https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-inboxrule",
            "One to50 explicitly selected Exchange mailbox GUIDs, not an implicit estate scan. Opaque RuleIdentity and raw recipient references remain distinct from names/Entra identities. Hidden rules are omitted unless explicitly selected. Unknown values and unsuccessful target reads are not successful empty results; no incident verdict or change authority. The typed evidence registration alone does not provide a runnable script. Live/module/RBAC behaviour is unverified.",
            ReportSchemaVersion: 2, AdapterVersion: FamilyAdapterVersion)
    });

    public static Definition Find(string id) => Definitions.SingleOrDefault(d => d.Id == id)
        ?? throw new ConfigurationException("Unknown registered Exchange report: " + id + ".");

    /// <summary>Exact registered identity/version dispatch; never infer a schema from the supplied row columns.</summary>
    public static Definition Find(string id, int reportSchemaVersion, string adapterVersion)
    {
        var definition = Find(id);
        if (definition.ReportSchemaVersion != reportSchemaVersion
            || !string.Equals(definition.AdapterVersion, adapterVersion, StringComparison.Ordinal))
            throw new ConfigurationException("The report schema/adapter version does not match its exact registration.");
        return definition;
    }

    /// <summary>Maps an adapter to its evidence resource: exchangeOnline to ExchangeOnline and purview to Purview, ordinally.</summary>
    public static string ResourceFor(string adapter) => adapter switch
    {
        ExchangeOnlineAdapter => ExchangeOnlineResource,
        PurviewAdapter => PurviewResource,
        _ => throw new ConfigurationException("Unknown Exchange report adapter.")
    };
}
