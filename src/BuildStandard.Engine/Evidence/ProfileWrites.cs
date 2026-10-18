using BuildStandard.Core.Models;
using BuildStandard.Engine.Identity;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildStandard.Core;
using BuildStandard.Core.Json;

namespace BuildStandard.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private static readonly JsonSerializerOptions ProfileCopyOptions = new(ToolkitJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    public IReadOnlyList<TenantProfile> LoadClientProfiles()
    {
        try
        {
            if (!ReviewPathExists(_paths.ProfilesFile, false)) return [];
            using var stream = new FileStream(_paths.ProfilesFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 8 * 1024 * 1024) throw new ConfigurationException("Saved clients exceed 8 MiB. Resolve the local profile store before continuing.");
            var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new ConfigurationException("Saved clients changed during reading. Refresh and try again.");
            var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(text, ToolkitJson.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ConfigurationException("The saved-client register must be a JSON array.");
            if (document.RootElement.GetArrayLength() > 5000) throw new ConfigurationException("Saved clients exceed the 5,000-client limit. No partial list is used.");
            var profiles = ToolkitJson.Deserialize<List<TenantProfile>>(text);
            _ = PartnerCustomerImport.Fingerprint(profiles);
            return profiles.AsReadOnly();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException or System.Security.SecurityException)
        { throw new ConfigurationException("Saved clients could not be read safely. Preserve the original profile file and resolve its format or access before continuing.", ex); }
    }

    /// <summary>Every participating writer holds one cross-process lease across reload, comparison and replacement.</summary>
    public IReadOnlyList<TenantProfile> UpdateProfiles(IReadOnlyList<TenantProfile> expected,
        Func<IReadOnlyList<TenantProfile>, IReadOnlyList<TenantProfile>> update, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = PartnerCustomerImport.Fingerprint(expected);
        using var lease = AcquireProfileWriteLease();
        var current = LoadClientProfiles();
        if (fingerprint != PartnerCustomerImport.Fingerprint(current))
            throw new ConfigurationException("Saved clients changed in another window. Refresh the saved-client list and review your edit again; no newer changes were overwritten.");
        var result = update(current);
        ValidateProfileReplacement(result);
        cancellationToken.ThrowIfCancellationRequested();
        WriteProfilesUnderLease(result);
        return result;
    }

    public IReadOnlyList<TenantProfile> ImportCustomers(PartnerCustomerPreview preview, IEnumerable<string> selectedTenantIds,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = selectedTenantIds.ToArray();
        if (selected.Length == 0) throw new ConfigurationException("Select at least one valid new client before saving the import.");
        using var lease = AcquireProfileWriteLease();
        var current = LoadClientProfiles();
        var result = PartnerCustomerImport.PrepareAdditions(preview, selected, current, now);
        ValidateProfileReplacement(result);
        cancellationToken.ThrowIfCancellationRequested();
        WriteProfilesUnderLease(result);
        return result;
    }

    private IDisposable AcquireProfileWriteLease()
    {
        try
        {
            _ = ReviewPathExists(_paths.DataDirectory, true);
            Directory.CreateDirectory(_paths.DataDirectory);
            var file = Path.Combine(_paths.DataDirectory, "profiles-write.lock");
            _ = ReviewPathExists(file, false);
            return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { throw new ConfigurationException("Saved clients are locked by another window or local storage is unavailable. Wait for that save to finish, then refresh and try again.", ex); }
    }

    private static void ValidateProfileReplacement(IReadOnlyList<TenantProfile> profiles)
    {
        if (profiles.Count > 5000) throw new ConfigurationException("This save would exceed 5,000 clients. No profiles were changed.");
        _ = PartnerCustomerImport.Fingerprint(profiles);
    }

    private void WriteProfilesUnderLease(IReadOnlyList<TenantProfile> profiles)
    {
        _ = ReviewPathExists(_paths.ProfilesFile, false);
        // Explicit nulls in existing profile fields must not turn into constructor defaults after an import.
        var node = ToolkitJson.ParseNode(JsonSerializer.Serialize(profiles, ProfileCopyOptions));
        WriteJsonAtomic(_paths.ProfilesFile, node);
    }
}
