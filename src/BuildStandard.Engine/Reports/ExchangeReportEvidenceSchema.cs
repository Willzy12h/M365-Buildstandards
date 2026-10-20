using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Exchange;

namespace BuildStandard.Engine.Reports;

/// <summary>
/// Strict, bounded reader for INT-088 <c>exchangeReportEvidence</c> schema 1. It shares the read states and the integrity
/// rule with Graph report evidence but is a separate kind: neither reader accepts the other's records, and neither is a
/// configuration capture. Registered row schemas mean raw module output cannot be imported as a report.
/// </summary>
public static partial class ExchangeReportEvidenceSchema
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumTextLength = 4096;
    private const int MaximumValueLength = 256;
    private static readonly JsonSerializerOptions Strict = MakeOptions();
    private static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions(ToolkitJson.Options)
        {
            PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never, MaxDepth = 48
        };
        options.Converters.Clear(); options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        // Frozen, with its resolver, so the registered row projection can be read from the same contract that writes rows.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static JsonObject Row<T>(T row) where T : ExchangeReportRow =>
        JsonSerializer.SerializeToNode(row, Strict)!.AsObject();
    public static string Serialize(ExchangeReportEvidence evidence) => JsonSerializer.Serialize(evidence, Strict);

    /// <summary>The registered row projection: a row type's JSON property names in the order they are written.</summary>
    public static IReadOnlyList<string> Columns(Type rowType)
    {
        if (!typeof(ExchangeReportRow).IsAssignableFrom(rowType)) throw Bad("Not a registered Exchange report row type.");
        return Strict.GetTypeInfo(rowType).Properties.Select(p => p.Name).ToList();
    }

    public static ExchangeReportEvidence Read(string json, string expectedTenantId)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw Bad("Exchange report evidence exceeds the 32 MiB reader limit.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.Ordinal);
            var evidence = JsonSerializer.Deserialize<ExchangeReportEvidence>(json, Strict) ?? throw Bad("Exchange report evidence is empty.");
            Validate(evidence, expectedTenantId);
            return evidence;
        }
        catch (JsonException ex) { throw Bad("Invalid Exchange report evidence JSON: " + ex.Message); }
    }

    public static void Seal(ExchangeReportEvidence evidence)
    {
        if (evidence.ReportSchemaVersion == 2) RequireEncodedSize(evidence, MaximumBytes - 64);
        evidence.IntegrityDigest = EvidenceIntegrity.Compute(evidence);
        Validate(evidence, evidence.TenantId);
    }

    public static void Validate(ExchangeReportEvidence e, string expectedTenantId)
    {
        if (e.SchemaVersion != 1 || e.Kind != "exchangeReportEvidence")
            throw Bad("Unsupported Exchange report evidence kind or schema.");
        CanonicalGuid(e.Id); CanonicalGuid(e.RunId); CanonicalGuid(e.TenantId);
        if (!ReportValues.TryGuid(expectedTenantId, out var expected) || !string.Equals(e.TenantId, expected, StringComparison.Ordinal))
            throw new TenantMismatchException("Exchange report evidence belongs to another tenant.");
        var definition = ExchangeReportRegistry.Find(e.ReportId, e.ReportSchemaVersion, e.AdapterVersion);
        if (!string.Equals(e.Resource, definition.Resource, StringComparison.Ordinal))
            throw Bad("The evidence resource does not match the registered adapter.");
        switch (e.SourceMode)
        {
            case "live":
                CanonicalGuid(e.InitiatingAccountObjectId);
                if (!IsUpn(e.ObservedAccountUpn)) throw Bad("A live run must record the account the module observed.");
                break;
            case "historical":
                // Imported output keeps its original claims; it cannot become an authenticated live run.
                if (e.InitiatingAccountObjectId is not null) CanonicalGuid(e.InitiatingAccountObjectId);
                if (e.ObservedAccountUpn is not null && !IsUpn(e.ObservedAccountUpn)) throw Bad("Invalid observed account.");
                break;
            default: throw Bad("Unknown Exchange report source mode.");
        }
        if (!Utc(e.StartedAt, out var started) || !Utc(e.EndedAt, out var ended) || ended < started)
            throw Bad("The report collection interval is invalid.");
        foreach (var version in new[] { e.ToolkitVersion, e.ModuleVersion, e.RuntimeVersion })
            if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl)) throw Bad("Missing report tool, module or runtime provenance.");
        foreach (var digest in new[] { e.ScriptSha256, e.ManifestSha256, e.RunnerTemplateSha256 })
            if (!IsSha256(digest)) throw Bad("Script, manifest and runner digests must be exact lower-case SHA-256 values.");
        ValidateParameters(definition, e.Parameters);
        if (e.SourceCommands is null || !e.SourceCommands.SequenceEqual(definition.SourceCommands, StringComparer.Ordinal))
            throw Bad("Report source commands do not match the registration.");
        Strings(e.Limitations);
        ValidateRowsAndSections(definition, e);
        State(e.Status);
        if (e.Status != ReportEvidenceSchema.Overall(e.Sections.Select(s => s.Status))) throw Bad("The report cannot claim a better state than its sections.");
        if (definition.ReportSchemaVersion == 2) RequireEncodedSize(e, MaximumBytes);
        if (!IsSha256(e.IntegrityDigest) || !EvidenceIntegrity.Verify(e, e.IntegrityDigest))
            throw Bad("Exchange report evidence failed its integrity check. Digests detect change; they do not authenticate source claims.");
    }

    // Stored/imported readers never normalise IDs. Only exact actor-bound report2 child admission uses this option.
    private static void ValidateRowsAndSections(ExchangeReportRegistry.Definition definition, ExchangeReportEvidence e,
        bool normaliseChildKeys = false)
    {
        if (e.Sections is null || e.Sections.Count != definition.Sections.Count || e.Sections.Any(s => s is null)
            || !e.Sections.Select(s => s.Id).SequenceEqual(definition.Sections.Select(s => s.Id), StringComparer.Ordinal))
            throw Bad("Missing, duplicated or unrelated report sections.");
        var includeArchive = Parameter(e, "IncludeArchive") == "true";
        foreach (var section in e.Sections)
        {
            State(section.Status); Strings(section.Limitations);
            if (section.Rows is null || section.Rows.Count > definition.RowLimit || section.Rows.Any(r => r is null))
                throw Bad("Invalid or excessive report rows.");
            if (section.Status != ReportReadState.Collected && string.IsNullOrWhiteSpace(section.Error))
                throw Bad("Unsuccessful or partial report sections require an explicit reason.");
            if (section.Status == ReportReadState.Collected && section.Error is not null)
                throw Bad("A successful report section cannot conceal a read error.");
            if (section.Error is not null) Text(section.Error);
            if ((section.Status is ReportReadState.Failed or ReportReadState.NotAttempted) && section.Rows.Count != 0
                && !(definition.ReportSchemaVersion == 2 && section.Id == "target-reads"))
                throw Bad("Failed or unattempted sections cannot claim collected rows.");
            var schema = definition.Sections.Single(s => s.Id == section.Id);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in section.Rows)
            {
                if (normaliseChildKeys && (!node.ContainsKey("id") || node["id"] is not null))
                    throw Bad("A report2 child must supply explicit-null local ID; only the parent computes observation keys.");
                ExchangeReportRow row;
                try { row = (ExchangeReportRow)(JsonSerializer.Deserialize(node.ToJsonString(), schema.RowType, Strict) ?? throw Bad("Empty report row.")); }
                catch (JsonException ex) { throw Bad("Invalid registered report row: " + ex.Message); }
                if (normaliseChildKeys)
                {
                    row.Id = row switch
                    {
                        ExchangeGroupTargetRow target => ExchangeObservationKey.TargetRead(definition.Id, target.RequestedGroupGuid),
                        ExchangeGroupMemberRow member => GroupKey(member),
                        ExchangeCalendarTargetRow target => ExchangeObservationKey.TargetRead(definition.Id, target.RequestedExchangeGuid),
                        CalendarPermissionObservationRow permission => CalendarKey(permission),
                        ExchangePermissionTargetRow target => ExchangeObservationKey.TargetRead(definition.Id, target.RequestedExchangeGuid),
                        MailboxPermissionObservationRow permission => PermissionKey(permission),
                        ExchangeTargetReadRow target => ExchangeObservationKey.TargetRead(definition.Id, target.RequestedExchangeGuid),
                        InboxRuleObservationRow rule => ExchangeObservationKey.InboxRule(rule.MailboxExchangeGuid, rule.RuleIdentity),
                        _ => throw Bad("No parent observation-key encoder is registered for this family row.")
                    };
                    node["id"] = row.Id;
                }
                State(row.ReadStatus);
                if (row.Id is not null && (!(definition.ReportSchemaVersion == 1 ? ReportValues.IsCanonicalGuid(row.Id)
                    : row.Id.StartsWith("sha256:",StringComparison.Ordinal) && IsSha256(row.Id[7..])) || !identities.Add(row.Id)))
                    throw Bad("Duplicate, malformed or empty report identity.");
                if (row.Name is not null)
                {
                    if (definition.ReportSchemaVersion == 1) Value(row.Name);
                    // A friendly label is not source identity. A reported blank name can accompany an exact rule ID.
                    else if (row.Name.Length > 1024 || row.Name.Any(char.IsControl))
                        throw Bad("A reported family name exceeds1,024 characters or contains control characters.");
                }
                if (row.Error is not null) Text(row.Error);
                if (row.ReadStatus == ReportReadState.Collected && (row.Id is null || row.Error is not null))
                    throw Bad("A successful row needs exact identity and no hidden error.");
                if (row.ReadStatus != ReportReadState.Collected && string.IsNullOrWhiteSpace(row.Error))
                    throw Bad("An unsuccessful or partial row needs a reason.");
                if (section.Status == ReportReadState.Collected && row.ReadStatus != ReportReadState.Collected)
                    throw Bad("Incomplete rows cannot become a successful section.");
                if (definition.ReportSchemaVersion == 2) ValidateFamilyRow(row, definition.Id);
                else if (row is MailboxReportRow mailbox) ValidateMailbox(mailbox, includeArchive);
                else ValidateSettings(row);
            }
        }
        if(definition.ReportSchemaVersion == 2) ValidateFamilyReport(e);
    }

    /// <summary>
    /// Each value group carries its own read state. A value is present only when its group was read; a group that was
    /// not read has no value, so a failed read can never look like zero or like a known quota.
    /// </summary>
    private static void ValidateMailbox(MailboxReportRow m, bool includeArchive)
    {
        foreach (var state in new[] { m.PrimarySizeReadStatus, m.QuotaReadStatus, m.ArchiveReadStatus, m.ArchiveSizeReadStatus }) State(state);
        if (m.ExternalDirectoryObjectId is not null) CanonicalGuid(m.ExternalDirectoryObjectId);
        foreach (var text in new[] { m.PrimarySmtpAddress, m.MailboxType }) if (text is not null) Readable(text);
        Group(m.PrimarySizeReadStatus, m.PrimarySizeRaw);
        Group(m.QuotaReadStatus, m.IssueWarningQuotaRaw, m.ProhibitSendQuotaRaw, m.ProhibitSendReceiveQuotaRaw);
        // Archive absence is only ever a successful explicit archive-state read.
        if (m.ArchiveReadStatus == ReportReadState.Collected ? m.HasArchive is null : m.HasArchive is not null)
            throw Bad("Archive existence is known only from a successful archive-state read.");
        if (m.ArchiveReadStatus != ReportReadState.Collected && m.ArchiveQuotaRaw is not null)
            throw Bad("An archive quota needs a successful archive-state read.");
        if (m.ArchiveQuotaRaw is not null) Readable(m.ArchiveQuotaRaw);
        if (m.HasArchive != true && (m.ArchiveSizeReadStatus != ReportReadState.NotAttempted || m.ArchiveSizeRaw is not null || m.ArchiveQuotaRaw is not null))
            throw Bad("Archive size and quota apply only to a mailbox whose archive was read as present.");
        Group(m.ArchiveSizeReadStatus, m.ArchiveSizeRaw);
        if (!includeArchive && (m.ArchiveReadStatus != ReportReadState.NotAttempted || m.ArchiveSizeReadStatus != ReportReadState.NotAttempted))
            throw Bad("Archive values were recorded although the run did not include archives.");
        if (m.ReadStatus == ReportReadState.Collected)
        {
            if (string.IsNullOrWhiteSpace(m.MailboxType) || m.PrimarySizeReadStatus != ReportReadState.Collected || m.QuotaReadStatus != ReportReadState.Collected)
                throw Bad("A successful mailbox row needs its type, size and quotas.");
            if (includeArchive && (m.ArchiveReadStatus != ReportReadState.Collected
                || m.HasArchive == true && m.ArchiveSizeReadStatus != ReportReadState.Collected))
                throw Bad("A successful mailbox row in an archive run needs its archive state and, when present, its archive size.");
        }
    }

    private static void ValidateSettings(ExchangeReportRow row)
    {
        CanonicalGuid(row.Id); // New setting adapters exclude unidentified rows, even when a section is partial.
        switch (row)
        {
            case MailboxProtocolReportRow p:
                if (p.PrimarySmtpAddress is not null) Readable(p.PrimarySmtpAddress);
                Boolean(p.PopEnabledReadStatus, p.PopEnabled); Boolean(p.ImapEnabledReadStatus, p.ImapEnabled);
                Boolean(p.MapiEnabledReadStatus, p.MapiEnabled); Boolean(p.EwsEnabledReadStatus, p.EwsEnabled);
                Boolean(p.ActiveSyncEnabledReadStatus, p.ActiveSyncEnabled); Boolean(p.OwaEnabledReadStatus, p.OwaEnabled);
                Boolean(p.OrganisationSmtpAuthDisabledReadStatus, p.OrganisationSmtpAuthDisabled);
                Boolean(p.EffectiveSmtpAuthDisabledReadStatus, p.EffectiveSmtpAuthDisabled);
                State(p.SmtpAuthOverrideReadStatus);
                if (p.SmtpAuthOverride is not ("Enabled" or "Disabled" or "Inherit" or "Unknown")
                    || (p.SmtpAuthOverrideReadStatus == ReportReadState.Collected) == (p.SmtpAuthOverride == "Unknown"))
                    throw Bad("SMTP AUTH override must distinguish observed inheritance from an unknown read.");
                bool? expected = p.SmtpAuthOverride switch
                {
                    "Enabled" => false, "Disabled" => true, "Inherit" => p.OrganisationSmtpAuthDisabled, _ => null
                };
                if (p.EffectiveSmtpAuthDisabled != expected)
                    throw Bad("Effective SMTP AUTH configuration does not match its observed inputs.");
                Complete(row, p.PopEnabledReadStatus, p.ImapEnabledReadStatus, p.MapiEnabledReadStatus, p.EwsEnabledReadStatus,
                    p.ActiveSyncEnabledReadStatus, p.OwaEnabledReadStatus, p.SmtpAuthOverrideReadStatus,
                    p.OrganisationSmtpAuthDisabledReadStatus, p.EffectiveSmtpAuthDisabledReadStatus);
                break;
            case MailboxForwardingReportRow f:
                if (f.PrimarySmtpAddress is not null) Readable(f.PrimarySmtpAddress);
                OptionalTarget(f.ForwardingAddressReadStatus, f.ForwardingAddress);
                OptionalTarget(f.ForwardingSmtpAddressReadStatus, f.ForwardingSmtpAddress);
                Boolean(f.DeliverToMailboxAndForwardReadStatus, f.DeliverToMailboxAndForward);
                Complete(row, f.ForwardingAddressReadStatus, f.ForwardingSmtpAddressReadStatus, f.DeliverToMailboxAndForwardReadStatus);
                break;
            case MailboxHoldReportRow h:
                if (h.PrimarySmtpAddress is not null) Readable(h.PrimarySmtpAddress);
                Boolean(h.LitigationHoldEnabledReadStatus, h.LitigationHoldEnabled);
                Boolean(h.RetentionHoldEnabledReadStatus, h.RetentionHoldEnabled);
                Boolean(h.ComplianceTagHoldAppliedReadStatus, h.ComplianceTagHoldApplied);
                Boolean(h.DelayHoldAppliedReadStatus, h.DelayHoldApplied);
                State(h.InPlaceHoldIdsReadStatus);
                if ((h.InPlaceHoldIdsReadStatus == ReportReadState.Collected) != (h.InPlaceHoldIds is not null))
                    throw Bad("Hold identifiers require an explicit collection read; unknown is not an empty list.");
                if (h.InPlaceHoldIds is { } ids)
                {
                    if (ids.Count > 100) throw Bad("Hold identifier list exceeds its registered limit.");
                    foreach (var id in ids) { if (id is null) throw Bad("Invalid hold identifier."); Readable(id); }
                }
                Complete(row, h.LitigationHoldEnabledReadStatus, h.RetentionHoldEnabledReadStatus, h.ComplianceTagHoldAppliedReadStatus,
                    h.DelayHoldAppliedReadStatus, h.InPlaceHoldIdsReadStatus);
                break;
            default: throw Bad("No validator for the registered setting row.");
        }
    }

    private static void Boolean(string state, bool? value)
    {
        State(state);
        if ((state == ReportReadState.Collected) != value.HasValue)
            throw Bad("A boolean is known only when its exact value was collected; unknown is never false.");
    }
    private static void OptionalTarget(string state, string? value)
    {
        State(state);
        if (state != ReportReadState.Collected && value is not null) throw Bad("An unread forwarding target cannot contain a value.");
        if (value is not null) Readable(value);
    }
    private static void Complete(ExchangeReportRow row, params string[] states)
    {
        if (row.ReadStatus == ReportReadState.Collected && states.Any(s => s != ReportReadState.Collected))
            throw Bad("A complete settings row cannot contain an unsuccessful read.");
    }

    private static void Group(string state, params string?[] values)
    {
        if (state == ReportReadState.Collected)
        {
            if (values.Any(v => v is null)) throw Bad("A value group read successfully must record every value.");
        }
        else if (values.Any(v => v is not null)) throw Bad("A value group that was not read successfully cannot record values.");
        foreach (var value in values) if (value is not null) Readable(value);
    }

    /// <summary>
    /// A recorded mailbox value must be readable. Empty or whitespace-only text is not an observed zero, a known quota or a
    /// known type: a value that could not be read is recorded as absent with its group's read state and reason, never blank.
    /// Readable raw values, including zero and Unlimited, are kept exactly as returned.
    /// </summary>
    private static void Readable(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Bad("A report value is empty or blank; an unreadable value is recorded as not read, never as blank text.");
        Value(value);
    }

    public static void ValidateParameters(ExchangeReportRegistry.Definition definition, List<ExchangeReportParameter>? parameters)
    {
        if (parameters is null || parameters.Count != definition.Parameters.Count || parameters.Any(p => p is null))
            throw Bad("Report parameters do not match the registration.");
        for (var i = 0; i < parameters.Count; i++)
        {
            var (actual, registered) = (parameters[i], definition.Parameters[i]);
            if (!string.Equals(actual.Name, registered.Name, StringComparison.Ordinal) || !string.Equals(actual.Type, registered.Type, StringComparison.Ordinal))
                throw Bad("Report parameters do not match the registration.");
            if (!IsNormalised(actual.Type, actual.Value)) throw Bad("Report parameter " + registered.Name + " is not a normalised " + registered.Type + " value.");
        }
    }

    /// <summary>The single normalised text form of each parameter type, so equal inputs always record identical values.</summary>
    public static bool IsNormalised(string type, string? value) => value is not null && type switch
    {
        ExchangeReportParameterType.Boolean => value is "true" or "false",
        ExchangeReportParameterType.Integer => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            && string.Equals(number.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal),
        ExchangeReportParameterType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        ExchangeReportParameterType.Text => value.Length <= MaximumTextLength && !value.Any(char.IsControl),
        _ => false
    };

    private static string? Parameter(ExchangeReportEvidence e, string name) => e.Parameters.SingleOrDefault(p => p.Name == name)?.Value;
    private static bool Utc(string? value, out DateTimeOffset at) => Timestamps.TryParse(value, out at) && value!.EndsWith('Z');
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool IsUpn(string? value) => value is { Length: > 2 and <= MaximumValueLength } && value.IndexOf('@') > 0
        && value.IndexOf('@') < value.Length - 1 && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
    private static void CanonicalGuid(string? value) { if (!ReportValues.IsCanonicalGuid(value)) throw Bad("Invalid report identity."); }
    private static void State(string state) { if (!ReportReadState.All.Contains(state, StringComparer.Ordinal)) throw Bad("Unknown report read state."); }
    private static void Value(string value) { if (value.Length > MaximumValueLength || value.Any(char.IsControl)) throw Bad("A report value is too long or contains control characters."); }
    private static void Text(string value) { if (value.Length > MaximumTextLength || value.Any(c => char.IsControl(c) && c is not '\n')) throw Bad("A report reason is too long or contains control characters."); }
    private static void Strings(List<string>? values) { if (values is null || values.Count > 64 || values.Any(s => s is null || s.Length > MaximumTextLength)) throw Bad("Invalid report limitations."); }
    private static ConfigurationException Bad(string message) => new(message);
}
