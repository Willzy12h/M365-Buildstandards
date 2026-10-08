using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Scripts;

public sealed record ScriptRegistryEntry(string Id, string Manifest, string ManifestSha256, string ScriptSha256);
public sealed record ScriptRegistry(int SchemaVersion, IReadOnlyList<ScriptRegistryEntry> Scripts);

/// <summary>
/// The reviewed script library (INT-072). Only package-owned files listed in <c>scripts/registry.json</c> are loaded, and
/// each manifest and script must match the SHA-256 the registry pins. Nothing an engineer types can name a file or a
/// command: a form only supplies typed values for a registered item's declared parameters.
/// </summary>
public sealed class ScriptCatalogue
{
    public const string RegistryPath = "registry.json";
    private const string ResourcePrefix = "Scripts/";
    private const int MaximumFileBytes = 256 * 1024;

    /// <summary>Command verbs a read-only script may use. Anything else, written as Verb-Noun, refuses the whole library.</summary>
    public static readonly IReadOnlySet<string> ReadOnlyVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        "Get", "Search", "Select", "Where", "ForEach", "Sort", "Group", "Measure", "Write", "ConvertTo", "ConvertFrom", "Test", "Format"
    };

    /// <summary>The one non-read command a read-only script may use: it only tightens the script's own error checking.</summary>
    private static readonly IReadOnlySet<string> LocalCommands = new HashSet<string>(StringComparer.Ordinal) { "Set-StrictMode" };

    private static readonly IReadOnlySet<string> Runtimes = new HashSet<string>(StringComparer.Ordinal) { "5.1", "7" };
    private static readonly IReadOnlySet<string> SupportedResources = new HashSet<string>(StringComparer.Ordinal) { "exchangeOnline" };
    private static readonly Regex IdPattern = new("^[a-z0-9]+(\\.[a-z0-9]+(-[a-z0-9]+)*)+$", RegexOptions.CultureInvariant);
    private static readonly Regex CategoryPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex FilePattern = new("^[A-Za-z0-9]+(-[A-Za-z0-9]+)*\\.(ps1|json)$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ParameterNamePattern = new("^[A-Z][A-Za-z0-9]{1,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex ColumnPattern = new("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new("^[0-9]+(\\.[0-9]+){1,3}$", RegexOptions.CultureInvariant);
    private static readonly Regex CommandPattern = new("(?<![A-Za-z0-9_-])([A-Z][A-Za-z]+)-([A-Z][A-Za-z0-9]+)", RegexOptions.CultureInvariant);
    private static readonly Regex RequiresPattern = new("^\\s*#requires", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private static readonly Lazy<ScriptCatalogue> Embedded = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<ScriptEntry> Entries { get; }

    private ScriptCatalogue(IReadOnlyList<ScriptEntry> entries) => Entries = entries;

    /// <summary>The library shipped inside the engine assembly, checked once per process.</summary>
    public static ScriptCatalogue Shipped => Embedded.Value;

    public ScriptEntry Find(string id) =>
        Entries.FirstOrDefault(e => string.Equals(e.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ConfigurationException($"No library item has the ID '{id}'. Run 'bdit scripts' to list them.");

    /// <summary>Matches every word against the name, ID, area, description and keywords, ignoring case.</summary>
    public IReadOnlyList<ScriptEntry> Search(string? text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Entries.Where(e =>
        {
            var haystack = string.Join(' ', new[] { e.Manifest.Name, e.Manifest.Id, e.Manifest.Area, e.Manifest.Description }.Concat(e.Manifest.Keywords));
            return words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private static ScriptCatalogue LoadEmbedded()
    {
        var assembly = typeof(ScriptCatalogue).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // MSBuild's RecursiveDir uses the build machine's separator; the library always uses '/'.
            var normalised = name.Replace('\\', '/');
            if (!normalised.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files[normalised[ResourcePrefix.Length..]] = buffer.ToArray();
        }
        return Load(files);
    }

    /// <summary>Loads and checks a whole library. Any problem refuses the library rather than skipping the item.</summary>
    public static ScriptCatalogue Load(IReadOnlyDictionary<string, byte[]> files)
    {
        foreach (var (path, bytes) in files)
        {
            CheckContainedPath(path);
            if (bytes.Length > MaximumFileBytes) throw Refuse($"{path} is larger than 256 KiB.");
        }
        if (!files.TryGetValue(RegistryPath, out var registryBytes)) throw Refuse("the registry is missing.");
        var registry = Deserialize<ScriptRegistry>(registryBytes, RegistryPath);
        if (registry.SchemaVersion != 1) throw Refuse("the registry schema version is not 1.");

        var used = new HashSet<string>(StringComparer.Ordinal) { RegistryPath };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ScriptEntry>();
        foreach (var item in registry.Scripts)
        {
            if (!ids.Add(item.Id)) throw Refuse($"the ID {item.Id} is registered twice.");
            if (!Sha256Pattern.IsMatch(item.ManifestSha256) || !Sha256Pattern.IsMatch(item.ScriptSha256))
                throw Refuse($"{item.Id} has a malformed SHA-256 in the registry.");
            CheckContainedPath(item.Manifest);
            if (!files.TryGetValue(item.Manifest, out var manifestBytes)) throw Refuse($"{item.Manifest} is registered but missing.");
            if (Sha256(manifestBytes) != item.ManifestSha256) throw Refuse($"{item.Manifest} does not match its registered SHA-256.");
            var manifest = Deserialize<ScriptManifest>(manifestBytes, item.Manifest);
            if (manifest.Id != item.Id) throw Refuse($"{item.Manifest} declares the ID {manifest.Id}, not {item.Id}.");
            if (!item.Manifest.StartsWith(manifest.Category + "/", StringComparison.Ordinal))
                throw Refuse($"{item.Manifest} is not in its category folder {manifest.Category}.");

            CheckContainedPath(manifest.ScriptPath);
            if (!manifest.ScriptPath.StartsWith(manifest.Category + "/", StringComparison.Ordinal) || !manifest.ScriptPath.EndsWith(".ps1", StringComparison.Ordinal))
                throw Refuse($"{manifest.Id} must name a .ps1 script in its category folder.");
            if (!files.TryGetValue(manifest.ScriptPath, out var scriptBytes)) throw Refuse($"{manifest.ScriptPath} is missing.");
            var scriptSha = Sha256(scriptBytes);
            if (scriptSha != manifest.ScriptSha256 || scriptSha != item.ScriptSha256)
                throw Refuse($"{manifest.ScriptPath} does not match the SHA-256 its manifest and the registry pin.");
            if (!used.Add(item.Manifest) || !used.Add(manifest.ScriptPath)) throw Refuse($"{manifest.Id} shares a file with another item.");

            var script = DecodeAscii(scriptBytes, manifest.ScriptPath);
            ValidateManifest(manifest);
            ValidateScript(manifest, script);
            entries.Add(new ScriptEntry(manifest, script, item.ManifestSha256));
        }
        var unlisted = files.Keys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (unlisted.Count > 0) throw Refuse("these files are not in the registry: " + string.Join(", ", unlisted) + ".");
        return new ScriptCatalogue(entries.OrderBy(e => e.Manifest.Area, StringComparer.Ordinal).ThenBy(e => e.Manifest.Name, StringComparer.Ordinal).ToList());
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static void ValidateManifest(ScriptManifest m)
    {
        var at = m.Id;
        if (m.SchemaVersion != 1) throw Refuse($"{at}: schema version must be 1.");
        if (!IdPattern.IsMatch(m.Id) || m.Id.Length > 80) throw Refuse($"{at}: the ID must be lower-case dotted words, for example exo.message-trace.");
        if (!CategoryPattern.IsMatch(m.Category)) throw Refuse($"{at}: the category must be a lower-case folder name.");
        RequireText(at, "name", m.Name, 80);
        RequireText(at, "area", m.Area, 60);
        RequireText(at, "description", m.Description, 600);
        RequireText(at, "prerequisites", m.Prerequisites, 600);
        if (!Sha256Pattern.IsMatch(m.ScriptSha256)) throw Refuse($"{at}: scriptSha256 must be 64 lower-case hex characters.");
        foreach (var keyword in m.Keywords) RequireText(at, "keyword", keyword, 40);
        foreach (var limitation in m.Limitations) RequireText(at, "limitation", limitation, 400);
        foreach (var role in m.Roles) RequireText(at, "role", role, 80);
        foreach (var scope in m.Scopes) RequireText(at, "scope", scope, 80);
        if (m.SupportedRuntimes.Count == 0 || m.SupportedRuntimes.Any(r => !Runtimes.Contains(r)) || m.SupportedRuntimes.Distinct().Count() != m.SupportedRuntimes.Count)
            throw Refuse($"{at}: supported runtimes must be 5.1 and/or 7.");
        // Only Exchange Online has a reviewed connection and tenant check in this release. Graph items come from the
        // registered report services, and Purview needs its own reviewed connection before any script may target it.
        if (m.Resources.Count != 1 || !SupportedResources.Contains(m.Resources[0])) throw Refuse($"{at}: the only supported resource is exchangeOnline.");
        if (m.Modules.Count == 0) throw Refuse($"{at}: name the PowerShell module the script needs.");
        foreach (var module in m.Modules)
        {
            if (!Regex.IsMatch(module.Name, "^[A-Za-z][A-Za-z0-9.]{1,63}$", RegexOptions.CultureInvariant)) throw Refuse($"{at}: module name {module.Name} is not valid.");
            if (!VersionPattern.IsMatch(module.MinimumVersion)) throw Refuse($"{at}: module {module.Name} needs a minimum version such as 3.7.0.");
        }
        if (m.LiveStatus != ScriptLiveStatus.Unverified) throw Refuse($"{at}: only William records live acceptance; new items are unverified.");
        if (m.OutputSchemaVersion != 1) throw Refuse($"{at}: output schema version must be 1.");
        if (m.OutputSchema.Columns.Count is 0 or > 40 || m.OutputSchema.Columns.Any(c => !ColumnPattern.IsMatch(c))
            || m.OutputSchema.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.OutputSchema.Columns.Count)
            throw Refuse($"{at}: output columns must be 1–40 unique names.");
        if (m.Limits.MaximumRows is < 1 or > 50_000) throw Refuse($"{at}: maximum rows must be between 1 and 50,000.");
        if (m.Limits.TimeoutSeconds is < 10 or > 3_600) throw Refuse($"{at}: the timeout must be between 10 seconds and an hour.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in m.Parameters)
        {
            var pat = $"{at} parameter {p.Name}";
            if (!ParameterNamePattern.IsMatch(p.Name) || !names.Add(p.Name)) throw Refuse($"{pat}: names are unique PascalCase words.");
            RequireText(pat, "label", p.Label, 60);
            RequireText(pat, "help", p.Help, 300);
            if (p.Format is not null && p.Type != ScriptParameterType.String) throw Refuse($"{pat}: only string parameters have a format.");
            if (p.MaxLength is not null && (p.Type != ScriptParameterType.String || p.MaxLength is < 1 or > 1_000)) throw Refuse($"{pat}: maxLength is 1–1000 and only for strings.");
            if (p.Array != (p.MaxItems is not null)) throw Refuse($"{pat}: array parameters, and only they, set maxItems.");
            if (p.MaxItems is < 1 or > 100) throw Refuse($"{pat}: maxItems must be 1–100.");
            if (p.Array && p.Type is not (ScriptParameterType.String or ScriptParameterType.Enum)) throw Refuse($"{pat}: only text and choice fields take several values.");
            if (p.Type == ScriptParameterType.Boolean && p.Required) throw Refuse($"{pat}: a tick box cannot be required.");
            if ((p.Minimum is not null || p.Maximum is not null) && p.Type != ScriptParameterType.Integer) throw Refuse($"{pat}: only integers have bounds.");
            if (p.Type == ScriptParameterType.Integer && (p.Minimum is null || p.Maximum is null || p.Minimum > p.Maximum)) throw Refuse($"{pat}: integers need a minimum and maximum.");
            if ((p.Allowed is not null) != (p.Type == ScriptParameterType.Enum)) throw Refuse($"{pat}: enum parameters, and only they, list allowed values.");
            if (p.Allowed is not null && (p.Allowed.Count == 0 || p.Allowed.Any(a => !ColumnPattern.IsMatch(a)) || p.Allowed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.Allowed.Count))
                throw Refuse($"{pat}: allowed values must be unique plain words.");
            if (p.Default is not null)
            {
                if (p.Required) throw Refuse($"{pat}: a required field has no default; the engineer enters it.");
                var problems = new List<string>();
                if (ScriptInputs.Convert(p, p.Default, problems) is null || problems.Count > 0) throw Refuse($"{pat}: the default is not a valid value.");
            }
        }
        foreach (var rule in m.Rules)
        {
            switch (rule.Kind)
            {
                case ScriptRuleKind.AtLeastOne:
                    if (rule.Parameters is null || rule.Parameters.Count < 2 || rule.Start is not null || rule.End is not null || rule.MaximumDays is not null || rule.MaximumAgeDays is not null)
                        throw Refuse($"{at}: an atLeastOne rule names two or more parameters and nothing else.");
                    foreach (var name in rule.Parameters)
                    {
                        var p = m.Parameters.FirstOrDefault(x => x.Name == name) ?? throw Refuse($"{at}: rule names unknown parameter {name}.");
                        if (p.Required || p.Type == ScriptParameterType.Boolean || p.Default is not null)
                            throw Refuse($"{at}: rule parameter {name} must be optional, have no default and not be a tick box.");
                    }
                    break;
                case ScriptRuleKind.DateRange:
                    if (rule.Parameters is not null || rule.Start is null || rule.End is null || rule.MaximumDays is null or < 1 or > 366 || rule.MaximumAgeDays is < 1 or > 3_650)
                        throw Refuse($"{at}: a dateRange rule names start and end dates and 1–366 maximum days.");
                    foreach (var name in new[] { rule.Start, rule.End })
                    {
                        var p = m.Parameters.FirstOrDefault(x => x.Name == name) ?? throw Refuse($"{at}: rule names unknown parameter {name}.");
                        if (p.Type != ScriptParameterType.Date || p.Array) throw Refuse($"{at}: date range parameter {name} must be a single date.");
                    }
                    break;
                default:
                    throw Refuse($"{at}: unknown rule.");
            }
        }
    }

    internal static void ValidateScript(ScriptManifest m, string script)
    {
        // The Copy script places the body inside a script block, where #requires is not allowed; requirements are
        // generated from the manifest instead.
        if (RequiresPattern.IsMatch(script)) throw Refuse($"{m.Id}: put requirements in the manifest, not a #requires line.");
        if (!script.Contains("param(", StringComparison.Ordinal)) throw Refuse($"{m.Id}: the script must declare its parameters with param().");
        foreach (var p in m.Parameters)
            if (!Regex.IsMatch(script, "\\$" + p.Name + "\\b", RegexOptions.CultureInvariant))
                throw Refuse($"{m.Id}: parameter {p.Name} is declared but the script never uses ${p.Name}.");
        if (m.Mode != ScriptMode.ReadOnly) return;
        foreach (Match command in CommandPattern.Matches(script))
            if (!ReadOnlyVerbs.Contains(command.Groups[1].Value) && !LocalCommands.Contains(command.Value))
                throw Refuse($"{m.Id}: read-only scripts cannot use {command.Value}.");
        foreach (var token in new[] { "Invoke-", "iex ", "Add-Type", "[scriptblock]::Create", "Start-Process", ".Invoke(" })
            if (script.Contains(token, StringComparison.OrdinalIgnoreCase)) throw Refuse($"{m.Id}: read-only scripts cannot use {token.Trim()}.");
    }

    private static void RequireText(string at, string field, string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim() || value.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
            throw Refuse($"{at}: {field} must be 1–{maximum} characters of plain single-line text.");
    }

    private static void CheckContainedPath(string path)
    {
        // Either the registry at the root, or category/file.ext: no rooted paths, no '..', no deeper folders.
        var parts = path.Split('/');
        var contained = path.Length <= 160 && parts.Length switch
        {
            1 => FilePattern.IsMatch(parts[0]),
            2 => CategoryPattern.IsMatch(parts[0]) && FilePattern.IsMatch(parts[1]),
            _ => false
        };
        if (!contained) throw Refuse($"'{path}' is not a contained library path.");
    }

    private static string DecodeAscii(byte[] bytes, string path)
    {
        // Windows PowerShell 5.1 reads a file without a byte order mark as the system code page, so library scripts
        // stay ASCII and read identically everywhere.
        if (bytes.Any(b => b > 0x7E || (b < 0x20 && b is not (byte)'\n' and not (byte)'\r' and not (byte)'\t')))
            throw Refuse($"{path} must be plain ASCII text.");
        return Encoding.ASCII.GetString(bytes);
    }

    private static T Deserialize<T>(byte[] bytes, string path)
    {
        try
        {
            using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 })) RejectDuplicates(document.RootElement, path);
            return JsonSerializer.Deserialize<T>(bytes, StrictJson) ?? throw Refuse($"{path} is empty.");
        }
        catch (JsonException ex) { throw Refuse($"{path} is not a valid library file: {ex.Message}"); }
    }

    private static void RejectDuplicates(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw Refuse($"{path} repeats the property {property.Name}.");
                RejectDuplicates(property.Value, path);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item, path);
    }

    private static ConfigurationException Refuse(string message) => new("The script library was refused: " + message);
}
