using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Scripts.Runner;
using BuildStandard.Engine.Standards;

namespace BuildStandard.Engine.Diagnostics;

internal sealed record OfflineDiagnosticHost(bool WindowsX64, Func<string, string?> PowerShellPath)
{
    internal static OfflineDiagnosticHost Default => new(OperatingSystem.IsWindows()
        && RuntimeInformation.OSArchitecture == Architecture.X64, ReadRunnerHost.Default.RuntimePath);
}

/// <summary>Shared local checks. Reads only admitted package files/settings; never profiles, evidence, caches or logs.</summary>
public sealed partial class OfflinePrerequisiteService
{
    private readonly OfflineDiagnosticLimits _limits;
    private readonly IClock _clock;
    private readonly OfflineDiagnosticIo _io;
    private readonly OfflineDiagnosticHost _host;
    public static string ToolkitVersion { get; } = typeof(ToolkitSettings).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
    private static readonly string[] LayoutFiles = ["Start.cmd", "buildstandard.cmd", "bdit.cmd", "app/BuildStandard.App.exe",
        "app/BuildStandard.App.dll", "app/BuildStandard.App.deps.json",
        "app/BuildStandard.App.runtimeconfig.json", "app/System.Private.CoreLib.dll"];
    private static readonly string[] WritableComponents = ["config", "data", "logs", "reports"];
    private static readonly string[] RequiredIds = ["root.access", "platform", "package.layout", "package.metadata",
        "package.integrity", "settings", "standard", "writable.config", "cleanup.config", "writable.data", "cleanup.data",
        "writable.logs", "cleanup.logs", "writable.reports", "cleanup.reports"];

    public OfflinePrerequisiteService(OfflineDiagnosticLimits? limits = null, IClock? clock = null)
        : this(limits ?? new(), clock ?? SystemClock.Instance, new(), OfflineDiagnosticHost.Default) { }

    internal OfflinePrerequisiteService(OfflineDiagnosticLimits limits, IClock clock, OfflineDiagnosticIo io, OfflineDiagnosticHost host)
    {
        limits.Validate();
        _limits = limits; _clock = clock; _io = io; _host = host;
    }

