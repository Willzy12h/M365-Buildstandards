using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace BuildStandard.Engine.Identity;

public enum CustomerImportState { Ready, Existing, Invalid, Conflict }

/// <summary>Untrusted customer-list text. Relationship and tags never establish access or authority.</summary>
public sealed record PartnerCustomerRow(string TenantId, string Company, string Domain, string Relationship, string Tags,
    CustomerImportState State, string Detail, int Occurrences);

public sealed class PartnerCustomerPreview
{
    public IReadOnlyList<PartnerCustomerRow> Rows { get; }
    public string ProfileFingerprint { get; }
    public int DataRows { get; }

    internal PartnerCustomerPreview(IEnumerable<PartnerCustomerRow> rows, string fingerprint, int dataRows)
    {
        Rows = Array.AsReadOnly(rows.ToArray());
        ProfileFingerprint = fingerprint;
        DataRows = dataRows;
    }
}

/// <summary>
/// Offline parsing and preparation only. The caller must conditionally commit under the shared profile-write lease;
/// checking this fingerprint and later calling an unconditional save does not provide concurrency protection.
/// </summary>
public static class PartnerCustomerImport
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public const int MaximumRows = 5000;
    private static readonly JsonSerializerOptions CopyOptions = new(ToolkitJson.Options)
    { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static PartnerCustomerPreview Preview(byte[] csv, IReadOnlyList<TenantProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(csv);
        if (csv.Length > MaximumBytes) throw new ConfigurationException("The customer CSV exceeds 5 MiB. Split the export before importing; no rows were imported.");
        var fingerprint = Fingerprint(profiles);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(csv); }
        catch (DecoderFallbackException ex) { throw new ConfigurationException("Save the customer list as UTF-8 CSV. This encoding could not be read.", ex); }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        ValidateQuotePlacement(text);
        using var parser = new TextFieldParser(new StringReader(text))
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        try
        {
            var headers = parser.ReadFields();
            if (headers is null || headers.Any(string.IsNullOrWhiteSpace) ||
                headers.Select(h => h.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
                throw new ConfigurationException("The customer CSV needs unique, non-empty column headings.");
            var columns = headers.Select((name, index) => (name: name.Trim(), index))
                .ToDictionary(p => p.name, p => p.index, StringComparer.OrdinalIgnoreCase);
            if (!columns.ContainsKey("Microsoft ID") || !columns.ContainsKey("Company name"))
                throw new ConfigurationException("The customer CSV must include Microsoft ID and Company name columns. Use the Partner Center customer-list export.");
            var existing = profiles.Select(p => p.TenantId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = new List<PartnerCustomerRow>();
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields is null) break;
                if (rows.Count == MaximumRows) throw new ConfigurationException("The customer CSV exceeds 5,000 data rows. Split it before importing; no rows were imported.");
                if (fields.Length != headers.Length) throw new ConfigurationException("A customer CSV row does not match its headings. Check commas and quoted fields; no rows were imported.");
                string Field(string name) => columns.TryGetValue(name, out var index) ? fields[index] : "";
                var rawId = Field("Microsoft ID");
                var rawCompany = Field("Company name");
                var rawDomain = Field("Primary domain name");
                var tenant = rawId.Trim().ToLowerInvariant();
                var company = rawCompany.Trim();
                var domain = rawDomain.Trim();
                var problem = RowProblem(rawId, rawCompany, rawDomain);
                var state = problem.Length != 0 ? CustomerImportState.Invalid : existing.Contains(tenant) ? CustomerImportState.Existing : CustomerImportState.Ready;
                var detail = problem.Length != 0 ? problem : state == CustomerImportState.Existing
                    ? "Already saved. Existing client details will not be overwritten."
                    : "New local client. Domain and access are unverified; no connection will be made.";
                rows.Add(new(tenant, company, domain, Field("Relationship"), Field("Tags"), state, detail, 1));
            }
            return new(Collapse(rows), fingerprint, rows.Count);
        }
        catch (MalformedLineException ex)
        {
            // The framework exception can contain customer text; do not repeat it in the engineer-facing message.
            throw new ConfigurationException($"Customer CSV quoting is invalid near line {ex.LineNumber}. Check the source file; no rows were imported.");
        }
    }

    public static IReadOnlyList<TenantProfile> PrepareAdditions(PartnerCustomerPreview preview, IEnumerable<string> selectedTenantIds,
        IReadOnlyList<TenantProfile> currentProfiles, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(selectedTenantIds);
        if (!string.Equals(preview.ProfileFingerprint, Fingerprint(currentProfiles), StringComparison.Ordinal))
            throw new ConfigurationException("Saved clients changed after this preview. Open a new import preview before saving.");
        var selected = selectedTenantIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = preview.Rows.Where(r => selected.Contains(r.TenantId)).ToList();
        if (rows.Count != selected.Count || rows.Any(r => r.State != CustomerImportState.Ready))
            throw new ConfigurationException("Select only valid new clients from this preview. Existing, invalid or conflicting clients cannot be imported.");
        // Return independent copies, never mutate live UI profiles or retain their mutable nested parameters.
        var result = currentProfiles.Select(p => ToolkitJson.Deserialize<TenantProfile>(JsonSerializer.Serialize(p, CopyOptions))).ToList();
        foreach (var row in rows)
            result.Add(ProfileValidator.Validate(new TenantProfile { TenantId = row.TenantId, Company = row.Company, Domain = row.Domain }, now));
        return result.AsReadOnly();
    }

    private static string Fingerprint(IReadOnlyList<TenantProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Any(p => p is null || !StrictTenantId(p.TenantId)) ||
            profiles.Select(p => p.TenantId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profiles.Count)
            throw new ConfigurationException("Saved clients contain malformed or duplicate tenant identities. Resolve them before importing customers.");
        return CanonicalJson.Sha256(ToolkitJson.ToNode(profiles));
    }

    private static bool StrictTenantId(string? value) =>
        value is { Length: 36 } && Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty;

    // TextFieldParser deliberately tolerates quotes inside bare fields and whitespace before quoted fields.
    // Refuse those forms before it can discard identity controls. It still owns CSV decoding/escaped quotes.
    private static void ValidateQuotePlacement(string text)
    {
        var quoted = false;
        var closed = false;
        var fieldStart = true;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') i++;
                    else { quoted = false; closed = true; }
                }
                continue;
            }
            if (c is ',' or '\r' or '\n') { fieldStart = true; closed = false; continue; }
            if (closed || c == '"' && !fieldStart)
                throw new ConfigurationException("Customer CSV quoting is invalid. Quotes must enclose a whole field, with no text or controls outside them.");
            if (c == '"') quoted = true;
            fieldStart = false;
        }
        if (quoted) throw new ConfigurationException("Customer CSV has an unfinished quoted field. No rows were imported.");
    }

    private static string RowProblem(string tenant, string company, string domain)
    {
        if (tenant.Any(char.IsControl) || company.Any(char.IsControl) || domain.Any(char.IsControl))
            return "Tenant ID, company and domain must not contain control characters.";
        if (!StrictTenantId(tenant.Trim()))
            return "Microsoft ID must be a non-empty tenant GUID. It is never inferred from a domain.";
        if (company.Trim().Length is < 1 or > 120) return "Company name must contain between 1 and 120 characters.";
        if (domain.Trim().Length > 200) return "Primary domain name must contain no more than 200 characters.";
        return "";
    }

    private static IEnumerable<PartnerCustomerRow> Collapse(IReadOnlyList<PartnerCustomerRow> rows)
    {
        // Keep malformed identities as individual diagnostic rows; only actual tenant GUIDs establish identity.
        var groups = rows.Select((row, index) => (row, key: ProfileValidator.IsGuid(row.TenantId) ? row.TenantId : "invalid-row-" + index))
            .GroupBy(p => p.key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var first = group.First().row;
            var count = group.Count();
            if (group.Any(p => p.row != first))
                yield return first with { State = CustomerImportState.Conflict, Occurrences = count,
                    Detail = "Repeated tenant ID has conflicting customer fields. Resolve these source rows and preview again." };
            else yield return first with { Occurrences = count };
        }
    }
}
