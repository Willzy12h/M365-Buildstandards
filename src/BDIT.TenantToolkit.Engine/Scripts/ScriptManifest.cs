namespace BDIT.TenantToolkit.Engine.Scripts;

/// <summary>INT-072 modes. There is no integrated-change mode: a change can only ever be copied.</summary>
public enum ScriptMode { ReadOnly, CopyOnlyChange }

public enum ScriptParameterType { String, Boolean, Integer, Date, Guid, Enum }

/// <summary>How a string parameter is checked. Text is any single line without control or formatting characters.</summary>
public enum ScriptValueFormat { Text, Upn, Smtp, Mailbox, Domain, IpAddress }

public enum ScriptRuleKind { AtLeastOne, DateRange }

public enum ScriptLiveStatus { Unverified, Verified }

public sealed record ScriptModule(string Name, string MinimumVersion);

/// <summary>
/// One form field. Blank optional fields are not passed to the script at all, so a search with only a recipient and a date
/// range searches on just those. Array fields take several values, separated by new lines, commas or semicolons.
/// </summary>
public sealed record ScriptParameter(
    string Name,
    string Label,
    ScriptParameterType Type,
    string Help,
    bool Required = false,
    bool Array = false,
    ScriptValueFormat? Format = null,
    int? MaxLength = null,
    int? MaxItems = null,
    long? Minimum = null,
    long? Maximum = null,
    IReadOnlyList<string>? Allowed = null,
    string? Default = null);

/// <summary>
/// A rule across fields. AtLeastOne needs a value in one of <see cref="Parameters"/>. DateRange bounds the days between
/// <see cref="Start"/> and <see cref="End"/> and, optionally, how far back the start may be.
/// </summary>
public sealed record ScriptRule(
    ScriptRuleKind Kind,
    IReadOnlyList<string>? Parameters = null,
    string? Start = null,
    string? End = null,
    int? MaximumDays = null,
    int? MaximumAgeDays = null);

public sealed record ScriptOutputSchema(IReadOnlyList<string> Columns);

public sealed record ScriptLimits(int MaximumRows, int TimeoutSeconds);

/// <summary>
/// INT-088 manifest schema 2 only: binds a reviewed read-only body to one registered Exchange report and to the engine's
/// own runner wrapper. Every value must equal the engine's registration; a manifest cannot define a report or a wrapper.
/// </summary>
public sealed record ScriptExecution(
    string Adapter,
    string ReportId,
    int ReportSchemaVersion,
    string OutputKind,
    int OutputSchemaVersion,
    string RunnerTemplateSha256);

/// <summary>
/// INT-072 manifest schema 1, with the additive library fields proposed in INT-080 (area, keywords, rules and string
/// formats). Strict: unknown members, missing required members and nulls in non-nullable members are refused. Schema 2
/// (INT-088) is the same shape plus a required <see cref="Execution"/>, which schema 1 must not have.
/// </summary>
public sealed record ScriptManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string Category,
    string Area,
    string Description,
    IReadOnlyList<string> Keywords,
    ScriptMode Mode,
    string ScriptPath,
    string ScriptSha256,
    IReadOnlyList<string> SupportedRuntimes,
    IReadOnlyList<ScriptModule> Modules,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Resources,
    IReadOnlyList<ScriptParameter> Parameters,
    IReadOnlyList<ScriptRule> Rules,
    ScriptOutputSchema OutputSchema,
    int OutputSchemaVersion,
    ScriptLimits Limits,
    string Prerequisites,
    ScriptLiveStatus LiveStatus,
    IReadOnlyList<string> Limitations,
    ScriptExecution? Execution = null);

/// <summary>A registered, digest-checked library item: its manifest and the exact script bytes it pins.</summary>
public sealed record ScriptEntry(ScriptManifest Manifest, string Script, string ManifestSha256);