    public OfflinePrerequisiteResult Check(string root, CancellationToken cancellationToken = default)
    {
        var started = Timestamps.Format(_clock.UtcNow);
        string fullRoot;
        try
        {
            if (string.IsNullOrWhiteSpace(root) || root.StartsWith("\\\\", StringComparison.Ordinal)
                || root.StartsWith("//", StringComparison.Ordinal)) throw new OfflineDiagnosticInputException();
            fullRoot = Path.GetFullPath(root);
            if (fullRoot.Equals(Path.GetPathRoot(fullRoot), StringComparison.OrdinalIgnoreCase)) throw new OfflineDiagnosticInputException();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new OfflineDiagnosticInputException(); }

        var files = new OfflinePackageFiles(fullRoot, _io, _limits);
        var findings = new List<OfflinePrerequisiteFinding>();
        var cancelled = false;
        try
        {
            try { files.CheckDirectory(fullRoot); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnsafeOfflinePathException)
            { throw new OfflineDiagnosticInputException(); }
            catch (Exception ex) when (AccessFailure(ex))
            {
                cancelled = cancellationToken.IsCancellationRequested;
                Add("root.access", OfflineFindingDisposition.Unknown, "The existing root could not be inspected.", "Ask the security owner to review access to the portable folder.");
                return Finish();
            }
            cancellationToken.ThrowIfCancellationRequested();
            Add("root.access", OfflineFindingDisposition.Pass, "The ordinary root folder was admitted.", "Keep the extracted package in a trusted local folder.");
            Add("platform", _host.WindowsX64 ? OfflineFindingDisposition.Pass : OfflineFindingDisposition.Fail,
                _host.WindowsX64 ? "Windows x64 is applicable to this portable build." : "This portable build requires Windows x64.",
                "Use an organisation-approved Windows x64 machine.");
            CheckFinding("package.layout", () =>
            {
                foreach (var path in LayoutFiles) { cancellationToken.ThrowIfCancellationRequested(); files.Admit(path); }
                files.Admit("standards", true); files.Admit("config", true);
                return (OfflineFindingDisposition.Pass, "The expected portable application and catalogue layout is present.");
            }, "Extract the complete, independently trusted portable ZIP.");

            string? packagedDefault = null;
            CheckFinding("package.metadata", () =>
            {
                var text = files.ReadText("VERSION.json", _limits.MetadataBytes, cancellationToken);
                return Document(() =>
                {
                    using var json = JsonDocument.Parse(text, ToolkitJson.DocumentOptions);
                    var metadata = json.RootElement;
                    RejectDuplicateProperties(metadata);
                    var release = metadata.GetProperty("defaultStandard").GetString() ?? "";
                    if (metadata.GetProperty("version").GetString() != ToolkitVersion
                        || metadata.GetProperty("runtime").GetString() != "win-x64"
                        || !metadata.GetProperty("selfContained").GetBoolean()
                        || metadata.GetProperty("sourceTreeDirty").GetBoolean()
                        || !DigestPattern40().IsMatch(metadata.GetProperty("sourceCommit").GetString() ?? "")
                        || !(metadata.GetProperty("dotnetRuntime").GetString() ?? "").StartsWith("10.", StringComparison.Ordinal)
                        || !OfflinePackageFiles.CatalogueName(release + ".json"))
                        return (OfflineFindingDisposition.Fail, "Published package metadata is inconsistent with this self-contained build.");
                    packagedDefault = release;
                    return (OfflineFindingDisposition.Pass, "Published self-contained .NET 10 metadata is valid; an installed SDK is not required.");
                });
            }, "Use a complete released package; a source checkout without published metadata cannot establish package readiness.");

            Dictionary<string, string>? inventory = null;
            CheckFinding("package.integrity", () =>
            {
                inventory = ParseInventory(files.ReadText("SHA256SUMS.txt", _limits.ManifestBytes, cancellationToken));
                var required = LayoutFiles.Concat(["VERSION.json", "standards/manifest.json", "config/toolkit.settings.json"]);
                if (packagedDefault is not null) required = required.Append("standards/" + packagedDefault + ".json");
                if (required.Any(path => !inventory.ContainsKey(path)))
                    return (OfflineFindingDisposition.Fail, "The package inventory omits a required component.");
                var mismatch = false;
                foreach (var pair in inventory)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Settings are engineer-editable and are validated separately, never an immutable binary failure.
                    if (pair.Key.Equals("config/toolkit.settings.json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!files.Hash(pair.Key, cancellationToken).Equals(pair.Value, StringComparison.OrdinalIgnoreCase)) mismatch = true;
                }
                return mismatch
                    ? (OfflineFindingDisposition.Fail, "One or more immutable package files differ from SHA256SUMS.txt.")
                    : (OfflineFindingDisposition.Pass, "All admitted immutable inventory entries match SHA256SUMS.txt.");
            }, "Compare the ZIP with an independently trusted reference; adjacent unsigned checksums do not identify a publisher.");

            ToolkitSettings? settings = null;
            CheckFinding("settings", () =>
            {
                var bytes = files.ReadBytes("config/toolkit.settings.json", _limits.StructuredFileBytes, cancellationToken);
                settings = Document(() => ToolkitSettings.Validate(ToolkitJson.Deserialize<ToolkitSettings>(Decode(bytes))));
                var changed = inventory is not null && inventory.TryGetValue("config/toolkit.settings.json", out var digest)
                    && !files.Digest(bytes).Equals(digest, StringComparison.OrdinalIgnoreCase);
                return (OfflineFindingDisposition.Pass, changed
                    ? "Engineer-editable settings validate and differ from the packaged default; this is separate from immutable integrity."
                    : "Engineer-editable settings validate using the toolkit settings validator.");
            }, "Review config/toolkit.settings.json through the existing configuration flow; do not store secrets there.");

            CheckFinding("standard", () =>
            {
                var release = string.IsNullOrWhiteSpace(settings?.DefaultStandardRelease) ? packagedDefault : settings.DefaultStandardRelease;
                if (release is null) return (OfflineFindingDisposition.Unknown, "The default catalogue could not be established.");
                var name = release + ".json";
                if (!OfflinePackageFiles.CatalogueName(name)) throw new UnsafeOfflinePathException();
                var text = files.ReadText("standards/manifest.json", _limits.StructuredFileBytes, cancellationToken);
                var manifest = Document(() =>
                {
                    using var json = JsonDocument.Parse(text, ToolkitJson.DocumentOptions);
                    RejectDuplicateProperties(json.RootElement);
                    RejectDuplicateProperties(json.RootElement.GetProperty("files"));
                    var parsed = ToolkitJson.Deserialize<StandardsManifest>(text);
                    if (!string.Equals(parsed.Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase) || parsed.Files is null
                        || parsed.Files.Any(pair => !OfflinePackageFiles.CatalogueName(pair.Key) || !DigestPattern().IsMatch(pair.Value)))
                        throw new ConfigurationException("Invalid catalogue manifest.");
                    return parsed;
                });
                if (!manifest.Files.TryGetValue(name, out var expected))
                    return (OfflineFindingDisposition.Fail, "The selected default catalogue is not listed in its manifest.");
                var bytes = files.ReadBytes("standards/" + name, _limits.StructuredFileBytes, cancellationToken);
                if (!files.Digest(bytes).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    return (OfflineFindingDisposition.Fail, "The default catalogue differs from its recorded digest.");
                Document(() => StandardsLoader.Parse(Decode(bytes), name));
                return (OfflineFindingDisposition.Pass, "The default catalogue matches its SHA-256 manifest and passes the existing catalogue validator.");
            }, "Restore verified catalogue bytes; keep historical release files unchanged.");

            CheckFinding("package.additional", () => ObserveAdditional(files, fullRoot, inventory, cancellationToken),
                "Review additional files independently; they are not trusted by this diagnostic.", required: false);
            foreach (var component in WritableComponents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Probe(component, files, fullRoot, findings, cancellationToken);
            }
            foreach (var runtime in new[] { "5.1", "7" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckFinding("powershell." + runtime, () =>
                {
                    var path = _host.PowerShellPath(runtime);
                    if (path is null) return (OfflineFindingDisposition.NotApplicable, "The optional Windows PowerShell host does not apply on this platform.");
                    files.CheckDirectory(Path.GetDirectoryName(path)!);
                    var attributes = _io.Attributes(path);
                    return (attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)
                        ? OfflineFindingDisposition.Unknown : OfflineFindingDisposition.Pass,
                        "Optional PowerShell " + runtime + " was observed at its standard location; it was not executed or version-verified.");
                }, "Use the organisation-approved PowerShell installation and application-control process for optional Exchange features.", required: false);
            }
            Add("exchange.access", OfflineFindingDisposition.Unknown, "Exchange module loading, policy and tenant roles were not tested.",
                "Ask the organisation to approve the required host/module and validate authorised tenant access separately.", false);
            Add("package.trust", OfflineFindingDisposition.Unknown, "Adjacent unsigned checksums do not establish publisher trust or live capability.",
                "Use an independently trusted ZIP reference and retain the human/live acceptance gates.", false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled = true; }
        return Finish();

        void Add(string id, OfflineFindingDisposition disposition, string explanation, string nextStep, bool required = true) =>
            findings.Add(new(id, required ? OfflineFindingApplicability.Required : OfflineFindingApplicability.Optional, disposition, explanation, nextStep));

        void CheckFinding(string id, Func<(OfflineFindingDisposition Disposition, string Explanation)> check, string nextStep, bool required = true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { var result = check(); Add(id, result.Disposition, result.Explanation, nextStep, required); }
            catch (UnsafeOfflinePathException) { Add(id, OfflineFindingDisposition.Fail, "A component has an unsafe path, link or reparse point; no such content was read.", nextStep, required); }
            catch (OfflinePackageScopeException) { Add(id, OfflineFindingDisposition.Fail, "The inventory includes a component outside the supported package inspection scope; its contents were not read.", nextStep, required); }
            catch (OfflineBoundException) { Add(id, OfflineFindingDisposition.Unknown, "The check exceeded its configured file or inventory bound; it was not certified.", nextStep, required); }
            catch (Exception ex) when (AccessFailure(ex)) { Add(id, OfflineFindingDisposition.Unknown, "A component is missing or could not be read; the check is incomplete.", nextStep, required); }
            catch (OfflineDocumentException) { Add(id, OfflineFindingDisposition.Fail, "The component document is invalid for the existing toolkit format or validator.", nextStep, required); }
        }

        OfflinePrerequisiteResult Finish()
        {
            foreach (var id in RequiredIds.Where(id => findings.All(f => f.Id != id)))
                Add(id, OfflineFindingDisposition.Unknown, cancelled ? "The required check was not completed before cancellation." : "The required check could not be completed.", "Rerun diagnostics after resolving the reported prerequisite.");
            var status = cancelled ? OfflinePrerequisiteStatus.Cancelled
                : findings.Any(f => f.Applicability == OfflineFindingApplicability.Required && f.Disposition != OfflineFindingDisposition.Pass)
                    ? OfflinePrerequisiteStatus.NeedsAttention : OfflinePrerequisiteStatus.Ready;
            return new(ToolkitVersion, started, Timestamps.Format(_clock.UtcNow), status, findings.AsReadOnly());
        }
    }

    private Dictionary<string, string> ParseInventory(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } line)
        {
            if (result.Count >= _limits.ManifestEntries) throw new OfflineBoundException();
            if (line.Length < 67 || !DigestPattern().IsMatch(line[..64]) || line.Substring(64, 2) != "  ")
                throw new UnsafeOfflinePathException();
            var path = line[66..];
            if (!result.TryAdd(path, line[..64])) throw new UnsafeOfflinePathException();
            if (!OfflinePackageFiles.PackageContent(path))
            {
                if (!OfflinePackageFiles.SafeRelative(path) || OfflinePackageFiles.PrivatePath(path)) throw new UnsafeOfflinePathException();
                throw new OfflinePackageScopeException();
            }
        }
        if (result.Count == 0) throw new UnsafeOfflinePathException();
        return result;
    }

    private (OfflineFindingDisposition, string) ObserveAdditional(OfflinePackageFiles files, string root,
        Dictionary<string, string>? inventory, CancellationToken ct)
    {
        if (inventory is null) return (OfflineFindingDisposition.Unknown, "Additional package files cannot be compared without an admitted inventory.");
        var pending = new Stack<(string Path, string Relative)>();
        pending.Push((root, ""));
        var seen = 0;
        var additional = 0;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            files.CheckDirectory(directory.Path);
            foreach (var entry in _io.Entries(directory.Path))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen > _limits.InventoryEntries) throw new OfflineBoundException();
                var relative = directory.Relative + Path.GetFileName(entry);
                // Working/private areas are neither enumerated below nor opened, irrespective of inventory claims.
                if (directory.Relative.Length == 0 && new[] { "data", "logs", "reports", "transfers", ".git" }.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
                if (!OfflinePackageFiles.SafeRelative(relative) || OfflinePackageFiles.PrivatePath(relative)) { additional++; continue; }
                var attributes = _io.Attributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new UnsafeOfflinePathException();
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (relative.Count(c => c == '/') >= 16) throw new OfflineBoundException();
                    if (relative.Split('/')[0].ToLowerInvariant() is "app" or "standards" or "config" or "docs" or "licenses")
                        pending.Push((entry, relative + "/"));
                    else additional++;
                }
                else if (!relative.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) && !inventory.ContainsKey(relative)) additional++;
            }
        }
        return additional == 0 ? (OfflineFindingDisposition.Pass, "No additional files were found in the package inspection scope.")
            : (OfflineFindingDisposition.Unknown, $"{additional} additional package entries were found; their contents were not read or trusted.");
    }

    private void Probe(string component, OfflinePackageFiles files, string root, List<OfflinePrerequisiteFinding> findings, CancellationToken ct)
    {
        string? owned = null;
        var disposition = OfflineFindingDisposition.Pass;
        var explanation = "An exclusively created temporary probe could be written in " + component + ".";
        var cleanup = OfflineFindingDisposition.Pass;
        var cancel = false;
        try
        {
            files.CheckDirectory(root);
            string parent;
            try { parent = files.Admit(component, true); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                parent = root;
                explanation = component + " is absent; a probe under the root confirms parent-create capability only, not the future folder's ACLs.";
            }
            files.CheckDirectory(parent);
            var name = _io.ProbeName();
            if (!name.StartsWith(".buildstandard-doctor-", StringComparison.Ordinal) || !OfflinePackageFiles.SafeRelative(name) || name.Contains('/'))
                throw new UnsafeOfflinePathException();
            var path = Path.Combine(parent, name);
            using (var stream = _io.CreateProbe(path))
            {
                owned = path; // Ownership begins only after CreateNew succeeds; collisions never become ours.
                ct.ThrowIfCancellationRequested();
                stream.WriteByte(0);
                stream.Flush();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { cancel = true; disposition = OfflineFindingDisposition.Unknown; explanation = "The writable probe was interrupted by cancellation."; }
        catch (UnsafeOfflinePathException) { disposition = OfflineFindingDisposition.Fail; explanation = "The writable destination is unsafe or linked; no probe was admitted."; }
        catch (Exception ex) when (AccessFailure(ex)) { disposition = OfflineFindingDisposition.Unknown; explanation = "An exclusively created writable probe could not be completed in " + component + "."; }
        finally
        {
            if (owned is not null)
            {
                try
                {
                    // If the containing path changed, do not risk deleting through a new link.
                    files.CheckDirectory(Path.GetDirectoryName(owned)!);
                    if ((_io.Attributes(owned) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new UnsafeOfflinePathException();
                    _io.DeleteProbe(owned);
                    try { _io.Attributes(owned); cleanup = OfflineFindingDisposition.Fail; }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
                }
                catch (Exception ex) when (AccessFailure(ex) || ex is UnsafeOfflinePathException)
                { cleanup = OfflineFindingDisposition.Fail; }
            }
            findings.Add(new("writable." + component, OfflineFindingApplicability.Required, disposition, explanation, "Ask the security owner to review portable folder access; the toolkit does not change ACLs."));
            findings.Add(new("cleanup." + component, OfflineFindingApplicability.Required, cleanup,
                cleanup == OfflineFindingDisposition.Pass ? "No owned probe remains from the completed cleanup attempt."
                    : "Owned probe cleanup could not be completed; a temporary diagnostic file may remain in " + component + ".",
                "Review only temporary files beginning .buildstandard-doctor- in the reported component; do not delete existing working files."));
        }
        if (cancel) ct.ThrowIfCancellationRequested();
    }

    private static bool AccessFailure(Exception ex) => ex is IOException or UnauthorizedAccessException;
    private static bool InvalidDocument(Exception ex) => ex is ConfigurationException or JsonException or KeyNotFoundException
        or InvalidOperationException or DecoderFallbackException or NullReferenceException or ArgumentException;
    private static T Document<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (Exception ex) when (InvalidDocument(ex)) { throw new OfflineDocumentException(); }
    }
    private static string Decode(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
    private static void RejectDuplicateProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new UnsafeOfflinePathException();
    }
    [GeneratedRegex("^[a-fA-F0-9]{64}$")]
    private static partial Regex DigestPattern();
    [GeneratedRegex("^[a-fA-F0-9]{40}$")]
    private static partial Regex DigestPattern40();
}
