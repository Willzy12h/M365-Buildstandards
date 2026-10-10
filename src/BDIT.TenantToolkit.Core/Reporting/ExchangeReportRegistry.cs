using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Reporting;

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
    public const string ExchangeOnlineAdapter = "exchangeOnline";
    public const string PurviewAdapter = "purview";
    public const string ExchangeOnlineResource = "ExchangeOnline";
    public const string PurviewResource = "Purview";

    public sealed record Section(string Id, Type RowType);
    public sealed record Parameter(string Name, string Type);
    public sealed record Definition(string Id, string Name, string Adapter, IReadOnlyList<string> SourceCommands,
        IReadOnlyList<Parameter> Parameters, IReadOnlyList<Section> Sections, string Reference, string Limitations)
    {
        /// <summary>The evidence resource for this adapter. Only the two exact pairs exist; anything else is refused.</summary>
        public string Resource => ResourceFor(Adapter);
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
            + "output are unverified until accepted in a test tenant.")
    });

    public static Definition Find(string id) => Definitions.SingleOrDefault(d => d.Id == id)
        ?? throw new ConfigurationException("Unknown registered Exchange report: " + id + ".");

    /// <summary>Maps an adapter to its evidence resource: exchangeOnline to ExchangeOnline and purview to Purview, ordinally.</summary>
    public static string ResourceFor(string adapter) => adapter switch
    {
        ExchangeOnlineAdapter => ExchangeOnlineResource,
        PurviewAdapter => PurviewResource,
        _ => throw new ConfigurationException("Unknown Exchange report adapter.")
    };
}
