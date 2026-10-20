using System.Security.Cryptography;
using BuildStandard.Core;

namespace BuildStandard.Engine.Scripts.Runner;

/// <summary>Fixed INT-088 runner bounds. Lower manifest limits prevail; nothing here can be raised by a manifest.</summary>
public static class ReadRunnerLimits
{
    public const string OutputKind = "scriptReadResult";
    public const int MaximumTimeoutSeconds = 1800;
    /// <summary>New report2 runs include authentication in their five-minute owned-host bound.</summary>
    public const int MaximumFamilyTimeoutSeconds = 300;
    public const int MaximumResultBytes = 32 * 1024 * 1024;
    public const int MaximumRequestBytes = 1024 * 1024;
    public const int MaximumProgressBytes = 1024 * 1024;
    public const int MaximumDiagnosticBytes = 256 * 1024;
    /// <summary>How long a cooperative stop may take before the owned process tree is ended.</summary>
    public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(5);
}

/// <summary>
/// The engine-embedded runner wrapper. Its bytes are anchored by <see cref="Sha256"/>, a constant in this assembly, never
/// by a caller-supplied file or by a digest a manifest chose. A schema 2 manifest must repeat this exact value.
/// </summary>
public static class ReadRunnerTemplate
{
    public const string Sha256 = "8ea5cb7575559d6c9c18987c7c881d9478112dd440f09358f7f5ae2b1c221550";
    private const string ResourceName = "Runner.ReadRunner.ps1";

    /// <summary>The wrapper's exact bytes, verified against <see cref="Sha256"/> on every call.</summary>
    public static byte[] Bytes()
    {
        using var stream = typeof(ReadRunnerTemplate).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new IntegrityException("The runner wrapper is missing from this build.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Sha256, StringComparison.Ordinal))
            throw new IntegrityException("The runner wrapper does not match this engine's pinned SHA-256.");
        return bytes;
    }
}
