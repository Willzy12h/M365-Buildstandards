using System.Security.Cryptography;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Reports;

/// <summary>
/// Bounded offline admission of supplied Exchange report evidence. Original bytes are immutable and retained separately
/// from typed presentation. A valid digest is not authentication of the recorded source, account or live-mode claims.
/// </summary>
public sealed class ExchangeReportImport
{
    private readonly byte[] _original;
    private readonly string _tenantId;

    private ExchangeReportImport(byte[] bytes, string expectedTenantId)
    {
        if (bytes.Length > ExchangeReportEvidenceSchema.MaximumBytes)
            throw new ConfigurationException("Exchange report evidence exceeds the 32 MiB reader limit.");
        _original = bytes.ToArray();
        _tenantId = expectedTenantId;
        _ = ReadEvidence();
        OriginalSha256 = Convert.ToHexStringLower(SHA256.HashData(_original));
    }

    public string OriginalSha256 { get; }

    public static ExchangeReportImport ReadFile(string file, string expectedTenantId)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ExchangeReportEvidenceSchema.MaximumBytes)
            throw new ConfigurationException("Exchange report evidence exceeds the 32 MiB reader limit.");
        using var reader = new BinaryReader(stream);
        return new(reader.ReadBytes(ExchangeReportEvidenceSchema.MaximumBytes + 1), expectedTenantId);
    }

    /// <summary>Returns a new validated projection; changing it cannot modify the retained original bytes.</summary>
    public ExchangeReportEvidence ReadEvidence()
    {
        string json;
        try { json = new UTF8Encoding(false, true).GetString(_original).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new ConfigurationException("Exchange report evidence is not valid UTF-8."); }
        return ExchangeReportEvidenceSchema.Read(json, _tenantId);
    }

    internal byte[] OriginalBytes() => _original.ToArray();
}
