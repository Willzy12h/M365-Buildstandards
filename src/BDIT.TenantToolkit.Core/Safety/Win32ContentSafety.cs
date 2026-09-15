using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
namespace BDIT.TenantToolkit.Core.Safety;

public static class Win32ContentSafety
{
    public const string Root = "/deviceAppManagement/mobileApps";
    public static string Path(string appId, string? version, string? file, Win32ContentAction action, JsonObject payload)
    {
        if (!ProfileValidator.IsGuid(appId) || !Enum.IsDefined(action)) throw new SafetyViolationException("Invalid Win32 content operation.");
        var root = Root + "/" + appId + "/microsoft.graph.win32LobApp/contentVersions";
        if (action == Win32ContentAction.CreateVersion)
        {
            if (payload.Count != 0 || version is not null || file is not null) throw new SafetyViolationException("Version creation takes no parameters.");
            return root;
        }
        if (string.IsNullOrEmpty(version) || version.Length > 20 || !version.All(char.IsAsciiDigit)) throw new SafetyViolationException("Invalid content version ID.");
        if (action == Win32ContentAction.PublishVersion)
        {
            if (payload.Count != 1 || payload["committedContentVersion"]?.ToString() != version || file is not null) throw new SafetyViolationException("Only the recorded content version may be published.");
            return Root + "/" + appId;
        }
        root += "/" + version + "/files";
        if (action == Win32ContentAction.CreateFile)
        {
            if (file is not null || payload.Count != 4 || payload["name"] is not JsonValue name || !name.TryGetValue<string>(out var fileName)
                || fileName != System.IO.Path.GetFileName(fileName) || fileName.Contains('/') || fileName.Contains('\\')
                || payload["size"] is not JsonValue size || !size.TryGetValue<long>(out var plain) || plain <= 0
                || payload["sizeEncrypted"] is not JsonValue encrypted || !encrypted.TryGetValue<long>(out var bytes) || bytes <= 0
                || payload["isDependency"]?.ToString() != "false")
                throw new SafetyViolationException("Invalid package file metadata.");
            return root;
        }
        if (!ProfileValidator.IsGuid(file) || payload.Count != 1 || payload["fileEncryptionInfo"] is not JsonObject info)
            throw new SafetyViolationException("Commit needs the recorded file ID and encryption metadata.");
        var names = new[] { "encryptionKey", "macKey", "initializationVector", "mac", "profileIdentifier", "fileDigest", "fileDigestAlgorithm" };
        if (info.Count != names.Length || names.Any(n => info[n] is not JsonValue)) throw new SafetyViolationException("Incomplete encryption metadata.");
        if (info["profileIdentifier"]?.ToString() != "ProfileVersion1" || info["fileDigestAlgorithm"]?.ToString() != "SHA256") throw new SafetyViolationException("Unsupported package encryption profile.");
        return root + "/" + file + "/commit";
    }
}
