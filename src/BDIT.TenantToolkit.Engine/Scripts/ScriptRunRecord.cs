using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.Engine.Scripts;

/// <summary>One form value as the run used it: the parameter name and the literal the wrapper carried.</summary>
public sealed class ScriptRunInput
{
    [JsonRequired] public string Name { get; set; } = "";
    [JsonRequired] public string Value { get; set; } = "";
}

/// <summary>
/// The kept result of one Run (INT-081, proposed): a separate, Claude-owned run-history record, not INT-071 report
/// evidence. INT-071's wrapper holds registered Graph reports with typed rows and an account object ID; a library run
/// has an Exchange sign-in name, declared CSV columns and a pinned script instead, so it is stored on its own under
/// <c>tenants/&lt;tenant&gt;/script-runs/</c> with its own strict reader. It is never configuration evidence, never
/// before-evidence for a deployment and never satisfies an assessment control.
/// </summary>
public sealed class ScriptRunRecord
{
    public const string RecordKind = "scriptRun";

    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public string Kind { get; set; } = RecordKind;
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string TenantId { get; set; } = "";
    [JsonRequired] public string TenantName { get; set; } = "";
    /// <summary>The account the run was pinned to; the wrapper refuses any other signed-in account before reading.</summary>
    [JsonRequired] public string Account { get; set; } = "";
    [JsonRequired] public string Resource { get; set; } = "exchangeOnline";
    [JsonRequired] public string ItemId { get; set; } = "";
    [JsonRequired] public string ItemName { get; set; } = "";
    [JsonRequired] public string ManifestSha256 { get; set; } = "";
    [JsonRequired] public string ScriptSha256 { get; set; } = "";
    /// <summary>SHA-256 of the generated wrapper text that ran, which is the Copy script for the same values and time.</summary>
    [JsonRequired] public string WrapperSha256 { get; set; } = "";
    [JsonRequired] public string Runtime { get; set; } = "";
    [JsonRequired] public string ToolkitVersion { get; set; } = "";
    [JsonRequired] public string StartedAt { get; set; } = "";
    [JsonRequired] public string EndedAt { get; set; } = "";
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? ExitCode { get; set; }
    /// <summary>INT-071 read state: Collected, Partial, Failed or Cancelled.</summary>
    [JsonRequired] public string Status { get; set; } = ReportReadState.Failed;
    /// <summary>How the run ended, from <see cref="ScriptRunEnd"/>.</summary>
    [JsonRequired] public string End { get; set; } = "";
    [JsonRequired] public bool Partial { get; set; }
    [JsonRequired] public bool UnknownValues { get; set; }
    [JsonRequired] public bool RowsTruncated { get; set; }
    [JsonRequired] public List<ScriptRunInput> Inputs { get; set; } = [];
    [JsonRequired] public List<string> Warnings { get; set; } = [];
    [JsonRequired] public List<string> Messages { get; set; } = [];
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Failure { get; set; }
    [JsonRequired] public List<string> Columns { get; set; } = [];
    [JsonRequired] public List<List<string>> Rows { get; set; } = [];
    [JsonRequired] public List<string> Limitations { get; set; } = [];
    [JsonRequired] public string IntegrityDigest { get; set; } = "";
}

/// <summary>Builds, seals, writes and strictly reads run records. Unknown members, missing members and inconsistent states are refused.</summary>
public static class ScriptRunSchema
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public const int MaximumRows = 50_000;
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ItemId = new("^[a-z0-9]+(\\.[a-z0-9]+(-[a-z0-9]+)*)+$", RegexOptions.CultureInvariant);
    private static readonly Regex Name = new("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Utc = new("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{1,7})?Z$", RegexOptions.CultureInvariant);

    public static readonly IReadOnlyList<string> StandingLimitations = new[]
    {
        "Run history, not configuration evidence: it cannot satisfy an assessment control or serve as before-evidence for a change.",
        "Live behaviour of this library item is not yet verified in a tenant."
    };

    private static readonly JsonSerializerOptions Strict = MakeOptions();

    private static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions(ToolkitJson.Options)
        {
            PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never, MaxDepth = 16, RespectNullableAnnotations = true
        };
        options.Converters.Clear();
        return options;
    }

    /// <summary>
    /// The record for a finished run. Rows are kept only for a completed run; if they would not fit the reader limit, the
    /// record keeps the metadata and says the rows were too large rather than storing a cut-down table as complete.
    /// </summary>
    public static ScriptRunRecord Create(ScriptRunRequest request, ScriptRunResult result, string toolkitVersion, Guid? id = null)
    {
        var m = request.Entry.Manifest;
        var record = new ScriptRunRecord
        {
            Id = (id ?? Guid.NewGuid()).ToString("D"),
            TenantId = Guid.Parse(request.Target.TenantId!).ToString("D"),
            TenantName = Single(request.Target.TenantName ?? "", 200),
            Account = request.Target.Account!.Trim(),
            ItemId = m.Id, ItemName = m.Name,
            ManifestSha256 = request.Entry.ManifestSha256, ScriptSha256 = m.ScriptSha256, WrapperSha256 = result.WrapperSha256,
            Runtime = result.Runtime, ToolkitVersion = toolkitVersion,
            StartedAt = Stamp(result.StartedAt), EndedAt = Stamp(result.EndedAt < result.StartedAt ? result.StartedAt : result.EndedAt),
            ExitCode = result.ExitCode, Status = result.Status, End = result.End.ToString(),
            Partial = result.Partial, UnknownValues = result.UnknownValues, RowsTruncated = result.RowsTruncated,
            Inputs = request.Binding.Arguments.Select(a => new ScriptRunInput { Name = a.Name, Value = ScriptCopy.Literal(a.Value) }).ToList(),
            Warnings = result.Warnings.Select(w => Single(w, 512)).ToList(),
            Messages = result.Messages.Select(w => Single(w, 512)).ToList(),
            Failure = result.Failure is null ? null : Single(result.Failure, 2048),
            Columns = result.Columns.ToList(),
            Rows = result.End == ScriptRunEnd.Completed ? result.Rows.Select(r => r.ToList()).ToList() : [],
            Limitations = StandingLimitations.ToList()
        };
        if (result.UnknownValues) record.Limitations.Add("Some values could not be read; those rows say Unknown with a reason. They were not checked.");
        if (result.Partial) record.Limitations.Add("Partial: the script or the run limit stopped before every row was read.");
        Seal(record);
        if (Encoding.UTF8.GetByteCount(Serialize(record)) > MaximumBytes)
        {
            record.Rows = [];
            record.Status = ReportReadState.Failed;
            record.End = nameof(ScriptRunEnd.OutputLimit);
            record.Failure = "The rows were too large to keep within the 32 MiB record limit. Narrow the form and run again.";
            Seal(record);
        }
        return record;
    }

    public static string Serialize(ScriptRunRecord record) => JsonSerializer.Serialize(record, Strict);

    public static void Seal(ScriptRunRecord record)
    {
        record.IntegrityDigest = EvidenceIntegrity.Compute(record);
        Validate(record, record.TenantId);
    }

    public static ScriptRunRecord Read(string json, string expectedTenantId)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw Bad("The run record exceeds the 32 MiB reader limit.");
        try
        {
            using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 }))
                ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.Ordinal);
            var record = JsonSerializer.Deserialize<ScriptRunRecord>(json, Strict) ?? throw Bad("The run record is empty.");
            Validate(record, expectedTenantId);
            return record;
        }
        catch (JsonException ex) { throw Bad("The run record is not valid: " + ex.Message); }
    }

    public static void Validate(ScriptRunRecord r, string expectedTenantId)
    {
        if (r.SchemaVersion != 1 || r.Kind != ScriptRunRecord.RecordKind || r.Resource != "exchangeOnline") throw Bad("Unsupported run record kind, resource or schema.");
        CanonicalGuid(r.Id); CanonicalGuid(r.TenantId);
        if (!Guid.TryParse(expectedTenantId, out var expected) || expected.ToString("D") != r.TenantId)
            throw new TenantMismatchException("The run record belongs to another tenant.");
        Text(r.TenantName, 200, allowEmpty: true);
        if (!ScriptCopy.IsAcceptableAccount(r.Account) || r.Account != r.Account.Trim()) throw Bad("The run record's account is not a sign-in name.");
        if (!ItemId.IsMatch(r.ItemId) || r.ItemId.Length > 80) throw Bad("The run record's item ID is malformed.");
        Text(r.ItemName, 80);
        if (!Sha256.IsMatch(r.ManifestSha256) || !Sha256.IsMatch(r.ScriptSha256) || !Sha256.IsMatch(r.WrapperSha256)) throw Bad("The run record's pins are malformed.");
        if (r.Runtime is not ("5.1" or "7")) throw Bad("The run record names an unsupported PowerShell.");
        Text(r.ToolkitVersion, 128);
        if (!Utc.IsMatch(r.StartedAt) || !Utc.IsMatch(r.EndedAt)
            || !DateTimeOffset.TryParse(r.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var started)
            || !DateTimeOffset.TryParse(r.EndedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ended) || ended < started)
            throw Bad("The run record's times are invalid.");
        if (!Enum.TryParse<ScriptRunEnd>(r.End, ignoreCase: false, out var end) || end.ToString() != r.End) throw Bad("The run record's end is unknown.");
        if (r.Status is not (ReportReadState.Collected or ReportReadState.Partial or ReportReadState.Failed or ReportReadState.Cancelled))
            throw Bad("The run record's status is unknown.");

        // States must agree: a run can never claim a better result than how it ended.
        var completed = end == ScriptRunEnd.Completed;
        var expectedStatus = completed ? (r.Partial ? ReportReadState.Partial : ReportReadState.Collected)
            : end == ScriptRunEnd.Cancelled ? ReportReadState.Cancelled : ReportReadState.Failed;
        if (r.Status != expectedStatus) throw Bad("The run record's status does not match how it ended.");
        if (r.RowsTruncated && !r.Partial) throw Bad("A run that kept only some rows must be marked partial.");
        if (completed && (r.ExitCode != 0 || r.Failure is not null)) throw Bad("A completed run needs exit code 0 and no failure.");
        if (!completed && string.IsNullOrWhiteSpace(r.Failure)) throw Bad("A run that did not complete must say why.");
        if (r.Failure is not null) Text(r.Failure, 2048);

        if (r.Inputs is null || r.Inputs.Count > 64 || r.Inputs.Any(i => i is null || !Name.IsMatch(i.Name) || i.Value is null || i.Value.Length > 64 * 1024)
            || r.Inputs.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Inputs.Count)
            throw Bad("The run record's inputs are invalid.");
        Strings(r.Warnings, 128, 512); Strings(r.Messages, 64, 512); Strings(r.Limitations, 16, 400);
        if (r.Columns is null || r.Columns.Count is 0 or > 40 || r.Columns.Any(c => c is null || !Name.IsMatch(c))
            || r.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Columns.Count)
            throw Bad("The run record's columns are invalid.");
        if (r.Rows is null || r.Rows.Count > MaximumRows || r.Rows.Any(row => row is null || row.Count != r.Columns.Count || row.Any(cell => cell is null)))
            throw Bad("The run record's rows do not match its columns.");
        if (!completed && r.Rows.Count != 0) throw Bad("A run that did not complete cannot keep rows.");
        if (r.IntegrityDigest is not { Length: 64 } || !Sha256.IsMatch(r.IntegrityDigest) || !EvidenceIntegrity.Verify(r, r.IntegrityDigest))
            throw Bad("The run record failed its integrity check. The digest detects change; it is not a signature.");
    }

    /// <summary>The rows as a spreadsheet-safe CSV (formula-like cells are prefixed), with the declared columns first.</summary>
    public static string ToCsv(ScriptRunRecord record) =>
        CsvWriter.Write(new[] { record.Columns.ToArray() }.Concat(record.Rows.Select(r => r.ToArray())));

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string Single(string text, int maximum)
    {
        var plain = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return plain.Length > maximum ? plain[..maximum] : plain;
    }

    private static void CanonicalGuid(string? value)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty || parsed.ToString("D") != value) throw Bad("The run record has a malformed identity.");
    }

    private static void Text(string? value, int maximum, bool allowEmpty = false)
    {
        if (value is null || (!allowEmpty && value.Trim().Length == 0) || value.Length > maximum || value.Any(char.IsControl)) throw Bad("The run record has invalid text.");
    }

    private static void Strings(List<string>? values, int count, int length)
    {
        if (values is null || values.Count > count || values.Any(v => v is null || v.Length > length || v.Any(char.IsControl))) throw Bad("The run record has invalid notes.");
    }

    private static ConfigurationException Bad(string message) => new(message);
}
